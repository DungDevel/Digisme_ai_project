using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using Npgsql;

namespace PosApi.Services;

public interface IInventoryService
{
    Task<PagedInventoryResponse> ListInventoryAsync(int page, int pageSize, int? productId, int? categoryId);
    Task<StockHistoryResponse> GetProductStockHistoryAsync(int productId);
    Task<PagedInventoryResponse> ListLowStockAsync(int page, int pageSize);
    Task<bool> AdjustStockAsync(int userId, int productId, InventoryAdjustRequest req);
    Task<InventoryReconcileResponse> ReconcileInventoryAsync(int userId, InventoryReconcileRequest? req);
}

public class InventoryService : IInventoryService
{
    private readonly AppDbContext _db;
    private readonly INotificationService? _notificationService;
    private readonly ILogger<InventoryService> _log;

    public InventoryService(AppDbContext db, INotificationService? notificationService = null, ILogger<InventoryService>? log = null)
    {
        _db = db;
        _notificationService = notificationService;
        _log = log ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<InventoryService>.Instance;
    }

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

    public async Task<PagedInventoryResponse> ListInventoryAsync(int page, int pageSize, int? productId, int? categoryId)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.Products.AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive);

        if (productId.HasValue) query = query.Where(p => p.Id == productId.Value);
        if (categoryId.HasValue) query = query.Where(p => p.CategoryId == categoryId.Value);

        var orderedQuery = query.OrderBy(p => p.Name).ThenBy(p => p.Id);

        var total = await orderedQuery.CountAsync();
        var products = await orderedQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var items = products.Select(p => new InventoryItemBrief(
            p.Id,
            p.Name,
            p.Barcode,
            p.CurrentStock,
            "Piece",
            p.CurrentStock <= p.MinStock)).ToList();

        return new PagedInventoryResponse(page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize), items);
    }

    public async Task<StockHistoryResponse> GetProductStockHistoryAsync(int productId)
    {
        var product = await _db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == productId);

        if (product == null) throw new KeyNotFoundException($"Sản phẩm {productId} không tồn tại");

        var ledger = await _db.StockLedger
            .AsNoTracking()
            .Where(l => l.ProductId == productId)
            .OrderByDescending(l => l.CreatedAt)
            .Take(100)
            .ToListAsync();

        var history = ledger.Select(l => new StockLedgerEntryBrief(
            (int)l.Id,
            l.CreatedAt,
            l.Qty,
            l.Balance,
            l.RefType.ToString(),
            l.RefId ?? "",
            l.Note ?? "")).ToList();

        return new StockHistoryResponse(product.Id, product.Name, product.CurrentStock, history);
    }

    public async Task<PagedInventoryResponse> ListLowStockAsync(int page, int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.CurrentStock <= p.MinStock);

        var orderedQuery = query.OrderBy(p => p.Name).ThenBy(p => p.Id);

        var total = await orderedQuery.CountAsync();
        var products = await orderedQuery.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var items = products.Select(p => new InventoryItemBrief(
            p.Id,
            p.Name,
            p.Barcode,
            p.CurrentStock,
            "Piece",
            true)).ToList();

        return new PagedInventoryResponse(page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize), items);
    }

    public async Task<bool> AdjustStockAsync(int userId, int productId, InventoryAdjustRequest req)
    {
        if (req.Amount == 0)
            throw new BusinessRuleException("Số lượng điều chỉnh không được bằng 0");

        if (string.IsNullOrWhiteSpace(req.Reason))
            throw new BusinessRuleException("Vui lòng cung cấp lý do điều chỉnh kho");

        var warehouseId = await ResolveWarehouseIdAsync(req.WarehouseId);

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _db.Database.CurrentTransaction;
            await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            using (var prodLockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                prodLockCmd!.CommandText = "SELECT \"Id\" FROM \"Products\" WHERE \"Id\" = @pid FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx1) prodLockCmd.Transaction = npgTx1;
                prodLockCmd.Parameters.Add(new NpgsqlParameter("@pid", productId));
                await prodLockCmd.ExecuteNonQueryAsync();
            }

            using (var whStockLockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                whStockLockCmd!.CommandText = "SELECT \"Id\" FROM \"WarehouseStocks\" WHERE \"WarehouseId\" = @whId AND \"ProductId\" = @pid FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx2) whStockLockCmd.Transaction = npgTx2;
                whStockLockCmd.Parameters.Add(new NpgsqlParameter("@whId", warehouseId));
                whStockLockCmd.Parameters.Add(new NpgsqlParameter("@pid", productId));
                await whStockLockCmd.ExecuteNonQueryAsync();
            }

            var product = await _db.Products.FindAsync(productId);
            if (product == null) throw new KeyNotFoundException($"Sản phẩm {productId} không tồn tại");

            var whStock = await _db.WarehouseStocks
                .FirstOrDefaultAsync(s => s.WarehouseId == warehouseId && s.ProductId == productId);

            decimal currentWhStock = whStock?.Stock ?? 0m;
            decimal newWhStock = currentWhStock + req.Amount;

            if (newWhStock < 0)
            {
                if (whStock == null)
                    throw new BusinessRuleException($"Kho không có tồn sản phẩm này, không thể điều chỉnh giảm ({req.Amount}).");
                else
                    throw new BusinessRuleException($"Số lượng điều chỉnh làm tồn kho tại kho này âm ({currentWhStock} + {req.Amount} = {newWhStock}).");
            }

            if (whStock == null)
            {
                whStock = new WarehouseStock
                {
                    WarehouseId = warehouseId,
                    ProductId = productId,
                    Stock = req.Amount,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.WarehouseStocks.Add(whStock);
            }
            else
            {
                whStock.Stock = newWhStock;
                whStock.UpdatedAt = DateTime.UtcNow;
            }

            var oldStock = product.CurrentStock;
            var newStock = oldStock + req.Amount;
            if (newStock < 0)
                throw new BusinessRuleException($"Số lượng điều chỉnh làm tổng tồn kho âm ({oldStock} + {req.Amount} = {newStock})");

            product.CurrentStock = newStock;

            var refId = $"ADJ-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper()}";

            _db.StockLedger.Add(new StockLedgerEntry
            {
                ProductId = product.Id,
                WarehouseId = warehouseId,
                Qty = req.Amount,
                Balance = newStock,
                RefType = LedgerRefType.Adjustment,
                RefId = refId,
                Note = req.Reason
            });

            PosLogService.Write(_db, "STOCK_ADJUSTED", userId, "product", product.Id.ToString(),
                new { delta = req.Amount, oldStock = oldStock, newStock = newStock, reason = req.Reason, warehouseId });

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            if (_notificationService != null && newStock <= product.MinStock)
            {
                try
                {
                    await _notificationService.NotifyLowStockAsync(product.Id, product.Name, product.Barcode, newStock, product.MinStock);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to dispatch LowStockAlert notification for product {ProductId}", product.Id);
                }
            }

            return true;
        });
    }

    public async Task<InventoryReconcileResponse> ReconcileInventoryAsync(int userId, InventoryReconcileRequest? req)
    {
        var targetWarehouseId = req?.TargetWarehouseId;
        var dryRun = req?.DryRun ?? true;

        if (targetWarehouseId.HasValue)
        {
            var wh = await _db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == targetWarehouseId.Value);
            if (wh == null)
                throw new KeyNotFoundException("Không tìm thấy kho được chỉ định");
            if (!wh.IsActive)
                throw new InvalidOperationException("Kho được chỉ định không hoạt động");
        }

        if (dryRun)
        {
            var report = await InventoryReconciler.ComputeDiffsAsync(_db, targetWarehouseId);
            return new InventoryReconcileResponse(
                true,
                report.TargetWarehouseId,
                report.TargetWarehouseName,
                report.TotalProductsChecked,
                report.ProductsWithSurplusCount,
                report.ProductsWithDeficitCount,
                report.Details.Select(d => new ReconcileItemDetailDto(d.ProductId, d.Barcode, d.Name, d.CurrentStock, d.TotalWhStock, d.Diff, d.ActionType)).ToList()
            );
        }
        else
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            ReconcileReportResult report = null!;
            int fixedCount = 0;

            await strategy.ExecuteAsync(async () =>
            {
                var existingTx = _db.Database.CurrentTransaction;
                await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

                report = await InventoryReconciler.ComputeDiffsAsync(_db, targetWarehouseId);
                await InventoryReconciler.ReconcileAsync(_db, targetWarehouseId);

                fixedCount = report.ProductsWithSurplusCount;

                PosLogService.Write(_db, "INVENTORY_RECONCILE_RUN", userId, "inventory", report.TargetWarehouseId.ToString(), new { report.TargetWarehouseId, report.TargetWarehouseName, fixedCount, report.ProductsWithDeficitCount });

                await _db.SaveChangesAsync();
                if (tx != null) await tx.CommitAsync();
            });

            return new InventoryReconcileResponse(
                false,
                report.TargetWarehouseId,
                report.TargetWarehouseName,
                report.TotalProductsChecked,
                report.ProductsWithSurplusCount,
                report.ProductsWithDeficitCount,
                report.Details.Select(d => new ReconcileItemDetailDto(d.ProductId, d.Barcode, d.Name, d.CurrentStock, d.TotalWhStock, d.Diff, d.ActionType)).ToList()
            );
        }
    }
}
