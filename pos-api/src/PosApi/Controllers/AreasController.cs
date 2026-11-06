using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/areas")]
[Authorize]
public class AreasController : ControllerBase
{
    private readonly IAreaService _service;

    public AreasController(IAreaService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedAreaResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeInactive = false)
    {
        var result = await _service.GetAreasAsync(q, page, pageSize, includeInactive);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AreaResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(int id)
    {
        var area = await _service.GetAreaByIdAsync(id);
        if (area is null)
            return NotFound(new { error = "Không tìm thấy khu vực" });
        return Ok(area);
    }

    [HttpPost]
    [Authorize(Policy = "ManagerOnly")]
    [ProducesResponseType(typeof(AreaResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] AreaCreateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CreateAreaAsync(userId, req);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "ManagerOnly")]
    [ProducesResponseType(typeof(AreaResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, [FromBody] AreaUpdateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.UpdateAreaAsync(userId, id, req);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "ManagerOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var success = await _service.DeleteAreaAsync(userId, id);
        if (!success)
            return NotFound(new { error = "Không tìm thấy khu vực" });
        return Ok(new { message = "Đã xóa khu vực thành công" });
    }
}
