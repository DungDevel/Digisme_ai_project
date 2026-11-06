using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

/// <summary>
/// Quản lý Nhà cung cấp (Supplier) — ADR 0001
/// Yêu cầu quyền Manager (tham khảo StockInsController.cs:19).
/// </summary>
[ApiController]
[Route("api/v1/suppliers")]
[Authorize(Policy = "ManagerOnly")]
public class SuppliersController : ControllerBase
{
    private readonly ISupplierService _supplierService;

    public SuppliersController(ISupplierService supplierService)
    {
        _supplierService = supplierService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedSupplierResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSuppliers([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] bool includeInactive = false)
    {
        var result = await _supplierService.GetSuppliersAsync(q, page, pageSize, includeInactive);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(SupplierResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSupplier(int id)
    {
        var supplier = await _supplierService.GetSupplierByIdAsync(id);
        if (supplier is null)
            return NotFound(new { error = $"Không tìm thấy nhà cung cấp với ID {id}" });
        return Ok(supplier);
    }

    [HttpPost]
    [ProducesResponseType(typeof(SupplierResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateSupplier([FromBody] SupplierCreateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        try
        {
            var created = await _supplierService.CreateSupplierAsync(userId, req);
            return StatusCode(StatusCodes.Status201Created, created);
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(SupplierResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateSupplier(int id, [FromBody] SupplierUpdateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        try
        {
            var updated = await _supplierService.UpdateSupplierAsync(userId, id, req);
            if (updated is null)
                return NotFound(new { error = $"Không tìm thấy nhà cung cấp với ID {id}" });
            return Ok(updated);
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteSupplier(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var success = await _supplierService.DeleteSupplierAsync(userId, id);
        if (!success)
            return NotFound(new { error = $"Không tìm thấy nhà cung cấp hoặc đã bị xóa" });
        return Ok(new { message = "Xóa mềm nhà cung cấp thành công" });
    }
}
