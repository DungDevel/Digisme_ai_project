using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface IStockTakeService
{
    Task<PagedStockTakeResponse> GetStockTakesAsync(int? warehouseId, StockTakeStatus? status, int page, int pageSize);
    Task<StockTakeResponse?> GetStockTakeByIdAsync(int id);
    Task<StockTakeResponse> CreateStockTakeAsync(int userId, StockTakeCreateRequest req);
    Task<StockTakeResponse> ConfirmStockTakeAsync(int userId, int id);
    Task<StockTakeResponse> CancelStockTakeAsync(int userId, int id);
}

public class StockTakeService : IStockTakeService
{
    private readonly AppDbContext _db;

    public StockTakeService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedStockTakeResponse> GetStockTakesAsync(int? warehouseId, StockTakeStatus? status, int page, int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.StockTakes
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(x => x.WarehouseId == warehouseId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var responses = items.Select(MapToResponse).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        return new PagedStockTakeResponse(page, pageSize, total, totalPages, responses);
    }

    public async Task<StockTakeResponse?> GetStockTakeByIdAsync(int id)
    {
        var st = await _db.StockTakes
            .AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(x => x.Id == id);

        return st is null ? null : MapToResponse(st);
    }

    public async Task<StockTakeResponse> CreateStockTakeAsync(int userId, StockTakeCreateRequest req)
    {
        if (req.Items == null || req.Items.Count == 0)
            throw new BusinessRuleException("Danh sách sản phẩm kiểm kê không được trống");

        var wh = await _db.Warehouses.FirstOrDefaultAsync(x => x.Id == req.WarehouseId && x.IsActive);
        if (wh is null)
            throw new KeyNotFoundException("Không tìm thấy kho hàng");

        var productIds = req.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();

        var stockTakeItems = new List<StockTakeItem>();
        foreach (var itemReq in req.Items)
        {
            var product = products.FirstOrDefault(p => p.Id == itemReq.ProductId);
            if (product is null)
                throw new KeyNotFoundException($"Không tìm thấy sản phẩm ID {itemReq.ProductId}");
            if (itemReq.ActualQty < 0)
                throw new BusinessRuleException($"Số lượng thực tế của sản phẩm '{product.Name}' không được âm");

            var delta = itemReq.ActualQty - itemReq.SystemQty;

            stockTakeItems.Add(new StockTakeItem
            {
                ProductId = product.Id,
                SystemQty = itemReq.SystemQty,
                ActualQty = itemReq.ActualQty,
                DeltaQty = delta
            });
        }

        var code = $"ST-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper()}";

        var stockTake = new StockTake
        {
            Code = code,
            WarehouseId = req.WarehouseId,
            Status = StockTakeStatus.Draft,
            Note = req.Note?.Trim(),
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            Items = stockTakeItems
        };

        _db.StockTakes.Add(stockTake);
        PosLogService.Write(_db, "STOCK_TAKE_CREATED", userId, "stock_take", code);
        await _db.SaveChangesAsync();

        await _db.Entry(stockTake).Reference(x => x.Warehouse).LoadAsync();
        foreach (var item in stockTake.Items)
        {
            await _db.Entry(item).Reference(x => x.Product).LoadAsync();
        }

        return MapToResponse(stockTake);
    }

    public async Task<StockTakeResponse> ConfirmStockTakeAsync(int userId, int id)
    {
        var stockTake = await _db.StockTakes
            .Include(x => x.Warehouse)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (stockTake is null)
            throw new KeyNotFoundException("Không tìm thấy phiếu kiểm kê");

        if (stockTake.Status != StockTakeStatus.Draft)
            throw new BusinessRuleException("Phiếu kiểm kê không ở trạng thái nháp để có thể duyệt");

        foreach (var item in stockTake.Items)
        {
            var stockRecord = await _db.WarehouseStocks
                .FirstOrDefaultAsync(s => s.WarehouseId == stockTake.WarehouseId && s.ProductId == item.ProductId);

            if (stockRecord is null)
            {
                stockRecord = new WarehouseStock
                {
                    WarehouseId = stockTake.WarehouseId,
                    ProductId = item.ProductId,
                    Stock = 0m,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.WarehouseStocks.Add(stockRecord);
            }

            stockRecord.Stock += item.DeltaQty;
            stockRecord.UpdatedAt = DateTime.UtcNow;

            _db.StockLedger.Add(new StockLedgerEntry
            {
                ProductId = item.ProductId,
                WarehouseId = stockTake.WarehouseId,
                Qty = item.DeltaQty,
                Balance = stockRecord.Stock,
                RefType = LedgerRefType.Adjustment,
                RefId = stockTake.Code,
                Note = $"Kiểm kê điều chỉnh kho {stockTake.Code}"
            });
        }

        stockTake.Status = StockTakeStatus.Completed;
        PosLogService.Write(_db, "STOCK_TAKE_CONFIRMED", userId, "stock_take", stockTake.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(stockTake);
    }

    public async Task<StockTakeResponse> CancelStockTakeAsync(int userId, int id)
    {
        var stockTake = await _db.StockTakes
            .Include(x => x.Warehouse)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (stockTake is null)
            throw new KeyNotFoundException("Không tìm thấy phiếu kiểm kê");

        if (stockTake.Status != StockTakeStatus.Draft)
            throw new BusinessRuleException("Chỉ có thể hủy phiếu kiểm kê ở trạng thái nháp");

        stockTake.Status = StockTakeStatus.Cancelled;
        PosLogService.Write(_db, "STOCK_TAKE_CANCELLED", userId, "stock_take", stockTake.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(stockTake);
    }

    private static StockTakeResponse MapToResponse(StockTake s) => new(
        s.Id,
        s.Code,
        s.WarehouseId,
        s.Warehouse?.Name ?? string.Empty,
        s.Status,
        s.Note,
        s.CreatedBy,
        s.CreatedAt,
        s.Items?.Select(i => new StockTakeItemResponse(
            i.Id,
            i.ProductId,
            i.Product?.Barcode ?? string.Empty,
            i.Product?.Name ?? string.Empty,
            i.SystemQty,
            i.ActualQty,
            i.DeltaQty
        )).ToList() ?? new()
    );
}
