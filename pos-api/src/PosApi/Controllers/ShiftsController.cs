using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

/// <summary>
/// SPRINT 3. SHIFTS (QUẢN LÝ CA LÀM VIỆC)
/// - POST /api/v1/shifts/open   — Mở ca làm việc (AnyRole)
/// - GET  /api/v1/shifts/current — Xem ca đang mở của user (AnyRole)
/// - POST /api/v1/shifts/close  — Đóng ca + kiểm đếm tiền mặt (AnyRole)
/// - GET  /api/v1/shifts        — Lịch sử ca làm việc (ManagerOnly)
/// </summary>
[ApiController]
[Route("api/v1/shifts")]
[Authorize(Policy = "AnyRole")]
public class ShiftsController : ControllerBase
{
    private readonly IShiftService _shiftService;

    public ShiftsController(IShiftService shiftService)
    {
        _shiftService = shiftService;
    }

    private int UserId => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpPost("open")]
    [ProducesResponseType(typeof(ShiftResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Open([FromBody] OpenShiftRequest req)
    {
        try
        {
            var result = await _shiftService.OpenShiftAsync(UserId, req.OpeningCash);
            return StatusCode(201, result);
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("current")]
    [ProducesResponseType(typeof(ShiftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Current()
    {
        var shift = await _shiftService.GetCurrentShiftAsync(UserId);
        if (shift == null)
            return NotFound(new { error = "Không có ca làm việc nào đang mở" });
        return Ok(shift);
    }

    [HttpPost("close")]
    [ProducesResponseType(typeof(ShiftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Close([FromBody] CloseShiftRequest req)
    {
        try
        {
            var result = await _shiftService.CloseShiftAsync(UserId, req.CountedCash);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet]
    [Authorize(Policy = "ManagerOnly")]
    [ProducesResponseType(typeof(PagedShiftResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _shiftService.ListShiftsAsync(page, pageSize);
        return Ok(result);
    }
}
