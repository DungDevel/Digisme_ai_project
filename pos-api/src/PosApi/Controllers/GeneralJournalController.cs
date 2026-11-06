using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/general-journal")]
[Authorize]
public class GeneralJournalController : ControllerBase
{
    private readonly IGeneralJournalService _service;

    public GeneralJournalController(IGeneralJournalService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedGeneralJournalResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _service.GetJournalEntriesAsync(fromDate, toDate, page, pageSize);
        return Ok(result);
    }
}
