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

        var strategy = _db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();

            // Row lock (SELECT FOR UPDATE): 2 ca thu song song cùng một công nợ không ghi đè nhau
            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();
            int? lockedId = null;
            using (var cmd = conn.CreateCommand() as Npgsql.NpgsqlCommand)
            {
                cmd!.CommandText = "SELECT \"Id\" FROM \"Debts\" WHERE \"Id\" = @id FOR UPDATE";
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter("@id", id));
                lockedId = (await cmd.ExecuteScalarAsync()) as int?;
            }
            var d = lockedId is null
                ? null
                : await _db.Debts.FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new KeyNotFoundException($"Công nợ {id} không tồn tại");
            if (d is null) throw new KeyNotFoundException($"Công nợ {id} không tồn tại");

            var (debt, newRemaining) = _debts.ApplyPayment(d, req.Amount, req.Note, userId);

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return new PayDebtResponse(debt.Id, debt.Amount, debt.PaidAmount, newRemaining,
                debt.Status.ToString());
        });

        return Ok(result);
    }

    private static DebtResponse ToResponse(Debt d) =>
        new(d.Id, d.CustomerId, d.Customer.Phone, d.Customer.Name,
            d.Amount, d.PaidAmount, Math.Max(0m, d.Amount - d.PaidAmount),
            d.Status.ToString(), d.Note, d.CreatedAt);
}
