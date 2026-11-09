using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Services;

public interface IProductService
{
    Task<ProductLookupResponse> GetByBarcodeAsync(string code);
    Task<PagedProductResponse> SearchAsync(string? q, int? categoryId, int page, int pageSize);
    Task<int> CreateAsync(ProductCreateRequest req, int userId, IFormFile? imageFile);
    Task UpdateAsync(int id, ProductUpdateRequest req, int userId, IFormFile? imageFile);
    Task DeleteAsync(int id, int userId);
}

public class ProductService : IProductService
{
    private readonly AppDbContext _db;
    private readonly IFileStorageService _storage;
    private readonly ILogger<ProductService> _log;

    public ProductService(AppDbContext db, IFileStorageService storage, ILogger<ProductService> log)
    {
        _db = db;
        _storage = storage;
        _log = log;
    }

    public async Task<ProductLookupResponse> GetByBarcodeAsync(string code)
    {
        var p = await _db.Products
            .Include(x => x.Category)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Barcode == code && x.IsActive);

        if (p == null) return null!;

        var baseUnit = await _db.ProductUnits
            .AsNoTracking()
            .Include(x => x.Unit)
            .FirstOrDefaultAsync(x => x.ProductId == p.Id && x.IsBase);

        return new ProductLookupResponse(
            p.Id, p.Name, p.Barcode, p.CategoryId, p.Category.Name,
            p.RetailPrice, p.WholesalePrice, p.MemberPrice,
            p.CurrentStock, baseUnit?.Unit?.Name ?? "cái",
            p.CurrentStock <= p.MinStock, p.IsWeighed, p.ImageUrl);
    }

    public async Task<PagedProductResponse> SearchAsync(string? q, int? categoryId, int page, int pageSize)
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

        return new PagedProductResponse(page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize),
            items.Select(p => new ProductBrief(
                p.Id, p.Name, p.Barcode, p.Category.Name, p.RetailPrice, p.CurrentStock,
                baseUnits.TryGetValue(p.Id, out var u) ? u.Unit?.Name ?? "cái" : "cái", p.ImageUrl)).ToList());
    }

    public async Task<int> CreateAsync(ProductCreateRequest req, int userId, IFormFile? imageFile)
    {
        var existing = await _db.Products.AnyAsync(x => x.Barcode == req.Barcode && x.IsActive);
        if (existing) throw new BusinessRuleException($"Barcode '{req.Barcode}' đã tồn tại.");

        string? imageUrl = null;
        if (imageFile != null)
        {
            imageUrl = await _storage.SaveFileAsync(imageFile, "products");
        }

        var p = new Product
        {
            Name = req.Name.Trim(),
            CategoryId = req.CategoryId,
            Barcode = req.Barcode.Trim(),
            CostPrice = req.CostPrice,
            RetailPrice = req.RetailPrice,
            WholesalePrice = req.WholesalePrice,
            MemberPrice = req.MemberPrice,
            MinStock = req.MinStock,
            IsWeighed = req.IsWeighed,
            ImageUrl = imageUrl
        };

        _db.Products.Add(p);
        PosLogService.Write(_db, "PRODUCT_CREATED", userId, "product", p.Barcode, new { p.Name });
        await _db.SaveChangesAsync();

        return p.Id;
    }

    public async Task UpdateAsync(int id, ProductUpdateRequest req, int userId, IFormFile? imageFile)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) throw new KeyNotFoundException($"Không tìm thấy sản phẩm #{id}");

        // Log giá cũ nếu có thay đổi
        if (p.RetailPrice != req.RetailPrice || p.WholesalePrice != req.WholesalePrice || p.MemberPrice != req.MemberPrice)
        {
            PosLogService.Write(_db, "PRODUCT_PRICE_UPDATED", userId, "product", p.Barcode,
                new { Old = new { p.RetailPrice, p.WholesalePrice, p.MemberPrice }, New = new { req.RetailPrice, req.WholesalePrice, req.MemberPrice } });
        }

        p.Name = req.Name.Trim();
        p.CategoryId = req.CategoryId;
        p.CostPrice = req.CostPrice;
        p.RetailPrice = req.RetailPrice;
        p.WholesalePrice = req.WholesalePrice;
        p.MemberPrice = req.MemberPrice;
        p.MinStock = req.MinStock;
        p.IsWeighed = req.IsWeighed;
        p.IsActive = req.IsActive;
        p.UpdatedAt = DateTime.UtcNow;

        if (imageFile != null)
        {
            // Xóa ảnh cũ nếu có
            if (!string.IsNullOrEmpty(p.ImageUrl))
            {
                await _storage.DeleteFileAsync(p.ImageUrl);
            }
            p.ImageUrl = await _storage.SaveFileAsync(imageFile, "products");
        }

        PosLogService.Write(_db, "PRODUCT_UPDATED", userId, "product", p.Barcode, new { p.Name });
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id, int userId)
    {
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id);
        if (p == null) throw new KeyNotFoundException($"Không tìm thấy sản phẩm #{id}");

        p.IsActive = false; // Soft Delete
        PosLogService.Write(_db, "PRODUCT_DEACTIVATED", userId, "product", p.Barcode, new { p.Name });
        await _db.SaveChangesAsync();
    }
}
