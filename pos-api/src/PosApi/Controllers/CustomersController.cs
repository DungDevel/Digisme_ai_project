using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

/// <summary>
/// M7. CRM — Sprint 1: F2 tra khách theo SĐT (hiện nhanh tổng nợ + điểm + tier khi đang bán).
/// Tạo khách mới khi chưa tồn tại; validate + chống duplicate theo SĐT.
/// </summary>
[ApiController]
[Route("api/v1/customers")]
[Authorize(Policy = "AnyRole")]
public class CustomersController : ControllerBase
{
    private readonly AppDbContext _db;
    public CustomersController(AppDbContext db) => _db = db;

    /// <summary>F2: tra khách theo SĐT — trả tổng nợ + điểm + tier, KHÔNG lộ dữ liệu nhạy cảm.</summary>
    [HttpGet("by-phone/{phone}")]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ByPhone(string phone)
    {
        var normalized = NormalizePhone(phone);
        if (string.IsNullOrEmpty(normalized))
            return NotFound(new { error = "Số điện thoại không hợp lệ" });

        var c = await _db.Customers.AsNoTracking()
            .Include(x => x.Loyalty)
            .FirstOrDefaultAsync(x => x.Phone == normalized && x.IsActive);
        if (c is null)
            return NotFound(new { error = $"Không tìm thấy khách với SĐT '{phone}'" });

        var totalDebt = await _db.Debts.AsNoTracking()
            .Where(d => d.CustomerId == c.Id && d.Status != DebtStatus.Settled)
            .SumAsync(d => d.Amount - d.PaidAmount);

        return Ok(ToResponse(c, totalDebt));
    }

    [HttpPost]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CustomerCreateRequest req)
    {
        var phone = NormalizePhone(req.Phone);
        if (string.IsNullOrEmpty(phone))
            return BadRequest(new { error = "Số điện thoại không hợp lệ (9–10 chữ số, bắt đầu 0)" });
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "Họ tên bắt buộc" });
        if (req.Name.Length > 128)
            return BadRequest(new { error = "Họ tên tối đa 128 ký tự" });

        var existing = await _db.Customers.FirstOrDefaultAsync(x => x.Phone == phone);
        if (existing is not null)
            return Conflict(new { error = $"Khách với SĐT '{phone}' đã tồn tại" });

        var cust = new Customer
        {
            Phone = phone,
            Name = req.Name.Trim(),
            Address = string.IsNullOrWhiteSpace(req.Address) ? null : req.Address.Trim()
        };
        cust.Loyalty = new LoyaltyAccount { Points = 0, Tier = "Silver", LifetimeSpend = 0m };
        _db.Customers.Add(cust);

        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        PosLogService.Write(_db, "CUSTOMER_CREATED", userId, "customer", phone);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Create), new { id = cust.Id }, ToResponse(cust, 0m));
    }

    /// <summary>Tìm khách theo tên/SĐT + phân trang.</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.Customers.AsNoTracking().Include(x => x.Loyalty).Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(x => x.Name.Contains(q) || x.Phone.Contains(q));
        }

        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var ids = items.Select(i => i.Id).ToList();
        var debts = await _db.Debts.AsNoTracking()
            .Where(d => ids.Contains(d.CustomerId) && d.Status != DebtStatus.Settled)
            .GroupBy(d => d.CustomerId)
            .Select(g => new { id = g.Key, sum = g.Sum(x => x.Amount - x.PaidAmount) })
            .ToDictionaryAsync(k => k.id, v => v.sum);

        return Ok(new PagedCustomerResponse(page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize),
            items.Select(c => ToResponse(c, debts.GetValueOrDefault(c.Id, 0m))).ToList()));
    }

    private static CustomerResponse ToResponse(Customer c, decimal totalDebt) =>
        new(c.Id, c.Phone, c.Name, c.Address, c.IsActive, c.Loyalty?.Points ?? 0, c.Loyalty?.Tier ?? "Silver", totalDebt);

    private static string? NormalizePhone(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        var digits = new string(p.Where(char.IsDigit).ToArray());
        // chuẩn hoá: bỏ 84/0084 đầu, giữ 9–10 chữ số
        if (digits.StartsWith("84") && digits.Length is 11 or 12) digits = digits[2..];
        return digits.Length is >= 9 and <= 10 ? digits : null;
    }
}
