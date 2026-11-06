using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

/// <summary>
/// P0-4. CÔNG NỢ — đường trả nợ: Debt.Open không còn kẹt vĩnh viễn.
///  • GET  /api/v1/debts            — danh sách có phân trang + lọc (customerId, status, page)
///  • GET  /api/v1/debts/{id}       — chi tiết 1 công nợ
///  • POST /api/v1/debts/{id}/pay   — trả (toàn bộ hoặc một phần): Open → PartiallyPaid/Settled,
///    ghi PosLog cùng transaction. Trả một phần > remaining → 400.
/// Chính sách: đọc = AnyRole (ca thu ngân cần thấy nợ của khách đang bán);
/// thu nợ = ManagerOnly (tiền vào — nhạy cảm, chỉ quản lý).
/// </summary>
[ApiController]
[Route("api/v1/debts")]
[Authorize(Policy = "AnyRole")]
public class DebtsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly DebtService _debts;

    public DebtsController(AppDbContext db, DebtService debts)
    {
        _db = db;
        _debts = debts;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int? customerId, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        IQueryable<Debt> totalQuery = _db.Debts.AsNoTracking().Include(d => d.Customer);
        if (customerId is > 0) totalQuery = totalQuery.Where(d => d.CustomerId == customerId.Value);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (Enum.TryParse<DebtStatus>(status, ignoreCase: true, out var st))
                totalQuery = totalQuery.Where(d => d.Status == st);
            else
                return BadRequest(new { error = "status không hợp lệ (Open | PartiallyPaid | Settled)" });
        }

        var total = await totalQuery.CountAsync();
        var items = await totalQuery
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();
        var debts = items.Select(ToResponse).ToList();

        return Ok(new PagedDebtResponse(page, pageSize, total,
            (int)Math.Ceiling(total / (double)pageSize), debts));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var d = await _db.Debts.AsNoTracking().Include(x => x.Customer)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (d is null) return NotFound(new { error = $"Công nợ {id} không tồn tại" });
        return Ok(ToResponse(d));
    }

    /// <summary>Thu công nợ — toàn bộ hoặc một phần. ManagerOnly.</summary>
    [HttpPost("{id:int}/pay")]
    [Authorize(Policy = "ManagerOnly")]
    public async Task<IActionResult> Pay(int id, [FromBody] PayDebtRequest req)
    {
        if (req.Amount <= 0)
            return BadRequest(new { error = "Số tiền phải lớn hơn 0" });

        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        try
        {
            var result = await _debts.PayDebtAsync(userId, id, req.Amount, req.Note);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static DebtResponse ToResponse(Debt d) =>
        new(d.Id, d.CustomerId, d.Customer.Phone, d.Customer.Name,
            d.Amount, d.PaidAmount, Math.Max(0m, d.Amount - d.PaidAmount),
            d.Status.ToString(), d.Note, d.CreatedAt);
}
