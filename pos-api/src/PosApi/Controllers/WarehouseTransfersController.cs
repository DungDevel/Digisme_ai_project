using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/warehouse-transfers")]
[Authorize]
public class WarehouseTransfersController : ControllerBase
{
    private readonly IWarehouseTransferService _service;

    public WarehouseTransfersController(IWarehouseTransferService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedWarehouseTransferResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] int? warehouseId,
        [FromQuery] TransferStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _service.GetTransfersAsync(warehouseId, status, page, pageSize);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(WarehouseTransferResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(int id)
    {
        var tr = await _service.GetTransferByIdAsync(id);
        if (tr is null)
            return NotFound(new { error = "Không tìm thấy phiếu chuyển kho" });
        return Ok(tr);
    }

    [HttpPost]
    [ProducesResponseType(typeof(WarehouseTransferResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] WarehouseTransferCreateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CreateTransferAsync(userId, req);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/confirm")]
    [ProducesResponseType(typeof(WarehouseTransferResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Confirm(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.ConfirmTransferAsync(userId, id);
        return Ok(result);
    }

    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(typeof(WarehouseTransferResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CancelTransferAsync(userId, id);
        return Ok(result);
    }
}
