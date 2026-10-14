using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

/// <summary>
/// SPRINT 3. PAYMENTS & VIETQR (THANH TOÁN QR ĐỘNG)
/// - POST /api/v1/payments/vietqr     — Tạo mã VietQR động cho đơn hàng (AnyRole)
/// - POST /api/v1/payments/{id}/confirm — Xác nhận trạng thái thanh toán (ManagerOnly)
/// </summary>
[ApiController]
[Route("api/v1/payments")]
[Authorize(Policy = "AnyRole")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    private int UserId => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpPost("vietqr")]
    [ProducesResponseType(typeof(VietQrResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateVietQr([FromBody] GenerateVietQrRequest req)
    {
        try
        {
            var result = await _paymentService.GenerateVietQrAsync(req.OrderId);
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

    [HttpPost("{id:int}/confirm")]
    [Authorize(Policy = "ManagerOnly")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Confirm(int id, [FromBody] ConfirmPaymentRequest req)
    {
        try
        {
            var result = await _paymentService.ConfirmPaymentAsync(id, UserId, req.Status, req.Reference);
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
