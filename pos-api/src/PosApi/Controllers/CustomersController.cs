using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/customers")]
[Authorize(Policy = "AnyRole")]
public class CustomersController : ControllerBase
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    private int UserId => int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

    [HttpGet("by-phone/{phone}")]
    [ProducesResponseType(typeof(CustomerResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ByPhone(string phone)
    {
        var result = await _customerService.GetByPhoneAsync(phone);
        if (result == null)
            return NotFound(new { error = $"Không tìm thấy khách với SĐT '{phone}'" });
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CustomerCreateRequest req)
    {
        var id = await _customerService.CreateAsync(req, UserId);
        return CreatedAtAction(nameof(ByPhone), new { phone = req.Phone }, new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _customerService.SearchAsync(q, page, pageSize);
        return Ok(result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CustomerUpdateRequest req)
    {
        await _customerService.UpdateAsync(id, req, UserId);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "ManagerOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        await _customerService.DeleteAsync(id, UserId);
        return NoContent();
    }
}
