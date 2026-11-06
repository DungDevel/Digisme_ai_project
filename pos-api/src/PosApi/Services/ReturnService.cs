using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using Npgsql;

namespace PosApi.Services;

public interface IReturnService
{
    Task<ReturnResponse> CreateReturnAsync(int userId, ReturnRequest req, string? idempotencyKey = null);
    Task<ReturnDetailResponse> GetReturnByIdAsync(int id);
    Task<PagedReturnResponse> ListReturnsAsync(int page, int pageSize);
}

public class ReturnService : IReturnService
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new(System.Text.Json.JsonSerializerDefaults.Web);
    private const string IdempotencyEndpoint = "POST /api/v1/returns";

    private readonly AppDbContext _db;
    private readonly ILogger<ReturnService> _log;

    public ReturnService(AppDbContext db, ILogger<ReturnService> log)
    {
        _db = db;
        _log = log;
    }

    private static string ComputeRequestHash(ReturnRequest req)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(req);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public async Task<ReturnResponse> CreateReturnAsync(int userId, ReturnRequest req, string? idempotencyKey = null)
    {
        if (req.Items == null || req.Items.Count == 0)
            throw new BusinessRuleException("Danh sách món trả không được trống");

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
                {
                    throw new ConflictException("Idempotency-Key đã được dùng cho nội dung khác");
                }
                if (existing.ResultJson is not null)
                {
                    return System.Text.Json.JsonSerializer.Deserialize<ReturnResponse>(existing.ResultJson, JsonOpts)!;
                }
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
                    else if (_db.Database.CurrentTransaction != null) await _db.Database.CurrentTransaction.RollbackAsync();
                    if (hasKey)
                    {
                        var winner = await _db.IdempotencyRecords
                            .AsNoTracking()
                            .FirstOrDefaultAsync(r => r.Key == idempotencyKey && r.UserId == userId && r.ExpiresAt > DateTime.UtcNow);
                        if (winner is not null && winner.ResultJson is not null)
                            return System.Text.Json.JsonSerializer.Deserialize<ReturnResponse>(winner.ResultJson, JsonOpts)!;
                        throw new BusinessRuleException("Yêu cầu trùng đang được xử lý — vui lòng thử lại sau (409)");
                    }
                    throw;
                }
            }

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
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx)
                {
                    lockCmd.Transaction = npgTx;
                }
                lockCmd.Parameters.Add(new NpgsqlParameter("@ids", itemIds));
                await lockCmd.ExecuteNonQueryAsync();
            }

            if (order.CustomerId.HasValue)
            {
                using var loyaltyLockCmd = conn.CreateCommand() as NpgsqlCommand;
                loyaltyLockCmd!.CommandText = "SELECT \"Id\" FROM \"LoyaltyAccounts\" WHERE \"CustomerId\" = @custid FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx2)
                {
                    loyaltyLockCmd.Transaction = npgTx2;
                }
                loyaltyLockCmd.Parameters.Add(new NpgsqlParameter("@custid", order.CustomerId.Value));
                await loyaltyLockCmd.ExecuteNonQueryAsync();
            }

            // Re-fetch items to get fresh state after lock
            var orderItems = await _db.OrderItems
                .Include(oi => oi.Product)
                .Where(oi => itemIds.Contains(oi.Id))
                .ToListAsync();

            if (orderItems.Count != req.Items.Count)
                throw new BusinessRuleException("Một hoặc nhiều món trả không thuộc đơn hàng này");

            decimal totalSubtotal = order.Subtotal > 0 ? order.Subtotal : order.Items.Sum(x => x.UnitPrice * x.Qty);
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

                var grossLine = oi.UnitPrice * oi.Qty;
                var lineSurcharge = totalSubtotal > 0 ? Math.Round(order.SurchargeTotal * (grossLine / totalSubtotal), 0, MidpointRounding.AwayFromZero) : 0m;
                var netLineTotal = grossLine - oi.LineDiscount + lineSurcharge;

                decimal itemReturnTotal;
                bool isFinalItemReturn = (oi.ReturnedQty + reqItem.Qty == oi.Qty);

                if (isFinalItemReturn)
                {
                    var previouslyReturnedAmount = await _db.ReturnItems
                        .Where(ri => ri.OrderItemId == oi.Id)
                        .SumAsync(ri => (decimal?)ri.Total) ?? 0m;
                    itemReturnTotal = Math.Max(0m, netLineTotal - previouslyReturnedAmount);
                }
                else
                {
                    itemReturnTotal = Math.Round((netLineTotal / oi.Qty) * reqItem.Qty, 0, MidpointRounding.AwayFromZero);
                }

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

            bool isFullOrderReturn = order.Items.All(oi => oi.ReturnedQty >= oi.Qty);
            if (isFullOrderReturn && order.ShippingFee > 0)
            {
                totalReturnedAmount += order.ShippingFee;
            }
            totalReturnedAmount = Math.Round(totalReturnedAmount, 0, MidpointRounding.AwayFromZero);

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

            // ── Loyalty Points Revocation & LifetimeSpend Update ──
            decimal goodsRefunded = returnItems.Sum(ri => ri.Total);
            if (order.CustomerId.HasValue && order.Status == OrderStatus.Completed)
            {
                var loyalty = await _db.LoyaltyAccounts.FirstOrDefaultAsync(l => l.CustomerId == order.CustomerId.Value);
                if (loyalty != null)
                {
                    if (goodsRefunded > 0)
                    {
                        loyalty.LifetimeSpend = Math.Max(0m, loyalty.LifetimeSpend - goodsRefunded);
                    }

                    if (order.LoyaltyEarned > 0)
                    {
                        var goodsTotalOfOrder = order.Total - order.ShippingFee;
                        int revoke = 0;
                        if (isFullOrderReturn)
                        {
                            revoke = order.LoyaltyEarned - order.LoyaltyRevoked;
                        }
                        else if (goodsTotalOfOrder > 0)
                        {
                            revoke = (int)Math.Floor(order.LoyaltyEarned * (goodsRefunded / goodsTotalOfOrder));
                        }
                        revoke = Math.Min(revoke, order.LoyaltyEarned - order.LoyaltyRevoked);
                        revoke = Math.Max(0, revoke);

                        if (revoke > 0)
                        {
                            order.LoyaltyRevoked += revoke;
                            int shortfall = 0;
                            if (loyalty.Points < revoke)
                            {
                                shortfall = revoke - loyalty.Points;
                                loyalty.Points = 0;
                            }
                            else
                            {
                                loyalty.Points -= revoke;
                            }

                            _db.PointTransactions.Add(new PointTransaction
                            {
                                LoyaltyAccount = loyalty,
                                Order = order,
                                ReturnRecord = returnRecord,
                                Type = PointTransactionType.Revoke,
                                Points = -revoke,
                                Shortfall = shortfall,
                                Note = $"Thu hồi điểm trả hàng {returnCode}"
                            });
                        }
                    }
                }
            }

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

            var response = new ReturnResponse(returnCode, totalReturnedAmount, DateTime.UtcNow);

            if (hasKey)
            {
                var idem = await _db.IdempotencyRecords
                    .FirstAsync(r => r.Key == idempotencyKey && r.UserId == userId);
                idem.ResultJson = System.Text.Json.JsonSerializer.Serialize(response, JsonOpts);
                idem.StatusCode = 201;
                idem.ExpiresAt = DateTime.UtcNow.AddHours(24);
                await _db.SaveChangesAsync();
            }

            if (tx != null) await tx.CommitAsync();

            return response;
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
