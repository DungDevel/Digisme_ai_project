using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface IProductRecipeService
{
    Task<List<ProductRecipeResponse>> GetRecipesByProductAsync(int productId);
    Task<List<ProductRecipeResponse>> UpsertRecipesAsync(int userId, int productId, ProductRecipeUpsertRequest req);
}

public class ProductRecipeService : IProductRecipeService
{
    private readonly AppDbContext _db;

    public ProductRecipeService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<ProductRecipeResponse>> GetRecipesByProductAsync(int productId)
    {
        var product = await _db.Products.AnyAsync(x => x.Id == productId);
        if (!product)
            throw new KeyNotFoundException("Không tìm thấy sản phẩm");

        var recipes = await _db.ProductRecipes
            .AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Ingredient)
            .Include(x => x.Unit)
            .Where(x => x.ProductId == productId)
            .ToListAsync();

        return recipes.Select(MapToResponse).ToList();
    }

    public async Task<List<ProductRecipeResponse>> UpsertRecipesAsync(int userId, int productId, ProductRecipeUpsertRequest req)
    {
        var product = await _db.Products.FirstOrDefaultAsync(x => x.Id == productId);
        if (product is null)
            throw new KeyNotFoundException("Không tìm thấy sản phẩm thành phẩm");

        var existing = await _db.ProductRecipes.Where(x => x.ProductId == productId).ToListAsync();
        _db.ProductRecipes.RemoveRange(existing);

        if (req.Items != null && req.Items.Any())
        {
            foreach (var item in req.Items)
            {
                if (item.IngredientId == productId)
                    throw new BusinessRuleException("Sản phẩm không thể làm nguyên liệu của chính nó");
                if (item.Qty <= 0m)
                    throw new BusinessRuleException("Định lượng nguyên liệu phải lớn hơn 0");

                var ingredient = await _db.Products.FirstOrDefaultAsync(x => x.Id == item.IngredientId);
                if (ingredient is null)
                    throw new BusinessRuleException($"Không tìm thấy nguyên liệu có ID {item.IngredientId}");

                var recipe = new ProductRecipe
                {
                    ProductId = productId,
                    IngredientId = item.IngredientId,
                    Qty = item.Qty,
                    UnitId = item.UnitId
                };
                _db.ProductRecipes.Add(recipe);
            }
        }

        PosLogService.Write(_db, "RECIPES_UPDATED", userId, "product", product.Name);
        await _db.SaveChangesAsync();

        return await GetRecipesByProductAsync(productId);
    }

    private static ProductRecipeResponse MapToResponse(ProductRecipe r) => new(
        r.Id, r.ProductId, r.Product?.Name ?? string.Empty, r.IngredientId, r.Ingredient?.Name ?? string.Empty,
        r.Qty, r.UnitId, r.Unit?.Name
    );
}
