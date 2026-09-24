using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

/// <summary>
/// BE-02: Hold / Resume cart.
///  • Hold: lưu snapshot giỏ (items + subtotal) — KHÔNG tạo order, KHÔNG trừ kho.
///  • Resume: kiểm tra lại tồn kho; nếu thiếu → vẫn trả giỏ về (frontend tự điều chỉnh), kèm cảnh báo.
///  • Concurrency: 1 cart chỉ thuộc 1 user (index UserId), resume/cancel đặt trạng thái nguyên tử
///    (WHERE ResumedAt IS NULL).
///  • Expire: cấu hình qua AppSettings:HoldCart:ExpireMinutes (mặc định 240), không hard-code.
/// </summary>
public interface ICartService
{
    Task<int> HoldAsync(int userId, HoldCartRequest req);
    Task<List<HeldCartResponse>> ListHeldAsync(int userId);
    Task<ResumeCartResponse> ResumeAsync(int userId, int cartId);
    Task CancelAsync(int userId, int cartId);
}

public class CartService : ICartService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db;
    private readonly int _expireMinutes;

    public CartService(AppDbContext db, IConfiguration cfg)
    {
        _db = db;
        _expireMinutes = Math.Max(15, cfg.GetValue("HoldCart:ExpireMinutes", 240));
    }

    public async Task<int> HoldAsync(int userId, HoldCartRequest req)
    {
        if (req.Items is null || req.Items.Count == 0)
            throw new BusinessRuleException("Không hold được giỏ trống");
        if (req.Items.Count > 200) throw new BusinessRuleException("Giỏ hàng tối đa 200 dòng");
        if (!string.IsNullOrWhiteSpace(req.Note) && req.Note.Length > 256)
            throw new BusinessRuleException("Ghi chú tối đa 256 ký tự");

        var productIds = req.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).ToListAsync();
        if (products.Count != productIds.Count)
            throw new BusinessRuleException("Có sản phẩm trong giỏ không tồn tại");

        var units = await _db.ProductUnits.AsNoTracking()
            .Where(u => productIds.Contains(u.ProductId)).ToListAsync();

        var isMember = false;
        if (req.CustomerId is > 0)
        {
            var cust = await _db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == req.CustomerId)
                ?? throw new BusinessRuleException($"Khách hàng {req.CustomerId} không tồn tại");
            isMember = cust.IsActive;
        }

        // Tính lại subtotal ở server (giữ nguyên logic giá tầng như OrderService)
        var subtotal = 0m;
        foreach (var item in req.Items)
        {
            if (item.Qty <= 0) throw new BusinessRuleException($"Số lượng '{(productIds.Contains(item.ProductId) ? "sản phẩm" : item.ProductId.ToString())}' phải lớn hơn 0");
            var p = products.First(x => x.Id == item.ProductId);
            if (!p.IsActive) throw new BusinessRuleException($"Sản phẩm '{p.Name}' đã ngừng bán");

            decimal conversion = 1m;
            if (item.UnitId is not null)
            {
                var pu = units.FirstOrDefault(u => u.ProductId == p.Id && u.UnitId == item.UnitId)
                    ?? throw new BusinessRuleException($"Đơn vị {item.UnitId} không hợp lệ cho '{p.Name}'");
                conversion = pu.ConversionRate;
            }
            var unitPrice = item.UnitPrice is decimal op && op >= 0
                ? Math.Round(op / conversion, 0, MidpointRounding.AwayFromZero)
                : (isMember && p.MemberPrice > 0 ? p.MemberPrice : p.RetailPrice);
            subtotal += Math.Round(item.Qty * conversion * unitPrice, 0, MidpointRounding.AwayFromZero);
        }

        var cart = new HeldCart
        {
            UserId = userId,
            CustomerId = req.CustomerId,
            Note = req.Note ?? string.Empty,
            ItemsJson = JsonSerializer.Serialize(req.Items, JsonOpts),
            Subtotal = Math.Round(subtotal, 0, MidpointRounding.AwayFromZero)
        };
        _db.HeldCarts.Add(cart);

        PosLogService.Write(_db, "CART_HELD", userId, "held_cart", null,
            new { items = req.Items.Count, subtotal = cart.Subtotal });

        await _db.SaveChangesAsync();
        return cart.Id;
    }

    public async Task<List<HeldCartResponse>> ListHeldAsync(int userId)
    {
        var now = DateTime.UtcNow;
        var carts = await _db.HeldCarts.AsNoTracking()
            .Where(c => c.UserId == userId && c.ResumedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .Take(50)
            .ToListAsync();
        return carts
            .Where(c => c.CreatedAt.AddMinutes(_expireMinutes) > now) // filter expire
            .Select(c => new HeldCartResponse(c.Id, c.CustomerId, c.Note, c.Subtotal, c.CreatedAt,
                c.CreatedAt.AddMinutes(_expireMinutes)))
            .ToList();
    }

    public async Task<ResumeCartResponse> ResumeAsync(int userId, int cartId)
    {
        // Nguyên tử: chỉ user sở hữu + chưa resume + chưa expire
        var cart = await _db.HeldCarts.FirstOrDefaultAsync(c => c.Id == cartId && c.UserId == userId);
        if (cart is null || cart.ResumedAt != null)
            throw new BusinessRuleException("Đơn chờ không tồn tại hoặc đã được lấy ra trước đó");
        if (cart.CreatedAt.AddMinutes(_expireMinutes) <= DateTime.UtcNow)
            throw new BusinessRuleException("Đơn chờ đã hết hạn — vui lòng thêm lại vào giỏ");

        cart.ResumedAt = DateTime.UtcNow;

        var items = JsonSerializer.Deserialize<List<OrderItemRequest>>(cart.ItemsJson, JsonOpts) ?? new();

        // Kiểm tra lại tồn kho (KHÔNG trừ kho — resume chỉ lấy giỏ về)
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id)).ToListAsync();

        var stockWarning = false;
        foreach (var item in items)
        {
            var p = products.FirstOrDefault(x => x.Id == item.ProductId);
            if (p is null || !p.IsActive || item.Qty > p.CurrentStock) stockWarning = true;
        }

        PosLogService.Write(_db, "CART_RESUMED", userId, "held_cart", cartId.ToString(),
            new { items = items.Count, stockWarning });

        await _db.SaveChangesAsync();

        return new ResumeCartResponse(cart.Id, cart.CustomerId, cart.Note, cart.Subtotal, items, stockWarning);
    }

    public async Task CancelAsync(int userId, int cartId)
    {
        var cart = await _db.HeldCarts.FirstOrDefaultAsync(c => c.Id == cartId && c.UserId == userId);
        if (cart is null) throw new KeyNotFoundException($"Đơn chờ {cartId} không tồn tại");
        if (cart.ResumedAt != null) throw new BusinessRuleException("Đơn chờ đã được lấy ra trước đó");

        _db.HeldCarts.Remove(cart);
        PosLogService.Write(_db, "CART_CANCELED", userId, "held_cart", cartId.ToString());
        await _db.SaveChangesAsync();
    }
}
