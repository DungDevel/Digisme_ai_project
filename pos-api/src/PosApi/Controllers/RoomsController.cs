using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/rooms")]
[Authorize]
public class RoomsController : ControllerBase
{
    private readonly IRoomService _service;

    public RoomsController(IRoomService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedRoomResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] string? q,
        [FromQuery] RoomStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeInactive = false)
    {
        var result = await _service.GetRoomsAsync(q, status, page, pageSize, includeInactive);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(RoomResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(int id)
    {
        var room = await _service.GetRoomByIdAsync(id);
        if (room is null)
            return NotFound(new { error = "Không tìm thấy phòng" });
        return Ok(room);
    }

    [HttpPost]
    [Authorize(Policy = "ManagerOnly")]
    [ProducesResponseType(typeof(RoomResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] RoomCreateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CreateRoomAsync(userId, req);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "ManagerOnly")]
    [ProducesResponseType(typeof(RoomResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, [FromBody] RoomUpdateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.UpdateRoomAsync(userId, id, req);
        return Ok(result);
    }

    [HttpPost("{id:int}/check-in")]
    [ProducesResponseType(typeof(RoomResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckIn(int id, [FromBody] RoomCheckInRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CheckInRoomAsync(userId, id, req);
        return Ok(result);
    }

    [HttpPost("{id:int}/check-out")]
    [ProducesResponseType(typeof(RoomCheckOutResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CheckOut(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CheckOutRoomAsync(userId, id);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "ManagerOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var success = await _service.DeleteRoomAsync(userId, id);
        if (!success)
            return NotFound(new { error = "Không tìm thấy phòng" });
        return Ok(new { message = "Đã xóa phòng thành công" });
    }
}
