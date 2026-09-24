using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

/// <summary>
/// M4. ORDERS — Sprint 1: POST /orders thật (BE-01).
/// Giá/tiền/vat/tiền thừa do server tính lại; Idempotency-Key chống đơn trùng (F9/retry).
/// </summary>
[ApiController]
[Route("api/v1/orders")]
[Authorize(Policy = "AnyRole")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orders;

    public OrdersController(IOrderService orders) => _orders = orders;

    [HttpPost]
    [ProducesResponseType(typeof(OrderCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _orders.CreateOrderAsync(userId, req, Request.Headers["Idempotency-Key"].ToString());
        return CreatedAtAction(nameof(Create), new { id = result.Id }, result);
    }
}
