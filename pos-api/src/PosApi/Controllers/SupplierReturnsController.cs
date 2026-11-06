using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/supplier-returns")]
[Authorize]
public class SupplierReturnsController : ControllerBase
{
    private readonly ISupplierReturnService _service;

    public SupplierReturnsController(ISupplierReturnService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedSupplierReturnResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] int? supplierId,
        [FromQuery] int? warehouseId,
        [FromQuery] SupplierReturnStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _service.GetSupplierReturnsAsync(supplierId, warehouseId, status, page, pageSize);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(SupplierReturnResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(int id)
    {
        var sr = await _service.GetSupplierReturnByIdAsync(id);
        if (sr is null)
            return NotFound(new { error = "Không tìm thấy phiếu trả nhà cung cấp" });
        return Ok(sr);
    }

    [HttpPost]
    [ProducesResponseType(typeof(SupplierReturnResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] SupplierReturnCreateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CreateSupplierReturnAsync(userId, req);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/confirm")]
    [ProducesResponseType(typeof(SupplierReturnResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Confirm(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.ConfirmSupplierReturnAsync(userId, id);
        return Ok(result);
    }

    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(typeof(SupplierReturnResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CancelSupplierReturnAsync(userId, id);
        return Ok(result);
    }
}
