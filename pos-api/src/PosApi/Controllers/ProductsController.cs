using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/products")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    private int UserId => User.Identity?.IsAuthenticated == true
        ? int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value)
        : 1;

    [HttpGet("barcode/{code}")]
    [Authorize(Policy = "AllRoles")]
    [ProducesResponseType(typeof(ProductLookupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ByBarcode(string code)
    {
        var result = await _productService.GetByBarcodeAsync(code);
        if (result == null)
            return NotFound(new { error = $"Không tìm thấy sản phẩm với mã '{code}'" });
        return Ok(result);
    }

    [HttpGet]
    [Authorize(Policy = "AllRoles")]
    [ProducesResponseType(typeof(PagedProductResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] int? categoryId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _productService.SearchAsync(q, categoryId, page, pageSize);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Policy = "ManagerOnly")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Create([FromForm] ProductCreateRequest req, IFormFile? imageFile)
    {
        var id = await _productService.CreateAsync(req, UserId, imageFile);
        return CreatedAtAction(nameof(ByBarcode), new { code = req.Barcode }, new { id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "ManagerOnly")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Update(int id, [FromForm] ProductUpdateRequest req, IFormFile? imageFile)
    {
        await _productService.UpdateAsync(id, req, UserId, imageFile);
        return NoContent();
    }

    [HttpPost("{id:int}/image")]
    [Authorize(Policy = "ManagerOnly")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateImage(int id, IFormFile imageFile)
    {
        if (imageFile == null || imageFile.Length == 0)
            return BadRequest(new { error = "File hình ảnh không được để trống" });

        try
        {
            var imageUrl = await _productService.UpdateImageAsync(id, imageFile, UserId);
            return Ok(new { id, imageUrl });
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

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "ManagerOnly")]
    public async Task<IActionResult> Delete(int id)
    {
        await _productService.DeleteAsync(id, UserId);
        return NoContent();
    }
}
