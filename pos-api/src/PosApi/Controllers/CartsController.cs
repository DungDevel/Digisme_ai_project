using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/carts")]
[Authorize(Policy = "AnyRole")]
public class CartsController : ControllerBase
{
    private readonly ICartService _carts;
    public CartsController(ICartService carts) => _carts = carts;

    private int UserId => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpPost("hold")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<IActionResult> Hold([FromBody] HoldCartRequest req)
    {
        var id = await _carts.HoldAsync(UserId, req);
        return CreatedAtAction(nameof(Hold), new { id }, new { id });
    }

    [HttpGet("hold")]
    public async Task<IActionResult> HeldList() => Ok(await _carts.ListHeldAsync(UserId));

    [HttpPost("{id:int}/resume")]
    public async Task<IActionResult> Resume(int id) => Ok(await _carts.ResumeAsync(UserId, id));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Cancel(int id)
    {
        await _carts.CancelAsync(UserId, id);
        return NoContent();
    }
}
