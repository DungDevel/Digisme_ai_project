using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/stock-takes")]
[Authorize]
public class StockTakesController : ControllerBase
{
    private readonly IStockTakeService _service;

    public StockTakesController(IStockTakeService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedStockTakeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] int? warehouseId,
        [FromQuery] StockTakeStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _service.GetStockTakesAsync(warehouseId, status, page, pageSize);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(StockTakeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(int id)
    {
        var st = await _service.GetStockTakeByIdAsync(id);
        if (st is null)
            return NotFound(new { error = "Không tìm thấy phiếu kiểm kê" });
        return Ok(st);
    }

    [HttpPost]
    [ProducesResponseType(typeof(StockTakeResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] StockTakeCreateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CreateStockTakeAsync(userId, req);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/confirm")]
    [ProducesResponseType(typeof(StockTakeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Confirm(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.ConfirmStockTakeAsync(userId, id);
        return Ok(result);
    }

    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(typeof(StockTakeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CancelStockTakeAsync(userId, id);
        return Ok(result);
    }
}
