using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/warehouse-stocks")]
[Authorize]
public class WarehouseStocksController : ControllerBase
{
    private readonly IWarehouseStockService _service;

    public WarehouseStocksController(IWarehouseStockService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedWarehouseStockResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] int? warehouseId,
        [FromQuery] int? productId,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _service.GetWarehouseStocksAsync(warehouseId, productId, q, page, pageSize);
        return Ok(result);
    }
}
