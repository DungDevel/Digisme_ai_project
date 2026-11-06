using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/tables")]
[Authorize]
public class TablesController : ControllerBase
{
    private readonly ITableService _service;

    public TablesController(ITableService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedTableResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] int? areaId,
        [FromQuery] TableStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeInactive = false)
    {
        var result = await _service.GetTablesAsync(areaId, status, page, pageSize, includeInactive);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(TableResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(int id)
    {
        var table = await _service.GetTableByIdAsync(id);
        if (table is null)
            return NotFound(new { error = "Không tìm thấy bàn" });
        return Ok(table);
    }

    [HttpPost]
    [Authorize(Policy = "ManagerOnly")]
    [ProducesResponseType(typeof(TableResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] TableCreateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CreateTableAsync(userId, req);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "ManagerOnly")]
    [ProducesResponseType(typeof(TableResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, [FromBody] TableUpdateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.UpdateTableAsync(userId, id, req);
        return Ok(result);
    }

    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(typeof(TableResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] TableStatusUpdateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.UpdateTableStatusAsync(userId, id, req.Status);
        return Ok(result);
    }

    [HttpPost("transfer")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Transfer([FromBody] TransferTableRequest req, [FromQuery] int sourceTableId)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        await _service.TransferTableAsync(userId, sourceTableId, req.TargetTableId);
        return Ok(new { message = "Chuyển bàn thành công" });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "ManagerOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var success = await _service.DeleteTableAsync(userId, id);
        if (!success)
            return NotFound(new { error = "Không tìm thấy bàn" });
        return Ok(new { message = "Đã xóa bàn thành công" });
    }
}
