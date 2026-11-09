using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    Task<PagedOrderResponse> GetOrdersAsync(DateTime? fromDate, DateTime? toDate, int? customerId, int? cashierId, int? shiftId, OrderStatus? status, PaymentMethod? paymentMethod, int page, int pageSize);
    Task<OrderDetailResponse> GetOrderByIdAsync(int id);
    Task UpdateOrderAsync(int id, OrderUpdateRequest req, int userId);
    Task CancelOrderAsync(int id, int userId, string reason);
    Task<OrderImageResponse> AddOrderImageAsync(int orderId, int userId, OrderImageUploadRequest req);
    Task DeleteOrderImageAsync(int imageId, int userId);
}

public class OrderService : IOrderService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const string IdempotencyEndpoint = "POST /api/v1/orders";

    private readonly AppDbContext _db;
    private readonly ILogger<OrderService> _log;
    private readonly INotificationService? _notificationService;
    private readonly Microsoft.Extensions.Configuration.IConfiguration? _configuration;
    private readonly IFileStorageService? _storage;

    public OrderService(AppDbContext db, ILogger<OrderService> log, IFileStorageService? storage = null, INotificationService? notificationService = null, Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
    {
        _db = db;
        _log = log;
        _storage = storage;
        _notificationService = notificationService;
        _configuration = configuration;
    }

    public async Task<OrderCreatedResponse> CreateOrderAsync(int userId, CreateOrderRequest req, string? idempotencyKey)
    {
        // ── Validate DeliveryMethod & ShippingFee ──
        var deliveryMethod = string.IsNullOrWhiteSpace(req.DeliveryMethod) ? "instore" : req.DeliveryMethod.Trim().ToLowerInvariant();
        if (deliveryMethod != "instore" && deliveryMethod != "shopdelivery" && deliveryMethod != "carrier")
            throw new BusinessRuleException("Hình thức giao hàng không hợp lệ (chỉ nhận: instore, shopdelivery, carrier)");

        decimal shippingFee = 0m;
        if (deliveryMethod == "instore")
        {
            shippingFee = 0m;
        }
        else
        {
            if (req.ShippingFee.HasValue)
            {
                if (req.ShippingFee.Value < 0)
                    throw new BusinessRuleException("Phí giao hàng không được âm");
                shippingFee = RoundMoney(req.ShippingFee.Value);
            }
            else
            {
                shippingFee = 0m;
            }
        }

        var requestHash = ComputeRequestHash(req);

        // ── Idempotency: client retry (F9 / network) → trả lại kết quả cũ ──
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
                    return JsonSerializer.Deserialize<OrderCreatedResponse>(existing.ResultJson, JsonOpts)!;
                }
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
                        RequestHash = requestHash,
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
                    else if (_db.Database.CurrentTransaction != null) await _db.Database.CurrentTransaction.RollbackAsync();
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
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx)
                {
                    lockCmd.Transaction = npgTx;
                }
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

            // ── Discount & VIP: chặn âm & vượt subtotal ──
            var discount = RoundMoney(req.Discount);
            if (req.CustomerId is > 0)
            {
                var cust = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CustomerId);
                if (cust != null && cust.IsVip)
                {
                    decimal vipPercent = 5m;
                    if (_configuration != null)
                    {
                        vipPercent = _configuration.GetValue<decimal>("VipDiscountPercent", 5m);
                    }
                    var vipDiscount = RoundMoney(subtotal * (vipPercent / 100m));
                    discount = RoundMoney(req.Discount + vipDiscount);
                }
            }
            if (discount > subtotal) discount = subtotal;

            // Distribute discount across lines proportionally
            decimal allocatedDiscount = 0m;
            var lineDiscounts = new decimal[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                if (i == lines.Count - 1)
                {
                    lineDiscounts[i] = discount - allocatedDiscount;
                }
                else
                {
                    var proportion = subtotal > 0 ? lines[i].LineTotal / subtotal : 0m;
                    lineDiscounts[i] = RoundMoney(discount * proportion);
                    allocatedDiscount += lineDiscounts[i];
                }
            }

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

            // ── Giá trị đơn: giá DB đã gồm VAT, base = tổng khách trả + phí giao hàng ──
            var baseAmount = subtotal - discount + surchargeTotal + shippingFee;
            var vat = RoundMoney((subtotal - discount + surchargeTotal) * storeSetting.VatRatePercent / (100m + storeSetting.VatRatePercent));
            var total = baseAmount;

            decimal allocatedSurcharge = 0m;
            var lineSurcharges = new decimal[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                if (i == lines.Count - 1)
                {
                    lineSurcharges[i] = surchargeTotal - allocatedSurcharge;
                }
                else
                {
                    var proportion = subtotal > 0 ? lines[i].LineTotal / subtotal : 0m;
                    lineSurcharges[i] = RoundMoney(surchargeTotal * proportion);
                    allocatedSurcharge += lineSurcharges[i];
                }
            }

            var vatBaseTotal = (subtotal - discount + surchargeTotal);
            decimal allocatedVat = 0m;
            var lineVats = new decimal[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                var lineVatBase = lines[i].LineTotal - lineDiscounts[i] + lineSurcharges[i];
                if (i == lines.Count - 1)
                {
                    lineVats[i] = vat - allocatedVat;
                }
                else
                {
                    var proportion = vatBaseTotal > 0 ? lineVatBase / vatBaseTotal : 0m;
                    lineVats[i] = RoundMoney(vat * proportion);
                    allocatedVat += lineVats[i];
                }
            }

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

            // ── Mã đơn INV-yyyymmdd-nnnn (counter + row lock, không trùng khi concurrent) ──
            var code = await NextInvoiceCodeAsync();

            // ── Lấy định lượng BOM cho các sản phẩm trong đơn ──
            var orderedProductIds = lines.Select(l => l.Product.Id).ToList();
            var recipes = await _db.ProductRecipes
                .Include(r => r.Ingredient)
                .Where(r => orderedProductIds.Contains(r.ProductId))
                .ToListAsync();
            var recipeDict = recipes.GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => g.ToList());

            // ── Trừ kho + ledger entries (bao gồm BOM nguyên liệu) ──
            var stockWarnings = new List<string>();
            var bomLedgerEntries = new List<StockLedgerEntry>();

            foreach (var line in lines)
            {
                if (line.Qty > line.Product.CurrentStock)
                    throw new StockShortageException(
                        $"Không đủ tồn kho '{line.Product.Name}': còn {line.Product.CurrentStock:0.###}, cần {line.Qty:0.###}");

                // Ghi nhận cảnh báo trước khi trừ kho
                if (line.Product.CurrentStock - line.Qty <= line.Product.MinStock && !stockWarnings.Contains(line.Product.Name))
                    stockWarnings.Add($"{line.Product.Name} còn {Round3(line.Product.CurrentStock - line.Qty):0.###}");

                // Cập nhật tồn kho thành phẩm
                line.Product.CurrentStock = Round3(line.Product.CurrentStock - line.Qty);

                // Trừ kho nguyên liệu theo BOM nếu có
                if (recipeDict.TryGetValue(line.Product.Id, out var prodRecipes))
                {
                    foreach (var recipe in prodRecipes)
                    {
                        var ing = recipe.Ingredient;
                        var requiredIngQty = Round3(recipe.Qty * line.Qty);
                        if (requiredIngQty > ing.CurrentStock)
                            throw new StockShortageException(
                                $"Không đủ tồn kho nguyên liệu '{ing.Name}' (BOM của '{line.Product.Name}'): còn {ing.CurrentStock:0.###}, cần {requiredIngQty:0.###}");

                        ing.CurrentStock = Round3(ing.CurrentStock - requiredIngQty);

                        bomLedgerEntries.Add(new StockLedgerEntry
                        {
                            ProductId = ing.Id,
                            Qty = -requiredIngQty,
                            Balance = ing.CurrentStock,
                            RefType = LedgerRefType.Sale,
                            RefId = code,
                            Note = $"Tiêu hao định lượng BOM cho {line.Product.Name}"
                        });
                    }
                }
            }

            var isCompleted = paymentMethod is PaymentMethod.Cash or PaymentMethod.Debt;
            var openShift = await _db.Shifts.FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);
            var order = new Order
            {
                Code = code,
                CashierId = userId,
                CustomerId = req.CustomerId,
                ShiftId = openShift?.Id,
                Subtotal = subtotal,
                Discount = discount,
                SurchargeTotal = surchargeTotal,
                Vat = vat,
                Total = total,
                PaymentMethod = paymentMethod,
                PaidAmount = paid,
                Change = change,
                DeliveryMethod = deliveryMethod,
                ShippingFee = shippingFee,
                Status = isCompleted ? OrderStatus.Completed : OrderStatus.Pending,
                CompletedAt = isCompleted ? now : null
            };
            order.Items = lines.Select((l, idx) => new OrderItem
            {
                Order = order,
                ProductId = l.Product.Id,
                UnitId = l.UnitId,
                Qty = l.Qty,
                UnitPrice = l.UnitPrice,
                LineDiscount = lineDiscounts[idx],
                Vat = lineVats[idx],
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

            // ── Loyalty: 1 điểm/1000đ, đơn completed + có khách (không tính trên phí vận chuyển) ──
            int? loyaltyEarned = null;
            if (isCompleted && req.CustomerId is > 0)
            {
                var npgTx = _db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
                var loyalty = await GetOrCreateLoyaltyAsync(req.CustomerId.Value, conn, npgTx);
                var goodsTotal = total - shippingFee;
                int earnedPoints = (int)(goodsTotal / 1000m);
                loyaltyEarned = earnedPoints;
                loyalty.Points += earnedPoints;
                loyalty.LifetimeSpend += goodsTotal;

                order.LoyaltyEarned = earnedPoints;
                _db.PointTransactions.Add(new PointTransaction
                {
                    LoyaltyAccount = loyalty,
                    Order = order,
                    Type = PointTransactionType.Earn,
                    Points = earnedPoints,
                    Note = $"Tích điểm đơn hàng {code}"
                });
            }

            // ── Ledger + PosLog ──
            var ledgerEntries = new List<StockLedgerEntry>();
            ledgerEntries.AddRange(lines.Select(l => new StockLedgerEntry
            {
                ProductId = l.Product.Id,
                Qty = -l.Qty,
                Balance = l.Product.CurrentStock,
                RefType = LedgerRefType.Sale,
                RefId = code,
                Note = "Bán tại quầy"
            }));
            ledgerEntries.AddRange(bomLedgerEntries);
            _db.StockLedger.AddRange(ledgerEntries);

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
                order.Items.Select(oi => new OrderLineResponse(
                    oi.Id,
                    oi.ProductId,
                    products.First(p => p.Id == oi.ProductId).Name,
                    oi.UnitId,
                    oi.UnitId is null
                        ? unitNames.TryGetValue((oi.ProductId, BaseUnitId(units, oi.ProductId)), out var bn) ? bn : "cái"
                        : unitNames[(oi.ProductId, oi.UnitId.Value)],
                    oi.Qty,
                    oi.UnitPrice,
                    oi.Total,
                    products.First(p => p.Id == oi.ProductId).IsWeighed)).ToList(),
                stockWarnings,
                0m, 0, 0m, 0m, null, order.DeliveryMethod, order.ShippingFee);

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

            try
            {
                if (_notificationService != null)
                {
                    if (isCompleted)
                    {
                        await _notificationService.NotifyOrderCompletedAsync(order.Id, code, total);
                    }
                    else
                    {
                        await _notificationService.NotifyOrderCreatedAsync(order.Id, code, total);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to dispatch order notification for {Code}", code);
            }

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
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();
        using var cmd = conn.CreateCommand() as NpgsqlCommand;
        cmd!.CommandText = "INSERT INTO \"invoice_day_counter\" (\"Day\", \"N\") VALUES (@day, 1) " +
                           "ON CONFLICT (\"Day\") DO UPDATE SET \"N\" = \"invoice_day_counter\".\"N\" + 1 " +
                           "RETURNING \"invoice_day_counter\".\"N\"";
        if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx)
        {
            cmd.Transaction = npgTx;
        }
        cmd.Parameters.Add(new NpgsqlParameter("@day", DateTime.UtcNow.ToString("yyyy-MM-dd")));
        var n = (int)(await cmd.ExecuteScalarAsync())!;
        return $"INV-{DateTime.UtcNow:yyyyMMdd}-{n:D4}";
    }

    private static int BaseUnitId(List<ProductUnit> units, int productId) =>
        units.FirstOrDefault(u => u.ProductId == productId && u.IsBase)?.UnitId ?? -1;

    private async Task<LoyaltyAccount> GetOrCreateLoyaltyAsync(int customerId, System.Data.Common.DbConnection conn, NpgsqlTransaction? tx)
    {
        // Lock loyalty account with FOR UPDATE (Lock order: Products -> LoyaltyAccounts, consistent with ReturnService)
        using (var lockCmd = conn.CreateCommand() as NpgsqlCommand)
        {
            lockCmd!.CommandText = "SELECT \"Id\" FROM \"LoyaltyAccounts\" WHERE \"CustomerId\" = @custid FOR UPDATE";
            if (tx != null) lockCmd.Transaction = tx;
            lockCmd.Parameters.Add(new NpgsqlParameter("@custid", customerId));
            await lockCmd.ExecuteNonQueryAsync();
        }

        var existing = await _db.LoyaltyAccounts.FirstOrDefaultAsync(l => l.CustomerId == customerId);
        if (existing is not null) return existing;

        try
        {
            var loyalty = new LoyaltyAccount { CustomerId = customerId, Points = 0, Tier = "Silver", LifetimeSpend = 0 };
            _db.LoyaltyAccounts.Add(loyalty);
            await _db.SaveChangesAsync();
            return loyalty;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            using (var lockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                lockCmd!.CommandText = "SELECT \"Id\" FROM \"LoyaltyAccounts\" WHERE \"CustomerId\" = @custid FOR UPDATE";
                if (tx != null) lockCmd.Transaction = tx;
                lockCmd.Parameters.Add(new NpgsqlParameter("@custid", customerId));
                await lockCmd.ExecuteNonQueryAsync();
            }
            return await _db.LoyaltyAccounts.FirstAsync(l => l.CustomerId == customerId);
        }
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

    private static string ComputeRequestHash(CreateOrderRequest req)
    {
        var json = JsonSerializer.Serialize(req);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
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

    public async Task<PagedOrderResponse> GetOrdersAsync(
        DateTime? fromDate,
        DateTime? toDate,
        int? customerId,
        int? cashierId,
        int? shiftId,
        OrderStatus? status,
        PaymentMethod? paymentMethod,
        int page,
        int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.Orders
            .AsNoTracking()
            .Include(o => o.Cashier)
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(o => o.CreatedAt >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(o => o.CreatedAt <= toDate.Value);
        if (customerId.HasValue)
            query = query.Where(o => o.CustomerId == customerId.Value);
        if (cashierId.HasValue)
            query = query.Where(o => o.CashierId == cashierId.Value);
        if (shiftId.HasValue)
            query = query.Where(o => o.ShiftId == shiftId.Value);
        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);
        if (paymentMethod.HasValue)
            query = query.Where(o => o.PaymentMethod == paymentMethod.Value);

        var total = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = orders.Select(o => new OrderSummaryResponse(
            o.Id,
            o.Code,
            o.CashierId,
            o.Cashier?.FullName ?? o.Cashier?.Username ?? "Unknown",
            o.CustomerId,
            o.Customer?.Name,
            o.ShiftId,
            o.Subtotal,
            o.Discount,
            o.SurchargeTotal,
            o.Vat,
            o.ShippingFee,
            o.Total,
            o.PaymentMethod.ToString().ToLowerInvariant(),
            o.PaidAmount,
            o.Change,
            o.Status.ToString().ToLowerInvariant(),
            o.Items.Count,
            o.CreatedAt,
            o.CompletedAt
        )).ToList();

        return new PagedOrderResponse(page, pageSize, total, totalPages, items);
    }

    public async Task<OrderDetailResponse> GetOrderByIdAsync(int id)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Cashier)
            .Include(o => o.Customer)
            .Include(o => o.Items).ThenInclude(i => i.Product)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
            throw new KeyNotFoundException($"Không tìm thấy đơn hàng #{id}");

        var unitIds = order.Items.Where(i => i.UnitId.HasValue).Select(i => i.UnitId!.Value).Distinct().ToList();
        var units = await _db.Units.AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name);

        var items = order.Items.Select(i => new OrderItemDetailResponse(
            i.Id,
            i.ProductId,
            i.Product?.Name ?? "",
            i.Product?.Barcode ?? "",
            i.UnitId,
            i.UnitId.HasValue && units.TryGetValue(i.UnitId.Value, out var uName) ? uName : "cái",
            i.Qty,
            i.UnitPrice,
            i.LineDiscount,
            i.Total,
            i.Product?.IsWeighed ?? false
        )).ToList();

        var payments = order.Payments.Select(p => new OrderPaymentBriefResponse(
            p.Id,
            p.Method.ToString(),
            p.Amount,
            p.Status.ToString(),
            p.Reference,
            p.PaidAt
        )).ToList();

        return new OrderDetailResponse(
            order.Id,
            order.Code,
            order.CashierId,
            order.Cashier?.FullName ?? order.Cashier?.Username ?? "Unknown",
            order.CustomerId,
            order.Customer?.Name,
            order.Customer?.Phone,
            order.ShiftId,
            order.Subtotal,
            order.Discount,
            order.SurchargeTotal,
            order.Vat,
            order.ShippingFee,
            order.Total,
            order.PaymentMethod.ToString().ToLowerInvariant(),
            order.PaidAmount,
            order.Change,
            order.DeliveryMethod,
            order.Status.ToString().ToLowerInvariant(),
            order.LoyaltyEarned,
            order.LoyaltyRevoked,
            order.CreatedAt,
            order.CompletedAt,
            items,
            payments
        );
    }

    public async Task UpdateOrderAsync(int id, OrderUpdateRequest req, int userId)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) throw new KeyNotFoundException($"Không tìm thấy đơn hàng #{id}");
        if (order.Status == OrderStatus.Completed)
            throw new BusinessRuleException("Không thể chỉnh sửa đơn hàng đã hoàn tất.");

        // Cập nhật thông tin cơ bản
        if (req.CustomerId.HasValue) order.CustomerId = req.CustomerId;
        if (!string.IsNullOrWhiteSpace(req.Note)) order.Note = req.Note; // Giả định Entity Order có trường Note

        // Cập nhật Items & Tính lại tiền
        if (req.Items != null && req.Items.Count > 0)
        {
            _db.OrderItems.RemoveRange(order.Items);

            var productIds = req.Items.Select(i => i.ProductId).Distinct().ToList();
            var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();
            var storeSetting = await _db.StoreSettings.AsNoTracking().FirstAsync();

            decimal newSubtotal = 0;
            foreach (var itemReq in req.Items)
            {
                var p = products.First(x => x.Id == itemReq.ProductId);
                var unitPrice = itemReq.UnitPrice ?? p.RetailPrice;
                var lineTotal = itemReq.Qty * unitPrice;

                order.Items.Add(new OrderItem
                {
                    Order = order,
                    ProductId = p.Id,
                    Qty = itemReq.Qty,
                    UnitPrice = unitPrice,
                    Total = lineTotal
                });
                newSubtotal += lineTotal;
            }
            order.Subtotal = RoundMoney(newSubtotal);
        }

        order.Discount = RoundMoney(req.Discount ?? order.Discount);

        // Tính lại Total (Đơn giản hóa: Total = Subtotal - Discount)
        order.Total = RoundMoney(order.Subtotal - order.Discount);

        PosLogService.Write(_db, "ORDER_UPDATED", userId, "order", order.Code, new { order.Total });
        await _db.SaveChangesAsync();
    }

    public async Task CancelOrderAsync(int id, int userId, string reason)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order == null) throw new KeyNotFoundException($"Không tìm thấy đơn hàng #{id}");
        if (order.Status == OrderStatus.Completed)
            throw new BusinessRuleException("Không thể hủy đơn hàng đã hoàn tất.");

        // Hoàn trả kho
        foreach (var item in order.Items)
        {
            var p = await _db.Products.FindAsync(item.ProductId);
            if (p != null) p.CurrentStock += item.Qty;
        }

        order.Status = OrderStatus.Cancelled;
        PosLogService.Write(_db, "ORDER_CANCELLED", userId, "order", order.Code, new { reason });
        await _db.SaveChangesAsync();
    }

    public async Task<OrderImageResponse> AddOrderImageAsync(int orderId, int userId, OrderImageUploadRequest req)
    {
        if (_storage == null) throw new InvalidOperationException("FileStorageService is not configured");
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId);
        if (order == null) throw new KeyNotFoundException($"Không tìm thấy đơn hàng #{orderId}");

        var imageUrl = await _storage.SaveFileAsync(req.File, "orders");

        var orderImage = new OrderImage
        {
            OrderId = orderId,
            ImageUrl = imageUrl,
            ImageType = string.IsNullOrWhiteSpace(req.ImageType) ? "Proof" : req.ImageType.Trim()
        };

        _db.OrderImages.Add(orderImage);
        PosLogService.Write(_db, "ORDER_IMAGE_UPLOADED", userId, "order", order.Code, new { orderImage.ImageType });
        await _db.SaveChangesAsync();

        return new OrderImageResponse(orderImage.Id, orderImage.ImageUrl, orderImage.ImageType, orderImage.CreatedAt);
    }

    public async Task DeleteOrderImageAsync(int imageId, int userId)
    {
        if (_storage == null) throw new InvalidOperationException("FileStorageService is not configured");
        var img = await _db.OrderImages.FirstOrDefaultAsync(x => x.Id == imageId);
        if (img == null) throw new KeyNotFoundException($"Không tìm thấy ảnh #{imageId}");

        await _storage.DeleteFileAsync(img.ImageUrl);

        _db.OrderImages.Remove(img);
        PosLogService.Write(_db, "ORDER_IMAGE_DELETED", userId, "image", imageId.ToString());
        await _db.SaveChangesAsync();
    }

    private sealed record ComputedLine(Product Product, int? UnitId, decimal Qty, decimal UnitPrice, decimal LineTotal);
}
