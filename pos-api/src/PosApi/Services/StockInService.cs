using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using Npgsql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace PosApi.Services;

public interface IStockInService
{
    Task<PagedStockInResponse> GetStockInsAsync(int? warehouseId, int? supplierId, StockInStatus? status, int page, int pageSize);
    Task<StockInResponse?> GetStockInByIdAsync(int id);
    Task<StockInResponse> CreateStockInDraftAsync(int userId, StockInCreateRequest req);
    Task<StockInResponse> UpdateStockInDraftAsync(int userId, int id, StockInUpdateRequest req);
    Task<StockInResponse> ConfirmStockInDraftAsync(int userId, int id, string? idempotencyKey);
    Task<StockInCreatedResponse> ConfirmStockInAsync(int userId, ConfirmStockInRequest req, string? idempotencyKey);
    Task<StockInResponse> CancelStockInAsync(int userId, int id);
    Task DeleteStockInAsync(int userId, int id);

    Task<SupplierInvoice> UploadInvoiceAsync(int userId, IFormFile file);
    Task<SupplierInvoice> GetInvoiceByIdAsync(int id);
    Task<SupplierInvoice> UpdateInvoiceItemsAsync(int id, List<StockInItemRequest> items);
    Task RejectInvoiceAsync(int id);
}

