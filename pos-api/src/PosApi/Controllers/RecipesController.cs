using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/products/{productId:int}/recipes")]
[Authorize(Policy = "ManagerOnly")]
public class RecipesController : ControllerBase
{
    private readonly IProductRecipeService _service;

    public RecipesController(IProductRecipeService service)
    {
        _service = service;
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<ProductRecipeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int productId)
    {
        var result = await _service.GetRecipesByProductAsync(productId);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(List<ProductRecipeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Upsert(int productId, [FromBody] ProductRecipeUpsertRequest req)
    {
        var userId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        var result = await _service.UpsertRecipesAsync(userId, productId, req);
        return Ok(result);
    }
}
