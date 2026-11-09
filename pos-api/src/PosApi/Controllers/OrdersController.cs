using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/orders")]
[Authorize(Policy = "AnyRole")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orders;

    public OrdersController(IOrderService orders) => _orders = orders;

    private int UserId => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpPost]
    [ProducesResponseType(typeof(OrderCreatedResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest req)
    {
        var result = await _orders.CreateOrderAsync(UserId, req, Request.Headers["Idempotency-Key"].ToString());
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedOrderResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery] int? customerId,
        [FromQuery] int? cashierId,
        [FromQuery] int? shiftId,
        [FromQuery] OrderStatus? status,
        [FromQuery] PaymentMethod? paymentMethod,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _orders.GetOrdersAsync(fromDate, toDate, customerId, cashierId, shiftId, status, paymentMethod, page, pageSize);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(OrderDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var result = await _orders.GetOrderByIdAsync(id);
        return Ok(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] OrderUpdateRequest req)
    {
        await _orders.UpdateOrderAsync(id, req, UserId);
        return NoContent();
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, [FromBody] string? reason)
    {
        await _orders.CancelOrderAsync(id, UserId, reason ?? "Hủy đơn hàng");
        return NoContent();
    }

    [HttpPost("{id:int}/images")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadImage(int id, [FromForm] string? imageType, IFormFile file)
    {
        var result = await _orders.AddOrderImageAsync(id, UserId, new OrderImageUploadRequest(imageType ?? "Proof", file));
        return Created("", result);
    }

    [HttpDelete("images/{imageId:int}")]
    public async Task<IActionResult> DeleteImage(int imageId)
    {
        await _orders.DeleteOrderImageAsync(imageId, UserId);
        return NoContent();
    }
}
