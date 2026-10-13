using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using Npgsql;

namespace PosApi.Services;

public interface IReturnService
{
    Task<ReturnResponse> CreateReturnAsync(int userId, ReturnRequest req);
    Task<ReturnDetailResponse> GetReturnByIdAsync(int id);
    Task<PagedReturnResponse> ListReturnsAsync(int page, int pageSize);
}

public class ReturnService : IReturnService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ReturnService> _log;

    public ReturnService(AppDbContext db, ILogger<ReturnService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<ReturnResponse> CreateReturnAsync(int userId, ReturnRequest req)
    {
        if (req.Items == null || req.Items.Count == 0)
            throw new BusinessRuleException("Danh sách món trả không được trống");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _db.Database.CurrentTransaction;
            await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

            // 1. Lock Order & OrderItems to prevent concurrency issues (Double return)
            var order = await _db.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == req.OrderId);

            if (order == null) throw new KeyNotFoundException($"Đơn hàng {req.OrderId} không tồn tại");

            // Row Lock for OrderItems to prevent race conditions
            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            var itemIds = req.Items.Select(i => i.OrderItemId).ToArray();
            using (var lockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                lockCmd!.CommandText = "SELECT \"Id\" FROM \"OrderItems\" WHERE \"Id\" = ANY(@ids) ORDER BY \"Id\" FOR UPDATE";
                lockCmd.Parameters.Add(new NpgsqlParameter("@ids", itemIds));
                await lockCmd.ExecuteNonQueryAsync();
            }

            // Re-fetch items to get fresh state after lock
            var orderItems = await _db.OrderItems
                .Where(oi => itemIds.Contains(oi.Id))
                .ToListAsync();

            if (orderItems.Count != req.Items.Count)
                throw new BusinessRuleException("Một hoặc nhiều món trả không thuộc đơn hàng này");

            decimal totalReturnedAmount = 0m;
            var returnItems = new List<ReturnItem>();

            foreach (var reqItem in req.Items)
            {
                var oi = orderItems.First(x => x.Id == reqItem.OrderItemId);
                
                if (reqItem.Qty <= 0)
                    throw new BusinessRuleException($"Số lượng trả món {oi.Product.Name} phải lớn hơn 0");

                // Validation: Total returned cannot exceed original quantity
                if (oi.ReturnedQty + reqItem.Qty > oi.Qty)
                    throw new BusinessRuleException($"Số lượng trả vượt quá số lượng đã bán của món {oi.Product.Name} (Đã trả: {oi.ReturnedQty}, Bán: {oi.Qty})");

                // Pricing: Use historic UnitPrice (base unit) saved at time of sale
                // Note: We do not distribute total order discount/surcharge here as per identified risk.
                // We return the actual item value.
                var itemReturnTotal = oi.UnitPrice * reqItem.Qty;
                totalReturnedAmount += itemReturnTotal;

                // Update OrderItem state
                oi.ReturnedQty += reqItem.Qty;

                returnItems.Add(new ReturnItem
                {
                    OrderItem = oi,
                    Qty = reqItem.Qty,
                    UnitPrice = oi.UnitPrice,
                    Total = itemReturnTotal
                });
            }

            // 2. Create Return Record
            var returnCode = $"PHTRA-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper()}";
            var returnRecord = new ReturnRecord
            {
                Code = returnCode,
                OrderId = order.Id,
                Reason = req.Reason,
                TotalReturned = totalReturnedAmount,
                CreatedBy = userId,
                Items = returnItems
            };
            _db.Returns.Add(returnRecord);

            // 3. Restore Stock & Write Ledger
            foreach (var ri in returnItems)
            {
                var product = ri.OrderItem.Product;
                
                // Atomic stock increase
                product.CurrentStock += ri.Qty;

                _db.StockLedger.Add(new StockLedgerEntry
                {
                    ProductId = product.Id,
                    Qty = ri.Qty,
                    Balance = product.CurrentStock,
                    RefType = LedgerRefType.Return,
                    RefId = returnCode,
                    Note = $"Trả hàng từ đơn {order.Code}"
                });
            }

            // 4. Audit Log
            PosLogService.Write(_db, "RETURN_CREATED", userId, "return", returnCode,
                new { orderCode = order.Code, totalReturned = totalReturnedAmount, itemsCount = returnItems.Count });

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            return new ReturnResponse(returnCode, totalReturnedAmount, DateTime.UtcNow);
        });
    }

    public async Task<ReturnDetailResponse> GetReturnByIdAsync(int id)
    {
        var r = await _db.Returns
            .Include(x => x.Items)
            .ThenInclude(i => i.OrderItem.Product)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (r == null) throw new KeyNotFoundException($"Phiếu trả {id} không tồn tại");

        return new ReturnDetailResponse(
            r.Id, r.Code, r.OrderId, r.Reason, r.TotalReturned, r.CreatedAt,
            r.Items.Select(i => new ReturnItemResponse(
                i.Id, i.OrderItem.Product.Name, i.Qty, i.UnitPrice, i.Total)).ToList());
    }

    public async Task<PagedReturnResponse> ListReturnsAsync(int page, int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.Returns.AsNoTracking().OrderByDescending(x => x.CreatedAt);
        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        return new PagedReturnResponse(page, pageSize, total,
            (int)Math.Ceiling(total / (double)pageSize),
            items.Select(r => new ReturnBrief(r.Id, r.Code, r.OrderId, r.TotalReturned, r.CreatedAt)).ToList());
    }
}
