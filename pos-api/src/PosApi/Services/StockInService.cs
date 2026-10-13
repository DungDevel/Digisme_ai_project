using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using Npgsql;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PosApi.Services;

public interface IStockInService
{
    Task<StockInCreatedResponse> ConfirmStockInAsync(int userId, ConfirmStockInRequest req, string? idempotencyKey);
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

    public StockInService(AppDbContext db, ILogger<StockInService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<StockInCreatedResponse> ConfirmStockInAsync(int userId, ConfirmStockInRequest req, string? idempotencyKey)
    {
        // ── Idempotency Fast-path ──
        var hasKey = !string.IsNullOrWhiteSpace(idempotencyKey);
        if (hasKey)
        {
            var existing = await _db.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == idempotencyKey && r.UserId == userId && r.ExpiresAt > DateTime.UtcNow);
            if (existing is not null && existing.ResultJson is not null)
            {
                return JsonSerializer.Deserialize<StockInCreatedResponse>(existing.ResultJson, JsonOpts)!;
            }
        }

        if (req.Items == null || req.Items.Count == 0)
            throw new BusinessRuleException("Danh sách mặt hàng nhập không được trống");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _db.Database.CurrentTransaction;
            await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

            // ── Idempotency Placeholder ──
            if (hasKey)
            {
                try
                {
                    await _db.IdempotencyRecords.AddAsync(new IdempotencyRecord
                    {
                        Key = idempotencyKey!,
                        UserId = userId,
                        Endpoint = IdempotencyEndpoint,
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

            // ── Lock SupplierInvoice ──
            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            using (var lockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                lockCmd!.CommandText = "SELECT \"Id\" FROM \"SupplierInvoices\" WHERE \"Id\" = @id FOR UPDATE";
                lockCmd.Parameters.Add(new NpgsqlParameter("@id", req.InvoiceId));
                await lockCmd.ExecuteNonQueryAsync();
            }

            var invoice = await _db.SupplierInvoices.FirstOrDefaultAsync(i => i.Id == req.InvoiceId);
            if (invoice == null) throw new KeyNotFoundException($"Hóa đơn {req.InvoiceId} không tồn tại");
            if (invoice.Status != SupplierInvoiceStatus.PendingReview)
                throw new BusinessRuleException($"Hóa đơn chỉ có thể xác nhận khi ở trạng thái Pending Review (Hiện tại: {invoice.Status})");

            // ── Lock Products (Ordered by ID to prevent deadlocks) ──
            var productIds = req.Items.Select(i => i.ProductId).Distinct().OrderBy(id => id).ToList();
            using (var prodLockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                prodLockCmd!.CommandText = "SELECT \"Id\" FROM \"Products\" WHERE \"Id\" = ANY(@ids) ORDER BY \"Id\" FOR UPDATE";
                prodLockCmd.Parameters.Add(new NpgsqlParameter("@ids", productIds));
                await prodLockCmd.ExecuteNonQueryAsync();
            }

            var products = await _db.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync();

            if (products.Count != productIds.Count)
                throw new BusinessRuleException("Một hoặc nhiều sản phẩm trong danh sách nhập không tồn tại");

            // ── Business Logic: Calculate Totals & Update Stock ──
            decimal total = 0m;
            var stockInItems = new List<StockInItem>();
            var ledgerEntries = new List<StockLedgerEntry>();

            foreach (var itemReq in req.Items)
            {
                var product = products.First(p => p.Id == itemReq.ProductId);
                if (!product.IsActive) throw new BusinessRuleException($"Sản phẩm '{product.Name}' đã bị vô hiệu");
                if (itemReq.Qty <= 0) throw new BusinessRuleException($"Số lượng nhập cho '{product.Name}' phải lớn hơn 0");
                if (itemReq.UnitCost < 0) throw new BusinessRuleException($"Giá nhập cho '{product.Name}' không được âm");

                var lineTotal = itemReq.Qty * itemReq.UnitCost;
                total += lineTotal;

                stockInItems.Add(new StockInItem
                {
                    ProductId = product.Id,
                    Qty = itemReq.Qty,
                    UnitCost = itemReq.UnitCost,
                    Total = lineTotal
                });

                // Update current stock
                product.CurrentStock += itemReq.Qty;

                ledgerEntries.Add(new StockLedgerEntry
                {
                    ProductId = product.Id,
                    Qty = itemReq.Qty,
                    Balance = product.CurrentStock,
                    RefType = LedgerRefType.Purchase,
                    Note = $"Nhập kho từ hóa đơn {req.InvoiceId}"
                });
            }

            // ── Generate StockIn Code (PHNK-yyyyMMdd-nnnn) ──
            var stockInCode = $"PHNK-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper()}";

            var stockIn = new StockIn
            {
                Code = stockInCode,
                SupplierInvoiceId = req.InvoiceId,
                SupplierName = req.SupplierName ?? (invoice.OcrResultJson != null ? "OCR extracted" : "Unknown"),
                Total = total,
                Note = req.Note,
                CreatedAt = DateTime.UtcNow,
                Items = stockInItems
            };

            _db.StockIns.Add(stockIn);
            _db.StockLedger.AddRange(ledgerEntries);

            foreach (var entry in ledgerEntries)
            {
                entry.RefId = stockInCode;
            }

            invoice.Status = SupplierInvoiceStatus.Confirmed;
            invoice.StockInId = stockIn.Id;

            PosLogService.Write(_db, "STOCK_IN_CONFIRMED", userId, "stockin", stockInCode,
                new { invoiceId = req.InvoiceId, total, itemsCount = stockInItems.Count });

            await _db.SaveChangesAsync();

            invoice.StockInId = stockIn.Id;
            await _db.SaveChangesAsync();

            var response = new StockInCreatedResponse(stockIn.Id, stockInCode, total, stockIn.CreatedAt);

            if (hasKey)
            {
                var idem = await _db.IdempotencyRecords
                    .FirstAsync(r => r.Key == idempotencyKey && r.UserId == userId);
                idem.ResultJson = JsonSerializer.Serialize(response, JsonOpts);
                idem.StatusCode = 201;
                await _db.SaveChangesAsync();
            }

            if (tx != null) await tx.CommitAsync();
            return response;
        });
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
}
