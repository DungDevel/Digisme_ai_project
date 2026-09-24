using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Dto;

namespace PosApi.Controllers;

[ApiController]
[Route("api/v1/products")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    public ProductsController(AppDbContext db) => _db = db;

    /// <summary>
    /// Tra cứu nhanh theo mã vạch — Sprint 1 yêu cầu p99 &lt; 50ms.
    /// Query đơn bảng, index unique trên barcode, không JOIN ngoài category name.
    /// </summary>
    [HttpGet("barcode/{code}")]
    [ProducesResponseType(typeof(ProductLookupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ByBarcode(string code)
    {
        var p = await _db.Products
            .Include(x => x.Category)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Barcode == code);

        if (p is null || !p.IsActive)
            return NotFound(new { error = $"Không tìm thấy sản phẩm với mã '{code}'" });

        var baseUnit = await _db.ProductUnits
            .AsNoTracking()
            .Include(x => x.Unit)
            .FirstOrDefaultAsync(x => x.ProductId == p.Id && x.IsBase);

        return Ok(new ProductLookupResponse(
            p.Id, p.Name, p.Barcode, p.CategoryId, p.Category.Name,
            p.RetailPrice, p.WholesalePrice, p.MemberPrice,
            p.CurrentStock, baseUnit?.Unit?.Name ?? "cái",
            p.CurrentStock <= p.MinStock, p.IsWeighed));
    }

    /// <summary>Tìm kiếm + phân trang (keyset bằng page/size), filter theo nhóm.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedProductResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string? q,
        [FromQuery] int? categoryId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.Products.AsNoTracking().Include(x => x.Category).Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.Name.Contains(q) || x.Barcode.Contains(q));
        if (categoryId is > 0)
            query = query.Where(x => x.CategoryId == categoryId.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(x => x.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var unitIds = items.Select(i => i.Id).ToList();
        var baseUnits = await _db.ProductUnits.AsNoTracking()
            .Include(x => x.Unit)
            .Where(x => x.IsBase && unitIds.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId);

        return Ok(new PagedProductResponse(page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize),
            items.Select(p => new ProductBrief(
                p.Id, p.Name, p.Barcode, p.Category.Name, p.RetailPrice, p.CurrentStock,
                baseUnits.TryGetValue(p.Id, out var u) ? u.Unit?.Name ?? "cái" : "cái")).ToList()));
    }
}
