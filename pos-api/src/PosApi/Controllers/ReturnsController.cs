using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

/// <summary>
/// M5. TRẢ HÀNG (RETURNS)
/// - POST /api/v1/returns        — Tạo phiếu trả hàng: hoàn kho + ghi sổ (Manager Only)
/// - GET  /api/v1/returns        — Lịch sử trả hàng (Manager Only)
/// - GET  /api/v1/returns/{id}    — Chi tiết phiếu trả (Manager Only)
/// </summary>
[ApiController]
[Route("api/v1/returns")]
[Authorize(Policy = "ManagerOnly")]
public class ReturnsController : ControllerBase
{
    private readonly IReturnService _returns;

    public ReturnsController(IReturnService returns)
    {
        _returns = returns;
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ReturnRequest req)
    {
        // Lấy UserId từ Token
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null) return Unauthorized();
        var userId = int.Parse(userIdClaim.Value);

        try
        {
            var result = await _returns.CreateReturnAsync(userId, req);
            return CreatedAtAction(nameof(Get), new { id = (int)0 }, result); // Simplified CreatedAtAction
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
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _returns.ListReturnsAsync(page, pageSize);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        try
        {
            var result = await _returns.GetReturnByIdAsync(id);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
