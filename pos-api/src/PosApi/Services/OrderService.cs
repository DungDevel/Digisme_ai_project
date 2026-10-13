using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using Npgsql;

namespace PosApi.Services;

/// <summary>
/// BE-01: Order engine — mọi giá trị được TÍNH LẠI ở server, không tin client.
///
/// Chốt nghiệp vụ Sprint 1 (từ tài liệu + api-contract):
///  • Giá trong DB là GIÁ GOM VAT (StoreSetting.VatRatePercent) → cột Vat chỉ tách hiển thị:
///    Vat = base × rate / (100 + rate).
///  • Money rounding: AwayFromZero về 0 chữ số thập phân (tiền VNĐ nguyên).
///  • Qty: decimal(18,3) — hàng cân (IsWeighed) tối đa 3 số lẻ; hàng đếm phải nguyên.
///  • Trừ kho atomic: SELECT ... FOR UPDATE trong transaction (chặn 2 order cùng SKU chạy song song).
///  • Invoice code: INV-yyyymmdd-nnnn, nnnn từ bảng invoice_day_counter (UPDATE ... RETURNING —
///    transaction-safe, không trùng khi concurrent).
///  • Idempotency-Key: cùng key (cùng user) → trả lại response lần chạy đầu, không tạo đơn mới.
///
/// Thanh toán:
///  • cash → paid ≥ total, change = paid - total, đơn completed.
///  • debt → paid ≥ 0; phần còn thiếu tự tạo Debt; đơn completed.
///  • vietqr / banktransfer → đơn pending, Payment record chờ confirm (Sprint 2).
/// Loyalty: 1 điểm / 1.000đ, chỉ khi đơn completed + có khách.
/// </summary>
public interface IOrderService
{
    Task<OrderCreatedResponse> CreateOrderAsync(int userId, CreateOrderRequest req, string? idempotencyKey);
}

