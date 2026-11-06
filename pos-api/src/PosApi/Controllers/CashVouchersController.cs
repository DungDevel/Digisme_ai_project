using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/cash-vouchers")]
[Authorize]
public class CashVouchersController : ControllerBase
{
    private readonly ICashVoucherService _service;

    public CashVouchersController(ICashVoucherService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedCashVoucherResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] CashVoucherType? type,
        [FromQuery] PartnerType? partnerType,
        [FromQuery] int? partnerId,
        [FromQuery] CashVoucherStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _service.GetVouchersAsync(type, partnerType, partnerId, status, page, pageSize);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CashVoucherResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(int id)
    {
        var voucher = await _service.GetVoucherByIdAsync(id);
        if (voucher is null)
            return NotFound(new { error = "Không tìm thấy phiếu thu/chi" });
        return Ok(voucher);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CashVoucherResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CashVoucherCreateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CreateVoucherAsync(userId, req);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/confirm")]
    [ProducesResponseType(typeof(CashVoucherResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Confirm(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.ConfirmVoucherAsync(userId, id);
        return Ok(result);
    }

    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(typeof(CashVoucherResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.CancelVoucherAsync(userId, id);
        return Ok(result);
    }
}