public class StockInService : IStockInService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const string IdempotencyEndpoint = "POST /api/v1/stock-ins/{id}/confirm";

    private readonly AppDbContext _db;
    private readonly ILogger<StockInService> _log;
    private readonly IConfiguration? _config;

    public StockInService(AppDbContext db, ILogger<StockInService> log, IConfiguration? config = null)
    {
        _db = db;
        _log = log;
        _config = config;
    }

    private static string ComputeRequestHash(ConfirmStockInRequest req)
    {
        var json = JsonSerializer.Serialize(req);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static decimal Round3(decimal val) => Math.Round(val, 3, MidpointRounding.AwayFromZero);
    private static decimal RoundMoney(decimal val) => Math.Round(val, 0, MidpointRounding.AwayFromZero);

    private async Task<int> ResolveWarehouseIdAsync(int? requestedWarehouseId)
    {
        if (requestedWarehouseId.HasValue && requestedWarehouseId.Value > 0)
        {
            var wh = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == requestedWarehouseId.Value && w.IsActive);
            if (wh == null) throw new KeyNotFoundException($"Kho hàng {requestedWarehouseId.Value} không tồn tại hoặc đã bị vô hiệu");
            return wh.Id;
        }
        var defaultWh = await _db.Warehouses.FirstOrDefaultAsync(w => w.IsDefault && w.IsActive)
                        ?? await _db.Warehouses.FirstOrDefaultAsync(w => w.IsActive);
        if (defaultWh == null)
        {
            defaultWh = new Warehouse
            {
                Code = "WH-DEFAULT",
                Name = "Kho mặc định",
                IsDefault = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.Warehouses.Add(defaultWh);
            await _db.SaveChangesAsync();
        }
        return defaultWh.Id;
    }

    private async Task<int> ResolveSupplierIdAsync(int? requestedSupplierId)
    {
        if (requestedSupplierId.HasValue && requestedSupplierId.Value > 0)
        {
            var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == requestedSupplierId.Value && x.IsActive);
            if (s == null) throw new KeyNotFoundException($"Nhà cung cấp {requestedSupplierId.Value} không tồn tại hoặc đã bị vô hiệu");
            return s.Id;
        }
        var defaultSupplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.IsActive)
                        ?? await _db.Suppliers.FirstOrDefaultAsync(s => s.Name == "Nhà cung cấp mặc định");
        if (defaultSupplier == null)
        {
            defaultSupplier = new Supplier
            {
                Code = "NCC-SYS-DEF",
                Name = "Nhà cung cấp mặc định",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.Suppliers.Add(defaultSupplier);
            await _db.SaveChangesAsync();
        }
        return defaultSupplier.Id;
    }

    public async Task<PagedStockInResponse> GetStockInsAsync(int? warehouseId, int? supplierId, StockInStatus? status, int page, int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.StockIns
            .AsNoTracking()
            .Include(s => s.Warehouse)
            .Include(s => s.Supplier)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .AsQueryable();

        if (warehouseId.HasValue)
            query = query.Where(s => s.WarehouseId == warehouseId.Value);
        if (supplierId.HasValue)
            query = query.Where(s => s.SupplierId == supplierId.Value);
        if (status.HasValue)
            query = query.Where(s => s.Status == status.Value);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(s => s.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var responses = items.Select(MapToStockInResponse).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        return new PagedStockInResponse(page, pageSize, total, totalPages, responses);
    }

    public async Task<StockInResponse?> GetStockInByIdAsync(int id)
    {
        var s = await _db.StockIns
            .AsNoTracking()
            .Include(s => s.Warehouse)
            .Include(s => s.Supplier)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(s => s.Id == id);

        return s == null ? null : MapToStockInResponse(s);
    }

    public async Task<StockInResponse> CreateStockInDraftAsync(int userId, StockInCreateRequest req)
    {
        if (req.Items == null || req.Items.Count == 0)
            throw new BusinessRuleException("Danh sách mặt hàng nhập không được trống");

        var warehouseId = await ResolveWarehouseIdAsync(req.WarehouseId);
        int? supplierId = null;
        string? supplierName = req.SupplierName;

        if (req.SupplierId.HasValue)
        {
            var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == req.SupplierId.Value && s.IsActive);
            if (supplier == null) throw new KeyNotFoundException($"Không tìm thấy nhà cung cấp {req.SupplierId.Value}");
            supplierId = supplier.Id;
            supplierName = supplier.Name;
        }

        var productIds = req.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();
        if (products.Count != productIds.Count)
            throw new BusinessRuleException("Một hoặc nhiều sản phẩm không tồn tại");

        decimal total = 0m;
        var stockInItems = new List<StockInItem>();

        foreach (var itemReq in req.Items)
        {
            var product = products.First(p => p.Id == itemReq.ProductId);
            if (!product.IsActive) throw new BusinessRuleException($"Sản phẩm '{product.Name}' đã bị vô hiệu");
            if (itemReq.Qty <= 0) throw new BusinessRuleException($"Số lượng nhập phải lớn hơn 0");
            if (itemReq.UnitCost < 0) throw new BusinessRuleException($"Giá nhập không được âm");

            var lineTotal = itemReq.Qty * itemReq.UnitCost;
            total += lineTotal;

            stockInItems.Add(new StockInItem
            {
                ProductId = product.Id,
                Qty = itemReq.Qty,
                UnitCost = itemReq.UnitCost,
                Total = lineTotal
            });
        }

        if (req.PaidAmount < 0 || req.PaidAmount > total)
            throw new BusinessRuleException("Số tiền đã trả lúc nhập không hợp lệ (phải từ 0 đến tổng tiền)");

        var code = $"PHNK-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper()}";

        var stockIn = new StockIn
        {
            Code = code,
            WarehouseId = warehouseId,
            SupplierId = supplierId,
            SupplierName = supplierName ?? "Unknown",
            Status = StockInStatus.Draft,
            Total = total,
            PaidAmount = req.PaidAmount,
            DueDate = req.DueDate,
            Note = req.Note?.Trim(),
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            Items = stockInItems
        };

        _db.StockIns.Add(stockIn);
        PosLogService.Write(_db, "STOCK_IN_CREATED", userId, "stockin", code, new { warehouseId, total, itemsCount = stockInItems.Count });
        await _db.SaveChangesAsync();

        await _db.Entry(stockIn).Reference(s => s.Warehouse).LoadAsync();
        await _db.Entry(stockIn).Reference(s => s.Supplier).LoadAsync();
        foreach (var item in stockIn.Items)
        {
            await _db.Entry(item).Reference(i => i.Product).LoadAsync();
        }

        return MapToStockInResponse(stockIn);
    }

    public async Task<StockInResponse> UpdateStockInDraftAsync(int userId, int id, StockInUpdateRequest req)
    {
        var stockIn = await _db.StockIns
            .Include(s => s.Warehouse)
            .Include(s => s.Supplier)
            .Include(s => s.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (stockIn == null) throw new KeyNotFoundException($"Phiếu nhập kho {id} không tồn tại");
        if (stockIn.Status != StockInStatus.Draft)
            throw new BusinessRuleException("Chỉ có thể sửa phiếu nhập kho khi ở trạng thái Draft");

        if (req.Items == null || req.Items.Count == 0)
            throw new BusinessRuleException("Danh sách mặt hàng không được trống");

        var warehouseId = await ResolveWarehouseIdAsync(req.WarehouseId ?? stockIn.WarehouseId);
        int? supplierId = stockIn.SupplierId;
        string? supplierName = req.SupplierName ?? stockIn.SupplierName;

        if (req.SupplierId.HasValue)
        {
            var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == req.SupplierId.Value && s.IsActive);
            if (supplier == null) throw new KeyNotFoundException($"Không tìm thấy nhà cung cấp {req.SupplierId.Value}");
            supplierId = supplier.Id;
            supplierName = supplier.Name;
        }

        var productIds = req.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();
        if (products.Count != productIds.Count)
            throw new BusinessRuleException("Một hoặc nhiều sản phẩm không tồn tại");

        decimal total = 0m;
        var newStockInItems = new List<StockInItem>();

        foreach (var itemReq in req.Items)
        {
            var product = products.First(p => p.Id == itemReq.ProductId);
            if (!product.IsActive) throw new BusinessRuleException($"Sản phẩm '{product.Name}' đã bị vô hiệu");
            if (itemReq.Qty <= 0) throw new BusinessRuleException($"Số lượng nhập phải lớn hơn 0");
            if (itemReq.UnitCost < 0) throw new BusinessRuleException($"Giá nhập không được âm");

            var lineTotal = itemReq.Qty * itemReq.UnitCost;
            total += lineTotal;

            newStockInItems.Add(new StockInItem
            {
                StockInId = stockIn.Id,
                ProductId = product.Id,
                Qty = itemReq.Qty,
                UnitCost = itemReq.UnitCost,
                Total = lineTotal
            });
        }

        var paidAmount = req.PaidAmount ?? stockIn.PaidAmount;
        if (paidAmount < 0 || paidAmount > total)
            throw new BusinessRuleException("Số tiền đã trả lúc nhập không hợp lệ (phải từ 0 đến tổng tiền)");

        stockIn.WarehouseId = warehouseId;
        stockIn.SupplierId = supplierId;
        stockIn.SupplierName = supplierName;
        stockIn.Total = total;
        stockIn.PaidAmount = paidAmount;
        stockIn.DueDate = req.DueDate ?? stockIn.DueDate;
        stockIn.Note = req.Note?.Trim() ?? stockIn.Note;

        _db.StockInItems.RemoveRange(stockIn.Items);
        stockIn.Items = newStockInItems;

        PosLogService.Write(_db, "STOCK_IN_UPDATED", userId, "stockin", stockIn.Code, new { total, itemsCount = newStockInItems.Count });
        await _db.SaveChangesAsync();

        await _db.Entry(stockIn).Reference(s => s.Warehouse).LoadAsync();
        await _db.Entry(stockIn).Reference(s => s.Supplier).LoadAsync();
        foreach (var item in stockIn.Items)
        {
            await _db.Entry(item).Reference(i => i.Product).LoadAsync();
        }

        return MapToStockInResponse(stockIn);
    }

    private async Task<StockIn> ConfirmStockInEntitiesAsync(
        int userId,
        int warehouseId,
        int? supplierId,
        string? supplierName,
        string? note,
        int? invoiceId,
        List<StockInItemRequest> items,
        StockIn? existingDraft = null,
        decimal paidAmount = 0m,
        DateTime? dueDate = null)
    {
        var wh = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == warehouseId && w.IsActive)
            ?? throw new KeyNotFoundException($"Kho hàng {warehouseId} không tồn tại hoặc đã bị vô hiệu");

        var productIds = items.Select(i => i.ProductId).Distinct().OrderBy(id => id).ToList();
        var conn = _db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

        using (var prodLockCmd = conn.CreateCommand() as NpgsqlCommand)
        {
            prodLockCmd!.CommandText = "SELECT \"Id\" FROM \"Products\" WHERE \"Id\" = ANY(@ids) ORDER BY \"Id\" FOR UPDATE";
            if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx1) prodLockCmd.Transaction = npgTx1;
            prodLockCmd.Parameters.Add(new NpgsqlParameter("@ids", productIds));
            await prodLockCmd.ExecuteNonQueryAsync();
        }

        using (var whStockLockCmd = conn.CreateCommand() as NpgsqlCommand)
        {
            whStockLockCmd!.CommandText = "SELECT \"Id\" FROM \"WarehouseStocks\" WHERE \"WarehouseId\" = @whId AND \"ProductId\" = ANY(@ids) ORDER BY \"Id\" FOR UPDATE";
            if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx2) whStockLockCmd.Transaction = npgTx2;
            whStockLockCmd.Parameters.Add(new NpgsqlParameter("@whId", warehouseId));
            whStockLockCmd.Parameters.Add(new NpgsqlParameter("@ids", productIds));
            await whStockLockCmd.ExecuteNonQueryAsync();
        }

        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();
        if (products.Count != productIds.Count)
            throw new BusinessRuleException("Một hoặc nhiều sản phẩm không tồn tại");

        var warehouseStocks = await _db.WarehouseStocks
            .Where(s => s.WarehouseId == warehouseId && productIds.Contains(s.ProductId))
            .ToListAsync();

        // Q14 Invariant Check: check if products violate CurrentStock == SUM(WarehouseStocks)
        var allProductWhStocks = await _db.WarehouseStocks
            .Where(s => productIds.Contains(s.ProductId))
            .GroupBy(s => s.ProductId)
            .Select(g => new { ProductId = g.Key, TotalStock = g.Sum(s => s.Stock) })
            .ToDictionaryAsync(x => x.ProductId, x => x.TotalStock);

        foreach (var prod in products)
        {
            var totalWh = allProductWhStocks.TryGetValue(prod.Id, out var ts) ? ts : 0m;
            if (prod.CurrentStock != totalWh)
            {
                throw new BusinessRuleException($"Sản phẩm '{prod.Name}' đang lệch bất biến tồn kho (CurrentStock: {prod.CurrentStock} != Tổng kho: {totalWh}). Vui lòng chạy công cụ cân bằng tồn kho (Q14) trước khi chốt phiếu nhập.");
            }
        }

        decimal total = 0m;
        var stockInItems = new List<StockInItem>();
        var ledgerEntries = new List<StockLedgerEntry>();

        StockIn stockIn;
        if (existingDraft != null)
        {
            stockIn = existingDraft;
            stockIn.WarehouseId = warehouseId;
            stockIn.SupplierId = supplierId;
            stockIn.SupplierName = supplierName ?? stockIn.SupplierName;
            stockIn.Note = note ?? stockIn.Note;
            stockIn.PaidAmount = paidAmount;
            stockIn.DueDate = dueDate;
            stockIn.Status = StockInStatus.Confirmed;
            _db.StockInItems.RemoveRange(stockIn.Items);
        }
        else
        {
            var code = $"PHNK-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper()}";
            stockIn = new StockIn
            {
                Code = code,
                SupplierInvoiceId = invoiceId,
                SupplierId = supplierId,
                SupplierName = supplierName ?? "Unknown",
                WarehouseId = warehouseId,
                Status = StockInStatus.Confirmed,
                PaidAmount = paidAmount,
                DueDate = dueDate,
                Note = note,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            };
            _db.StockIns.Add(stockIn);
        }

        foreach (var itemReq in items)
        {
            var product = products.First(p => p.Id == itemReq.ProductId);
            if (!product.IsActive) throw new BusinessRuleException($"Sản phẩm '{product.Name}' đã bị vô hiệu");
            if (itemReq.Qty <= 0) throw new BusinessRuleException($"Số lượng nhập phải lớn hơn 0");
            if (itemReq.UnitCost < 0) throw new BusinessRuleException($"Giá nhập không được âm");

            var lineTotal = itemReq.Qty * itemReq.UnitCost;
            total += lineTotal;

            // Q13: Weighted Average Cost calculation
            var oldStock = product.CurrentStock;
            var oldCost = product.CostPrice;
            decimal newCost;
            if (oldStock <= 0m)
            {
                newCost = itemReq.UnitCost;
            }
            else
            {
                newCost = Math.Round((oldStock * oldCost + itemReq.Qty * itemReq.UnitCost) / (oldStock + itemReq.Qty), 4);
            }
            var updateCostFlag = _config?.GetValue<bool>("Inventory:UpdateCostOnStockIn", false) ?? false;
            if (updateCostFlag)
            {
                product.CostPrice = newCost;
            }
            decimal costAfterVal = updateCostFlag ? newCost : oldCost;

            stockInItems.Add(new StockInItem
            {
                StockIn = stockIn,
                ProductId = product.Id,
                Qty = itemReq.Qty,
                UnitCost = itemReq.UnitCost,
                Total = lineTotal,
                CostBefore = oldCost,
                CostAfter = costAfterVal
            });

            product.CurrentStock = Round3(product.CurrentStock + itemReq.Qty);

            var whStock = warehouseStocks.FirstOrDefault(s => s.ProductId == product.Id);
            if (whStock == null)
            {
                whStock = new WarehouseStock
                {
                    WarehouseId = warehouseId,
                    ProductId = product.Id,
                    Stock = itemReq.Qty,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.WarehouseStocks.Add(whStock);
                warehouseStocks.Add(whStock);
            }
            else
            {
                whStock.Stock = Round3(whStock.Stock + itemReq.Qty);
                whStock.UpdatedAt = DateTime.UtcNow;
            }

            ledgerEntries.Add(new StockLedgerEntry
            {
                ProductId = product.Id,
                WarehouseId = warehouseId,
                Qty = itemReq.Qty,
                Balance = product.CurrentStock,
                RefType = LedgerRefType.Purchase,
                RefId = stockIn.Code,
                Note = $"Nhập kho {stockIn.Code} tại kho {wh.Name}"
            });
        }

        if (paidAmount < 0 || paidAmount > total)
            throw new BusinessRuleException("Số tiền đã trả lúc nhập không hợp lệ (phải từ 0 đến tổng tiền)");

        var remaining = total - paidAmount;
        int? resolvedSupplierId = supplierId;
        if ((remaining > 0 || paidAmount > 0) && (!resolvedSupplierId.HasValue || resolvedSupplierId.Value <= 0))
        {
            resolvedSupplierId = await ResolveSupplierIdAsync(supplierId);
            var supObj = await _db.Suppliers.FindAsync(resolvedSupplierId.Value);
            supplierName = supplierName ?? supObj?.Name ?? "Nhà cung cấp mặc định";
        }

        stockIn.SupplierId = resolvedSupplierId;
        stockIn.SupplierName = supplierName ?? stockIn.SupplierName;
        stockIn.Total = total;
        stockIn.PaidAmount = paidAmount;
        stockIn.DueDate = dueDate;
        stockIn.Items = stockInItems;
        _db.StockLedger.AddRange(ledgerEntries);

        await _db.SaveChangesAsync();

        if (remaining > 0 && resolvedSupplierId.HasValue)
        {
            var supplierDebt = new SupplierDebt
            {
                SupplierId = resolvedSupplierId.Value,
                Amount = remaining,
                PaidAmount = 0m,
                Status = DebtStatus.Open,
                ReferenceType = "StockIn",
                ReferenceId = stockIn.Code,
                StockInId = stockIn.Id,
                DueDate = dueDate,
                Note = $"Công nợ nhập kho {stockIn.Code}"
            };
            _db.SupplierDebts.Add(supplierDebt);
        }

        if (paidAmount > 0 && resolvedSupplierId.HasValue)
        {
            var voucherCode = $"PC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper()}";
            var cashVoucher = new CashVoucher
            {
                Code = voucherCode,
                Type = CashVoucherType.Payment,
                PartnerType = PartnerType.Supplier,
                PartnerId = resolvedSupplierId.Value,
                Amount = paidAmount,
                PaymentMethod = PaymentMethod.Cash,
                ReferenceType = "StockIn",
                ReferenceId = stockIn.Code,
                Status = CashVoucherStatus.Confirmed,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
                ConfirmedAt = DateTime.UtcNow,
                Note = $"Thanh toán một phần lúc nhập kho {stockIn.Code}"
            };
            _db.CashVouchers.Add(cashVoucher);

            var lastCashEntry = await _db.CashLedger.OrderByDescending(x => x.Id).FirstOrDefaultAsync();
            var currentCashBalance = lastCashEntry?.Balance ?? 0m;
            var newCashBalance = currentCashBalance - paidAmount;
            _db.CashLedger.Add(new CashLedgerEntry
            {
                Amount = -paidAmount,
                Balance = newCashBalance,
                PaymentMethod = PaymentMethod.Cash,
                RefType = "CashVoucher",
                RefId = voucherCode,
                Note = $"Chi thanh toán nhập kho {stockIn.Code}",
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();

        if (invoiceId.HasValue)
        {
            var invoice = await _db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId.Value);
            if (invoice != null)
            {
                invoice.Status = SupplierInvoiceStatus.Confirmed;
                invoice.StockInId = stockIn.Id;
                await _db.SaveChangesAsync();
            }
        }

        PosLogService.Write(_db, "STOCK_IN_CONFIRMED", userId, "stockin", stockIn.Code, new { warehouseId, total, itemsCount = stockInItems.Count });
        await _db.SaveChangesAsync();

        return stockIn;
    }

    public async Task<StockInResponse> ConfirmStockInDraftAsync(int userId, int id, string? idempotencyKey)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _db.Database.CurrentTransaction;
            await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

            var stockIn = await _db.StockIns
                .Include(s => s.Warehouse)
                .Include(s => s.Supplier)
                .Include(s => s.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (stockIn == null) throw new KeyNotFoundException($"Phiếu nhập kho {id} không tồn tại");
            if (stockIn.Status != StockInStatus.Draft)
                throw new BusinessRuleException("Chỉ có thể chốt phiếu nhập kho khi ở trạng thái Draft");

            var itemsReq = stockIn.Items.Select(i => new StockInItemRequest(i.ProductId, i.Qty, i.UnitCost)).ToList();
            await ConfirmStockInEntitiesAsync(userId, stockIn.WarehouseId, stockIn.SupplierId, stockIn.SupplierName, stockIn.Note, stockIn.SupplierInvoiceId, itemsReq, stockIn);

            if (tx != null) await tx.CommitAsync();

            await _db.Entry(stockIn).Reference(s => s.Warehouse).LoadAsync();
            await _db.Entry(stockIn).Reference(s => s.Supplier).LoadAsync();
            foreach (var item in stockIn.Items)
            {
                await _db.Entry(item).Reference(i => i.Product).LoadAsync();
            }

            return MapToStockInResponse(stockIn);
        });
    }

    public async Task<StockInCreatedResponse> ConfirmStockInAsync(int userId, ConfirmStockInRequest req, string? idempotencyKey)
    {
        if (req.Items == null || req.Items.Count == 0)
            throw new BusinessRuleException("Danh sách mặt hàng nhập không được trống");

        var requestHash = ComputeRequestHash(req);
        var hasKey = !string.IsNullOrWhiteSpace(idempotencyKey);
        if (hasKey)
        {
            var existing = await _db.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == idempotencyKey && r.UserId == userId && r.ExpiresAt > DateTime.UtcNow);
            if (existing is not null)
            {
                if (!string.IsNullOrEmpty(existing.RequestHash) && existing.RequestHash != requestHash)
                    throw new ConflictException("Idempotency-Key đã được dùng cho nội dung khác");
                if (existing.ResultJson is not null)
                    return JsonSerializer.Deserialize<StockInCreatedResponse>(existing.ResultJson, JsonOpts)!;
            }
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _db.Database.CurrentTransaction;
            await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

            if (hasKey)
            {
                try
                {
                    await _db.IdempotencyRecords.AddAsync(new IdempotencyRecord
                    {
                        Key = idempotencyKey!,
                        UserId = userId,
                        Endpoint = IdempotencyEndpoint,
                        RequestHash = requestHash,
                        ResultJson = null!,
                        StatusCode = 201,
                        ExpiresAt = DateTime.UtcNow.AddHours(24)
                    });
                    await _db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    if (tx != null) await tx.RollbackAsync();
                    if (hasKey)
                    {
                        var winner = await _db.IdempotencyRecords
                            .AsNoTracking()
                            .FirstOrDefaultAsync(r => r.Key == idempotencyKey && r.UserId == userId && r.ExpiresAt > DateTime.UtcNow);
                        if (winner is not null && winner.ResultJson is not null)
                            return JsonSerializer.Deserialize<StockInCreatedResponse>(winner.ResultJson, JsonOpts)!;
                        throw new BusinessRuleException("Yêu cầu trùng đang được xử lý — vui lòng thử lại sau (409)");
                    }
                    throw;
                }
            }

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using (var lockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                lockCmd!.CommandText = "SELECT \"Id\" FROM \"SupplierInvoices\" WHERE \"Id\" = @id FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx1) lockCmd.Transaction = npgTx1;
                lockCmd.Parameters.Add(new NpgsqlParameter("@id", req.InvoiceId));
                await lockCmd.ExecuteNonQueryAsync();
            }

            var invoice = await _db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == req.InvoiceId);
            if (invoice == null) throw new KeyNotFoundException($"Hóa đơn {req.InvoiceId} không tồn tại");
            if (invoice.Status != SupplierInvoiceStatus.PendingReview)
                throw new BusinessRuleException($"Hóa đơn chỉ có thể xác nhận khi ở trạng thái Pending Review (Hiện tại: {invoice.Status})");

            var warehouseId = await ResolveWarehouseIdAsync(null);
            int? supplierId = null;
            string? supplierName = req.SupplierName ?? (invoice.OcrResultJson != null ? "OCR extracted" : "Unknown");

            if (req.SupplierId.HasValue)
            {
                var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == req.SupplierId.Value && s.IsActive);
                if (supplier == null) throw new KeyNotFoundException($"Không tìm thấy nhà cung cấp {req.SupplierId.Value}");
                supplierId = supplier.Id;
                supplierName = supplier.Name;
            }

            var stockIn = await ConfirmStockInEntitiesAsync(userId, warehouseId, supplierId, supplierName, req.Note, req.InvoiceId, req.Items, null, req.PaidAmount, req.DueDate);

            var response = new StockInCreatedResponse(stockIn.Id, stockIn.Code, stockIn.Total, stockIn.CreatedAt);

            if (hasKey)
            {
                var idem = await _db.IdempotencyRecords.FirstAsync(r => r.Key == idempotencyKey && r.UserId == userId);
                idem.ResultJson = JsonSerializer.Serialize(response, JsonOpts);
                idem.StatusCode = 201;
                await _db.SaveChangesAsync();
            }

            if (tx != null) await tx.CommitAsync();
            return response;
        });
    }

    public async Task<StockInResponse> CancelStockInAsync(int userId, int id)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _db.Database.CurrentTransaction;
            await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

            var stockIn = await _db.StockIns
                .Include(s => s.Warehouse)
                .Include(s => s.Supplier)
                .Include(s => s.Items).ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (stockIn == null) throw new KeyNotFoundException($"Phiếu nhập kho {id} không tồn tại");
            if (stockIn.IsBackfilled)
                throw new BusinessRuleException("Không thể hủy phiếu nhập cũ đã được backfill từ dữ liệu lịch sử. Vui lòng sử dụng kiểm kê kho hoặc phiếu điều chỉnh để điều chỉnh tồn kho.");
            if (stockIn.Status != StockInStatus.Confirmed)
                throw new BusinessRuleException($"Chỉ có thể hủy phiếu nhập kho ở trạng thái Confirmed (Hiện tại: {stockIn.Status})");

            var productIds = stockIn.Items.Select(i => i.ProductId).Distinct().OrderBy(id => id).ToList();
            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            using (var prodLockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                prodLockCmd!.CommandText = "SELECT \"Id\" FROM \"Products\" WHERE \"Id\" = ANY(@ids) ORDER BY \"Id\" FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx1) prodLockCmd.Transaction = npgTx1;
                prodLockCmd.Parameters.Add(new NpgsqlParameter("@ids", productIds));
                await prodLockCmd.ExecuteNonQueryAsync();
            }

            using (var whStockLockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                whStockLockCmd!.CommandText = "SELECT \"Id\" FROM \"WarehouseStocks\" WHERE \"WarehouseId\" = @whId AND \"ProductId\" = ANY(@ids) FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx2) whStockLockCmd.Transaction = npgTx2;
                whStockLockCmd.Parameters.Add(new NpgsqlParameter("@whId", stockIn.WarehouseId));
                whStockLockCmd.Parameters.Add(new NpgsqlParameter("@ids", productIds));
                await whStockLockCmd.ExecuteNonQueryAsync();
            }

            var warehouseStocks = await _db.WarehouseStocks
                .Where(s => s.WarehouseId == stockIn.WarehouseId && productIds.Contains(s.ProductId))
                .ToListAsync();

            var reverseLedgerEntries = new List<StockLedgerEntry>();

            foreach (var item in stockIn.Items)
            {
                var product = item.Product;
                var whStock = warehouseStocks.FirstOrDefault(s => s.ProductId == product.Id);
                decimal currentWhStock = whStock?.Stock ?? 0m;

                if (product.CurrentStock < item.Qty || currentWhStock < item.Qty)
                {
                    throw new BusinessRuleException($"Không thể hủy phiếu nhập kho do tồn kho (hoặc tồn tại kho {stockIn.Warehouse.Name}) không đủ để trừ sản phẩm '{product.Name}' (Tồn tổng: {product.CurrentStock}, Tồn kho: {currentWhStock}, Cần hủy: {item.Qty})");
                }

                product.CurrentStock = Round3(product.CurrentStock - item.Qty);
                if (whStock != null)
                {
                    whStock.Stock = Round3(whStock.Stock - item.Qty);
                    whStock.UpdatedAt = DateTime.UtcNow;
                }

                // Q13.3: Restore CostPrice if unchanged
                if (item.CostAfter.HasValue && product.CostPrice == item.CostAfter.Value)
                {
                    if (item.CostBefore.HasValue)
                    {
                        product.CostPrice = item.CostBefore.Value;
                    }
                }
                else if (item.CostAfter.HasValue)
                {
                    PosLogService.Write(_db, "COST_PRICE_RETAINED_ON_CANCEL", userId, "product", product.Id.ToString(), new { item.CostAfter, currentCost = product.CostPrice, message = "CostPrice was modified by subsequent transactions; retained current cost." });
                }

                reverseLedgerEntries.Add(new StockLedgerEntry
                {
                    ProductId = product.Id,
                    WarehouseId = stockIn.WarehouseId,
                    Qty = -item.Qty,
                    Balance = product.CurrentStock,
                    RefType = LedgerRefType.Purchase,
                    RefId = stockIn.Code,
                    Note = $"Hủy phiếu nhập kho {stockIn.Code}"
                });
            }

            stockIn.Status = StockInStatus.Cancelled;
            _db.StockLedger.AddRange(reverseLedgerEntries);

            var linkedDebts = await _db.SupplierDebts.Where(d => d.StockInId == stockIn.Id || d.ReferenceId == stockIn.Code).ToListAsync();
            if (linkedDebts.Any(d => d.PaidAmount > 0))
            {
                throw new BusinessRuleException("Không thể hủy phiếu nhập vì đã có phát sinh thanh toán phân bổ vào công nợ của phiếu này");
            }
            _db.SupplierDebts.RemoveRange(linkedDebts);

            var linkedVouchers = await _db.CashVouchers.Where(v => v.ReferenceType == "StockIn" && v.ReferenceId == stockIn.Code).ToListAsync();
            foreach (var v in linkedVouchers)
            {
                if (v.Status == CashVoucherStatus.Confirmed)
                {
                    var lastCash = await _db.CashLedger.OrderByDescending(x => x.Id).FirstOrDefaultAsync();
                    var bal = (lastCash?.Balance ?? 0m) + v.Amount;
                    _db.CashLedger.Add(new CashLedgerEntry
                    {
                        Amount = v.Amount,
                        Balance = bal,
                        PaymentMethod = v.PaymentMethod,
                        RefType = "CashVoucherCancel",
                        RefId = v.Code,
                        Note = $"Hủy phiếu chi thanh toán nhập kho {stockIn.Code}",
                        CreatedBy = userId,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                _db.CashVouchers.Remove(v);
            }

            if (stockIn.SupplierInvoiceId.HasValue)
            {
                var invoice = await _db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == stockIn.SupplierInvoiceId.Value);
                if (invoice != null)
                {
                    invoice.Status = SupplierInvoiceStatus.PendingReview;
                    invoice.StockInId = null;
                }
            }

            PosLogService.Write(_db, "STOCK_IN_CANCELLED", userId, "stockin", stockIn.Code, new { stockIn.Code, stockIn.WarehouseId });
            await _db.SaveChangesAsync();

            if (tx != null) await tx.CommitAsync();
            return MapToStockInResponse(stockIn);
        });
    }

    public async Task DeleteStockInAsync(int userId, int id)
    {
        var stockIn = await _db.StockIns.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id);
        if (stockIn == null) throw new KeyNotFoundException($"Phiếu nhập kho {id} không tồn tại");
        if (stockIn.Status != StockInStatus.Draft)
            throw new BusinessRuleException("Chỉ có thể xóa phiếu nhập kho khi ở trạng thái Draft");

        _db.StockIns.Remove(stockIn);
        PosLogService.Write(_db, "STOCK_IN_DELETED", userId, "stockin", stockIn.Code);
        await _db.SaveChangesAsync();
    }

    public async Task<SupplierInvoice> UploadInvoiceAsync(int userId, IFormFile file)
    {
        var uploadsDir = Path.Combine(Directory.GetCurrentDirectory(), "Uploads");
        if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

        var fileName = $"{Guid.NewGuid()}_{file.FileName}";
        var filePath = Path.Combine(uploadsDir, fileName);
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var mockOcr = new
        {
            invoiceId = 0,
            items = new[]
            {
                new { name = "Sample Product", unit = "cái", qty = 10, unitPrice = 10000m, confidence = 0.98m }
            },
            overallConfidence = 0.95m,
            engine = "mock",
            reviewRequired = false
        };

        var ocrJson = JsonSerializer.Serialize(mockOcr, JsonOpts);

        var invoice = new SupplierInvoice
        {
            ImagePath = fileName,
            OcrResultJson = ocrJson,
            OcrConfidence = 0.95m,
            Status = SupplierInvoiceStatus.PendingReview,
            CreatedBy = userId
        };

        _db.SupplierInvoices.Add(invoice);
        await _db.SaveChangesAsync();

        PosLogService.Write(_db, "SUPPLIER_INVOICE_UPLOADED", userId, "supplierinvoice", invoice.Id.ToString(), new { imagePath = fileName });
        await _db.SaveChangesAsync();

        return invoice;
    }

    public async Task<SupplierInvoice> GetInvoiceByIdAsync(int id)
    {
        var invoice = await _db.SupplierInvoices
            .Include(i => i.StockIn)
            .FirstOrDefaultAsync(i => i.Id == id);
        if (invoice == null) throw new KeyNotFoundException($"Hóa đơn {id} không tồn tại");
        return invoice;
    }

    public async Task<SupplierInvoice> UpdateInvoiceItemsAsync(int id, List<StockInItemRequest> items)
    {
        var invoice = await _db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == id);
        if (invoice == null) throw new KeyNotFoundException($"Hóa đơn {id} không tồn tại");
        if (invoice.Status != SupplierInvoiceStatus.PendingReview)
            throw new BusinessRuleException("Chỉ có thể chỉnh sửa hóa đơn ở trạng thái Pending Review");

        var ocrObj = new
        {
            invoiceId = invoice.Id,
            items = items.Select(i => new { productId = i.ProductId, qty = i.Qty, unitPrice = i.UnitCost, confidence = 1.0m }),
            overallConfidence = 1.0m,
            engine = "manual_override",
            reviewRequired = false
        };

        invoice.OcrResultJson = JsonSerializer.Serialize(ocrObj, JsonOpts);
        await _db.SaveChangesAsync();
        return invoice;
    }

    public async Task RejectInvoiceAsync(int id)
    {
        var invoice = await _db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == id);
        if (invoice == null) throw new KeyNotFoundException($"Hóa đơn {id} không tồn tại");
        if (invoice.Status != SupplierInvoiceStatus.PendingReview)
            throw new BusinessRuleException("Chỉ có thể từ chối hóa đơn ở trạng thái Pending Review");

        invoice.Status = SupplierInvoiceStatus.Rejected;
        await _db.SaveChangesAsync();
    }

    private static StockInResponse MapToStockInResponse(StockIn s) => new(
        s.Id,
        s.Code,
        s.SupplierInvoiceId,
        s.SupplierId,
        s.SupplierName,
        s.WarehouseId,
        s.Warehouse?.Name ?? "N/A",
        s.Status,
        s.Total,
        s.PaidAmount,
        Math.Max(0m, s.Total - s.PaidAmount),
        s.DueDate,
        s.Note,
        s.CreatedBy,
        s.CreatedAt,
        s.Items.Select(i => new StockInItemResponse(
            i.Id,
            i.ProductId,
            i.Product?.Barcode ?? string.Empty,
            i.Product?.Name ?? string.Empty,
            i.Qty,
            i.UnitCost,
            i.Total
        )).ToList()
    );
}