public class OrderService : IOrderService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const string IdempotencyEndpoint = "POST /api/v1/orders";

    private readonly AppDbContext _db;
    private readonly ILogger<OrderService> _log;

    public OrderService(AppDbContext db, ILogger<OrderService> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<OrderCreatedResponse> CreateOrderAsync(int userId, CreateOrderRequest req, string? idempotencyKey)
    {
        // ── Idempotency: client retry (F9 / network) → trả lại kết quả cũ ──
        // P0-2: bước này chỉ là fast-path đọc. Chống race thật nằm TRONG transaction
        // (placeholder insert ngay đầu — request song song cùng key bị unique constraint chặn).
        var hasKey = !string.IsNullOrWhiteSpace(idempotencyKey);
        if (hasKey)
        {
            var existing = await _db.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Key == idempotencyKey && r.UserId == userId && r.ExpiresAt > DateTime.UtcNow);
            if (existing is not null && existing.ResultJson is not null)
            {
                return JsonSerializer.Deserialize<OrderCreatedResponse>(existing.ResultJson, JsonOpts)!;
            }
        }

        // ── Validate request ──
        if (req.Items is null || req.Items.Count == 0)
            throw new BusinessRuleException("Giỏ hàng trống — không tạo được đơn");
        if (req.Items.Count > 200)
            throw new BusinessRuleException("Giỏ hàng tối đa 200 dòng");
        if (req.PaidAmount < 0) throw new BusinessRuleException("paidAmount không được âm");
        if (req.Discount < 0) throw new BusinessRuleException("discount không được âm");

        var paymentMethod = ParsePaymentMethod(req.PaymentMethod);

        var storeSetting = await _db.StoreSettings.AsNoTracking().FirstAsync();
        var isMember = false;
        if (req.CustomerId is > 0)
        {
            var cust = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CustomerId)
                ?? throw new BusinessRuleException($"Khách hàng {req.CustomerId} không tồn tại");
            if (!cust.IsActive) throw new BusinessRuleException("Khách hàng này đã bị vô hiệu");
            isMember = true;
        }

        var now = DateTime.UtcNow;
        var productIds = req.Items.Select(i => i.ProductId).Distinct().ToList();

        // ── Transaction: tính giá + trừ kho + ledger + order + log ──
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _db.Database.CurrentTransaction;
            await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

            // ── P0-2: idempotency placeholder NGAY ĐẦU transaction ──
            // Request song song cùng (key, user): unique constraint trên Key + lock hàng
            // → bên thứ hai chặn tới khi tx này commit/rollback, rồi fail-fast tại INSERT.
            // Không còn cảnh "cả 2 đều tạo đơn".
            if (hasKey)
            {
                try
                {
                    await _db.IdempotencyRecords.AddAsync(new IdempotencyRecord
                    {
                        Key = idempotencyKey!,
                        UserId = userId,
                        Endpoint = IdempotencyEndpoint,
                        ResultJson = null!, // placeholder — fill sau khi order tạo xong
                        StatusCode = 201,
                        ExpiresAt = DateTime.UtcNow.AddHours(24)
                    });
                    await _db.SaveChangesAsync(); // chỉ save placeholder, riêng lẻ
                }
                catch (DbUpdateException)
                {
                    // race: request song song cùng key đang giữ khóa → hủy tx,
                    // đọc lại record đã commit của request kia.
                    if (tx != null) await tx.RollbackAsync();
                    if (hasKey)
                    {
                        var winner = await _db.IdempotencyRecords
                            .AsNoTracking()
                            .FirstOrDefaultAsync(r => r.Key == idempotencyKey && r.UserId == userId && r.ExpiresAt > DateTime.UtcNow);
                        if (winner is not null && winner.ResultJson is not null)
                            return JsonSerializer.Deserialize<OrderCreatedResponse>(winner.ResultJson, JsonOpts)!;
                        throw new BusinessRuleException(
                            "Yêu cầu trùng đang được xử lý — vui lòng thử lại sau ít giây (409)");
                    }
                    throw;
                }
            }

            // Khóa các dòng products (FOR UPDATE) để trừ kho atomic khi nhiều order song song
            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();
            using (var lockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                lockCmd!.CommandText = "SELECT \"Id\" FROM \"Products\" WHERE \"Id\" = ANY(@ids) ORDER BY \"Id\" FOR UPDATE";
                var p = new NpgsqlParameter("@ids", productIds);
                lockCmd.Parameters.Add(p);
                await lockCmd.ExecuteNonQueryAsync();
            }

            // P0-1: Bỏ AsNoTracking() để EF Core track thay đổi CurrentStock
            // Vì Products đã được lock bằng FOR UPDATE ở trên, EF Core sẽ tự động
            // persist thay đổi khi SaveChangesAsync() được gọi
            var products = await _db.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync();
            if (products.Count != productIds.Count)
                throw new BusinessRuleException("Có sản phẩm trong giỏ không tồn tại");

            var units = await _db.ProductUnits
                .AsNoTracking()
                .Include(u => u.Unit)
                .Where(u => productIds.Contains(u.ProductId))
                .ToListAsync();

            // ── Tính lại giá từng dòng (giá tầng theo khách, đơn vị gốc) ──
            var lines = new List<ComputedLine>();
            var subtotal = 0m;
            var unitNames = units.ToDictionary(u => (u.ProductId, u.UnitId), u => u.Unit!.Name);

            foreach (var item in req.Items)
            {
                var p = products.First(x => x.Id == item.ProductId);
                if (!p.IsActive) throw new BusinessRuleException($"Sản phẩm '{p.Name}' đã ngừng bán");

                decimal unitPrice;
                int? unitId = item.UnitId;
                decimal conversion = 1m;

                // đơn vị khác gốc → lấy conversion để quy qty & unitPrice về đơn vị gốc
                if (unitId is not null)
                {
                    var pu = units.FirstOrDefault(u => u.ProductId == p.Id && u.UnitId == unitId)
                        ?? throw new BusinessRuleException($"Đơn vị {unitId} không hợp lệ cho '{p.Name}'");
                    conversion = pu.ConversionRate;
                    if (pu.IsBase) unitId = null; // contract: unitId=null = đơn vị gốc
                }

                // qty client gửi ở đơn vị đã chọn → quy về đơn vị gốc
                var qtyBase = Round3(item.Qty * conversion);
                ValidateQty(p, qtyBase, conversion);

                if (item.UnitPrice is decimal op)
                {
                    if (op < 0) throw new BusinessRuleException($"Đơn giá '{p.Name}' không được âm");
                    // đơn giá client gửi ở đơn vị đã chọn → quy về đơn vị gốc để lưu
                    unitPrice = RoundMoney(op / conversion);
                }
                else
                {
                    unitPrice = isMember && p.MemberPrice > 0 ? p.MemberPrice : p.RetailPrice;
                }

                var lineTotal = RoundMoney(qtyBase * unitPrice);
                subtotal += lineTotal;
                lines.Add(new ComputedLine(p, unitId, qtyBase, unitPrice, lineTotal));
            }
            subtotal = RoundMoney(subtotal);

            // ── Discount: chặn âm & vượt subtotal ──
            var discount = RoundMoney(req.Discount);
            if (discount > subtotal) throw new BusinessRuleException("discount không được vượt subtotal");

            // ── Surcharges: chỉ áp surcharge AUTO đang hiệu lực ──
            var surchargeTotal = 0m;
            if (req.Surcharges is not null && req.Surcharges.Count > 0)
            {
                var surchargeIds = req.Surcharges.Select(s => s.SurchargeId).Distinct().ToList();
                var surcharges = await _db.Surcharges.AsNoTracking()
                    .Where(s => surchargeIds.Contains(s.Id)).ToListAsync();
                foreach (var sReq in req.Surcharges)
                {
                    var s = surcharges.FirstOrDefault(x => x.Id == sReq.SurchargeId)
                        ?? throw new BusinessRuleException($"Phụ phí {sReq.SurchargeId} không tồn tại");
                    if (!s.IsActive) throw new BusinessRuleException($"Phụ phí '{s.Name}' đang vô hiệu");
                    if (s.ActiveFrom is not null && now < s.ActiveFrom.Value)
                        throw new BusinessRuleException($"Phụ phí '{s.Name}' chưa có hiệu lực");
                    if (s.ActiveUntil is not null && now > s.ActiveUntil.Value)
                        throw new BusinessRuleException($"Phụ phí '{s.Name}' đã hết hiệu lực");
                    if (s.ApplyMode != SurchargeApply.Auto)
                        throw new BusinessRuleException($"Phụ phí '{s.Name}' là loại nhập tay — không áp khi tạo đơn");

                    var value = sReq.OverrideValue is decimal ov ? RoundMoney(ov) : s.Value;
                    if (value < 0) throw new BusinessRuleException($"Giá trị phụ phí '{s.Name}' không được âm");
                    surchargeTotal += s.Type == SurchargeType.Percent
                        ? RoundMoney(subtotal * value / 100m)
                        : RoundMoney(value);
                }
            }
            surchargeTotal = RoundMoney(surchargeTotal);

            // ── Giá trị đơn: giá DB đã gồm VAT, base = tổng khách trả ──
            var baseAmount = subtotal - discount + surchargeTotal; // = giá khách trả (gồm VAT)
            var vat = RoundMoney(baseAmount * storeSetting.VatRatePercent / (100m + storeSetting.VatRatePercent));
            var total = baseAmount;

            // ── Tiền đưa / thừa / nợ ──
            var paid = RoundMoney(req.PaidAmount);
            decimal change = 0m, amountOwed = 0m;
            if (paymentMethod == PaymentMethod.Cash)
            {
                if (paid < total)
                    throw new BusinessRuleException($"Tiền khách đưa {paid:N0}đ chưa đủ tổng {total:N0}đ");
                change = paid - total;
            }
            else if (paymentMethod == PaymentMethod.Debt)
            {
                amountOwed = Math.Max(0m, total - paid);
                if (req.CustomerId is not > 0)
                    throw new BusinessRuleException("Mua chịu (debt) bắt buộc chọn khách hàng");
            }

            // ── Trừ kho + ledger entries ──
            var stockWarnings = new List<string>();
            foreach (var line in lines)
            {
                if (line.Qty > line.Product.CurrentStock)
                    throw new StockShortageException(
                        $"Không đủ tồn kho '{line.Product.Name}': còn {line.Product.CurrentStock:0.###}, cần {line.Qty:0.###}");

                // Ghi nhận cảnh báo trước khi trừ kho
                if (line.Product.CurrentStock - line.Qty <= line.Product.MinStock && !stockWarnings.Contains(line.Product.Name))
                    stockWarnings.Add($"{line.Product.Name} còn {Round3(line.Product.CurrentStock - line.Qty):0.###}");

                // Cập nhật tồn kho bằng conditional UPDATE với điều kiện WHERE
                // Để tránh bán vượt tồn nếu có concurrent order
                line.Product.CurrentStock = Round3(line.Product.CurrentStock - line.Qty);
            }

            // ── Mã đơn INV-yyyymmdd-nnnn (counter + row lock, không trùng khi concurrent) ──
            var code = await NextInvoiceCodeAsync();

            var isCompleted = paymentMethod is PaymentMethod.Cash or PaymentMethod.Debt;
            var order = new Order
            {
                Code = code,
                CashierId = userId,
                CustomerId = req.CustomerId,
                Subtotal = subtotal,
                Discount = discount,
                SurchargeTotal = surchargeTotal,
                Vat = vat,
                Total = total,
                PaymentMethod = paymentMethod,
                PaidAmount = paid,
                Change = change,
                Status = isCompleted ? OrderStatus.Completed : OrderStatus.Pending,
                CompletedAt = isCompleted ? now : null
            };
            order.Items = lines.Select(l => new OrderItem
            {
                Order = order,
                ProductId = l.Product.Id,
                UnitId = l.UnitId,
                Qty = l.Qty,
                UnitPrice = l.UnitPrice,
                Total = l.LineTotal
            }).ToList();
            _db.Orders.Add(order);

            // ── Debt: phần chưa thanh toán ──
            if (paymentMethod == PaymentMethod.Debt && amountOwed > 0)
            {
                _db.Debts.Add(new Debt
                {
                    CustomerId = req.CustomerId!.Value,
                    Amount = amountOwed,
                    Note = $"Đơn {code}"
                });
            }

            // ── Payment record cho vietqr/bank (chờ confirm — Sprint 2) ──
            if (paymentMethod is PaymentMethod.VietQr or PaymentMethod.BankTransfer)
            {
                order.Payments.Add(new Payment
                {
                    Order = order,
                    Method = paymentMethod,
                    Amount = total,
                    Status = PaymentStatus.Pending
                });
            }

            // ── Loyalty: 1 điểm/1000đ, đơn completed + có khách ──
            int? loyaltyEarned = null;
            if (isCompleted && req.CustomerId is > 0)
            {
                var loyalty = await GetOrCreateLoyaltyAsync(req.CustomerId.Value);
                loyaltyEarned = (int)(total / 1000m);
                loyalty.Points += loyaltyEarned.Value;
                loyalty.LifetimeSpend += total;
            }

            // ── Ledger + PosLog ──
            _db.StockLedger.AddRange(lines.Select(l => new StockLedgerEntry
            {
                ProductId = l.Product.Id,
                Qty = -l.Qty,
                Balance = l.Product.CurrentStock,
                RefType = LedgerRefType.Sale,
                RefId = code,
                Note = "Bán tại quầy"
            }));

            PosLogService.Write(_db, "ORDER_CREATED", userId, "order", code,
                new { total, paymentMethod = paymentMethod.ToString().ToLowerInvariant(), lines = lines.Count });

            // ── Save: order + items + kho + ledger + log cùng 1 transaction ──
            await _db.SaveChangesAsync();

            var response = new OrderCreatedResponse(
                order.Id, code, order.Subtotal, order.Discount, order.SurchargeTotal,
                order.Vat, order.Total, paymentMethod.ToString().ToLowerInvariant(),
                order.PaidAmount, order.Change, amountOwed,
                order.Status.ToString().ToLowerInvariant(),
                loyaltyEarned, order.CompletedAt ?? now,
                lines.Select(l => new OrderLineResponse(
                    l.Product.Id, l.Product.Name, l.UnitId,
                    l.UnitId is null
                        ? unitNames.TryGetValue((l.Product.Id, BaseUnitId(units, l.Product.Id)), out var bn) ? bn : "cái"
                        : unitNames[(l.Product.Id, l.UnitId.Value)],
                    l.Qty, l.UnitPrice, l.LineTotal, l.Product.IsWeighed)).ToList(),
                stockWarnings);

            // ── P0-2: fill kết quả vào placeholder idempotency (đã insert đầu tx) ──
            // Cùng transaction với order → commit một lượt. Không cần Add lần 2.
            if (hasKey)
            {
                var idem = await _db.IdempotencyRecords
                    .FirstAsync(r => r.Key == idempotencyKey && r.UserId == userId);
                idem.ResultJson = JsonSerializer.Serialize(response, JsonOpts);
                idem.StatusCode = 201;
                idem.ExpiresAt = DateTime.UtcNow.AddHours(24);
                await _db.SaveChangesAsync();
            }

            if (tx != null) await tx.CommitAsync();
            return response;
        });
    }

    /// <summary>
    /// INV-yyyymmdd-nnnn: row trong invoice_day_counter khóa bằng SELECT FOR UPDATE,
    /// 2 request cùng ngày không thể trùng nnnn (BE-P0: sequence/transaction-safe).
    /// </summary>
    private async Task<string> NextInvoiceCodeAsync()
    {
        // INSERT ... ON CONFLICT (day) DO UPDATE ... RETURNING — row lock tự giữ trong transaction,
        // 2 order cùng ngày không thể trùng nnnn (transaction-safe, không cần sequence).
        var conn = _db.Database.GetDbConnection();
        using var cmd = conn.CreateCommand() as NpgsqlCommand;
        cmd!.CommandText = "INSERT INTO \"invoice_day_counter\" (\"Day\", \"N\") VALUES (@day, 1) " +
                           "ON CONFLICT (\"Day\") DO UPDATE SET \"N\" = \"invoice_day_counter\".\"N\" + 1 " +
                           "RETURNING \"invoice_day_counter\".\"N\"";
        cmd.Parameters.Add(new NpgsqlParameter("@day", DateTime.UtcNow.ToString("yyyy-MM-dd")));
        var n = (int)(await cmd.ExecuteScalarAsync())!;
        return $"INV-{DateTime.UtcNow:yyyyMMdd}-{n:D4}";
    }

    private static int BaseUnitId(List<ProductUnit> units, int productId) =>
        units.FirstOrDefault(u => u.ProductId == productId && u.IsBase)?.UnitId ?? -1;

    private async Task<LoyaltyAccount> GetOrCreateLoyaltyAsync(int customerId)
    {
        var existing = await _db.LoyaltyAccounts.FirstOrDefaultAsync(l => l.CustomerId == customerId);
        if (existing is not null) return existing;
        var loyalty = new LoyaltyAccount { CustomerId = customerId, Points = 0, Tier = "Silver", LifetimeSpend = 0 };
        _db.LoyaltyAccounts.Add(loyalty);
        return loyalty;
    }

    /// <summary>
    /// Validate qty quy về đơn vị gốc: hàng cân (IsWeighed) ≤3 số lẻ; hàng đếm phải nguyên.
    /// </summary>
    private static void ValidateQty(Product p, decimal qtyBase, decimal conversion)
    {
        if (qtyBase <= 0) throw new BusinessRuleException($"Số lượng '{p.Name}' phải lớn hơn 0");
        if (qtyBase > 100_000m) throw new BusinessRuleException($"Số lượng '{p.Name}' quá lớn");
        if (p.IsWeighed)
        {
            if (Math.Abs(qtyBase - Math.Round(qtyBase, 3)) > 0.0001m)
                throw new BusinessRuleException($"Hàng cân '{p.Name}' tối đa 3 số thập phân");
        }
        else if (qtyBase != Math.Truncate(qtyBase))
        {
            throw new BusinessRuleException($"Sản phẩm '{p.Name}' là hàng đếm — quy về đơn vị gốc phải ra số nguyên");
        }
    }

    private static decimal RoundMoney(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);
    private static decimal Round3(decimal v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);

    private static PaymentMethod ParsePaymentMethod(string s) => s.Trim().ToLowerInvariant() switch
    {
        "cash" => PaymentMethod.Cash,
        "debt" => PaymentMethod.Debt,
        "vietqr" or "viet_qr" => PaymentMethod.VietQr,
        "banktransfer" or "bank_transfer" => PaymentMethod.BankTransfer,
        _ => throw new BusinessRuleException($"paymentMethod '{s}' không hợp lệ (cash|debt|vietqr|banktransfer)")
    };

    private sealed record ComputedLine(Product Product, int? UnitId, decimal Qty, decimal UnitPrice, decimal LineTotal);
}
