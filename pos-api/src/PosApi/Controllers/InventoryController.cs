using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

/// <summary>
/// M6. INVENTORY (QUẢN LÝ KHO)
/// - GET /api/v1/inventory           — Danh sách tồn kho (AnyRole)
/// - GET /api/v1/inventory/{productId} — Chi tiết tồn kho + Lịch sử (AnyRole)
/// - GET /api/v1/inventory/low-stock — Danh sách tồn kho thấp (AnyRole)
/// - POST /api/v1/inventory/adjust   — Điều chỉnh kho (ManagerOnly)
/// </summary>
[ApiController]
[Route("api/v1/inventory")]
[Authorize(Policy = "AnyRole")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventory;

    public InventoryController(IInventoryService inventory)
    {
        _inventory = inventory;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? productId, [FromQuery] int? categoryId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _inventory.ListInventoryAsync(page, pageSize, productId, categoryId);
        return Ok(result);
    }

    [HttpGet("low-stock")]
    public async Task<IActionResult> ListLowStock([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _inventory.ListLowStockAsync(page, pageSize);
        return Ok(result);
    }

    [HttpGet("{productId:int}")]
    public async Task<IActionResult> GetProductInventory(int productId)
    {
        try
        {
            var result = await _inventory.GetProductStockHistoryAsync(productId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost("adjust")]
    [Authorize(Policy = "ManagerOnly")]
    public async Task<IActionResult> Adjust([FromBody] InventoryAdjustRequest req, [FromQuery] int productId)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null) return Unauthorized();
        var userId = int.Parse(userIdClaim.Value);

        try
        {
            var result = await _inventory.AdjustStockAsync(userId, productId, req);
            return Ok(new { success = true, message = "Điều chỉnh kho thành công" });
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
}
