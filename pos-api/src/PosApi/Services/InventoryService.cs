using Microsoft.EntityFrameworkCore;
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

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _db.Database.CurrentTransaction;
            await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            using (var lockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                lockCmd!.CommandText = "SELECT \"Id\" FROM \"Products\" WHERE \"Id\" = @pid FOR UPDATE";
                lockCmd.Parameters.Add(new NpgsqlParameter("@pid", productId));
                await lockCmd.ExecuteScalarAsync();
            }

            var product = await _db.Products.FindAsync(productId);
            if (product == null) throw new KeyNotFoundException($"Sản phẩm {productId} không tồn tại");

            var oldStock = product.CurrentStock;
            var newStock = oldStock + req.Amount;
            if (newStock < 0)
                throw new BusinessRuleException($"Số lượng điều chỉnh làm tồn kho âm ({oldStock} + {req.Amount} = {newStock})");

            product.CurrentStock = newStock;

            var refId = $"ADJ-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper()}";

            _db.StockLedger.Add(new StockLedgerEntry
            {
                ProductId = product.Id,
                Qty = req.Amount,
                Balance = newStock,
                RefType = LedgerRefType.Adjustment,
                RefId = refId,
                Note = req.Reason
            });

            PosLogService.Write(_db, "STOCK_ADJUSTED", userId, "product", product.Id.ToString(),
                new { delta = req.Amount, oldStock = oldStock, newStock = newStock, reason = req.Reason });

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
}
