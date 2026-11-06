using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/cash-fund-book")]
[Authorize]
public class CashFundBookController : ControllerBase
{
    private readonly ICashFundBookService _service;

    public CashFundBookController(ICashFundBookService service)
    {
        _service = service;
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(CashFundBookSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate)
    {
        var result = await _service.GetFundBookSummaryAsync(fromDate, toDate);
        return Ok(result);
    }

    [HttpGet("ledger")]
    [ProducesResponseType(typeof(PagedCashLedgerResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLedger(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _service.GetFundLedgerEntriesAsync(fromDate, toDate, page, pageSize);
        return Ok(result);
    }
}
