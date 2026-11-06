using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Dto;

namespace PosApi.Services;

public interface IWarehouseStockService
{
    Task<PagedWarehouseStockResponse> GetWarehouseStocksAsync(int? warehouseId, int? productId, string? q, int page, int pageSize);
}

public class WarehouseStockService : IWarehouseStockService
{
    private readonly AppDbContext _db;

    public WarehouseStockService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedWarehouseStockResponse> GetWarehouseStocksAsync(int? warehouseId, int? productId, string? q, int page, int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.WarehouseStocks
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Product)
            .AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(x => x.WarehouseId == warehouseId.Value);
        }

        if (productId.HasValue)
        {
            query = query.Where(x => x.ProductId == productId.Value);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(x => x.Product.Name.Contains(q) || x.Product.Barcode.Contains(q) || x.Warehouse.Name.Contains(q));
        }

        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.WarehouseId).ThenBy(x => x.ProductId).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var responses = items.Select(x => new WarehouseStockResponse(
            x.Id,
            x.WarehouseId,
            x.Warehouse.Name,
            x.ProductId,
            x.Product.Barcode,
            x.Product.Name,
            x.Stock,
            x.UpdatedAt
        )).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        return new PagedWarehouseStockResponse(page, pageSize, total, totalPages, responses);
    }
}
