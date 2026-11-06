using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/customer-groups")]
[Authorize(Policy = "ManagerOnly")]
public class CustomerGroupsController : ControllerBase
{
    private readonly ICustomerGroupService _service;

    public CustomerGroupsController(ICustomerGroupService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedCustomerGroupResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetList(
        [FromQuery] string? q,
        [FromQuery] bool? isSupplier,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool includeInactive = false)
    {
        var result = await _service.GetCustomerGroupsAsync(q, isSupplier, page, pageSize, includeInactive);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CustomerGroupResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(int id)
    {
        var group = await _service.GetCustomerGroupByIdAsync(id);
        if (group is null)
            return NotFound(new { error = "Không tìm thấy nhóm" });
        return Ok(group);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CustomerGroupResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CustomerGroupCreateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        try
        {
            var result = await _service.CreateCustomerGroupAsync(userId, req);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(CustomerGroupResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(int id, [FromBody] CustomerGroupUpdateRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        try
        {
            var result = await _service.UpdateCustomerGroupAsync(userId, id, req);
            return Ok(result);
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        try
        {
            var success = await _service.DeleteCustomerGroupAsync(userId, id);
            if (!success)
                return NotFound(new { error = "Không tìm thấy nhóm hoặc nhóm đã bị xóa" });
            return Ok(new { message = "Đã xóa nhóm thành công" });
        }
        catch (BusinessRuleException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
