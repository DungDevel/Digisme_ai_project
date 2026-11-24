using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/bank-transactions")]
[Authorize(Policy = "ManagerOnly")]
public class BankTransactionsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly BankReconciliationService _reconciliationService;

    public BankTransactionsController(AppDbContext context, BankReconciliationService reconciliationService)
    {
        _context = context;
        _reconciliationService = reconciliationService;
    }

    private int UserId => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    [ProducesResponseType(typeof(PagedBankTransactionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListTransactions(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _context.BankTransactions
            .AsNoTracking()
            .Include(x => x.Order)
            .Include(x => x.ResolvedByUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BankTransactionStatus>(status, true, out var st))
        {
            query = query.Where(x => x.Status == st);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(x => x.TransactionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new BankTransactionResponse(
                x.Id,
                x.Provider,
                x.ExternalId,
                x.AccountNumber,
                x.Amount,
                x.Content,
                x.ReferenceCode,
                x.TransactionDate,
                x.Status.ToString(),
                x.UnmatchedReason.HasValue ? x.UnmatchedReason.Value.ToString() : null,
                x.OrderId,
                x.Order != null ? x.Order.Code : null,
                x.MatchedAt,
                x.ResolvedByUserId,
                x.ResolutionNote,
                x.CreatedAt
            ))
            .ToListAsync();

        return Ok(new PagedBankTransactionResponse(page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize), items));
    }

    [HttpPost("{id:int}/match")]
    [ProducesResponseType(typeof(BankTransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MatchTransaction(int id, [FromBody] MatchBankTransactionRequest req)
    {
        try
        {
            var res = await _reconciliationService.ManualMatchAsync(id, req.OrderId, req.Note, UserId);
            return Ok(res);
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

    [HttpPost("{id:int}/ignore")]
    [ProducesResponseType(typeof(BankTransactionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IgnoreTransaction(int id, [FromBody] IgnoreBankTransactionRequest req)
    {
        try
        {
            var res = await _reconciliationService.ManualIgnoreAsync(id, req.Note, UserId);
            return Ok(res);
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
}
