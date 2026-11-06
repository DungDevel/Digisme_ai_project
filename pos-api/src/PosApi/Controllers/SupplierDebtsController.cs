using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/supplier-debts")]
[Authorize]
public class SupplierDebtsController : ControllerBase
{
    private readonly ISupplierDebtService _service;

    public SupplierDebtsController(ISupplierDebtService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedSupplierDebtResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] int? supplierId,
        [FromQuery] DebtStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _service.GetSupplierDebtsAsync(supplierId, status, page, pageSize);
        return Ok(result);
    }

    [HttpPost("{id:int}/pay")]
    [ProducesResponseType(typeof(PaySupplierDebtResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Pay(int id, [FromBody] PaySupplierDebtRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        try
        {
            var result = await _service.PaySupplierDebtAsync(userId, id, req.Amount, req.Note);
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
}
