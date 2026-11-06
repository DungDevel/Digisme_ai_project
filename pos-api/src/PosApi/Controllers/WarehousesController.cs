using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/warehouses")]
[Authorize]
public class WarehousesController : ControllerBase
{
    private readonly IWarehouseService _service;

    public WarehousesController(IWarehouseService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedWarehouseResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeInactive = false)
    {
        var result = await _service.GetWarehousesAsync(q, page, pageSize, includeInactive);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(WarehouseResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(int id)
    {
        var wh = await _service.GetWarehouseByIdAsync(id);
        if (wh is null)
            return NotFound(new { error = "Không tìm thấy kho hàng" });
        return Ok(wh);
    }

    [HttpPost]
    [Authorize(Policy = "ManagerOnly")]
    [ProducesResponseType(typeof(WarehouseResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] WarehouseCreateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CreateWarehouseAsync(userId, req);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "ManagerOnly")]
    [ProducesResponseType(typeof(WarehouseResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, [FromBody] WarehouseUpdateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.UpdateWarehouseAsync(userId, id, req);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "ManagerOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var success = await _service.DeleteWarehouseAsync(userId, id);
        if (!success)
            return NotFound(new { error = "Không tìm thấy kho hàng" });
        return Ok(new { message = "Đã xóa kho hàng thành công" });
    }
}
