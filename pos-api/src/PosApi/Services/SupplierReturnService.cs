using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface ISupplierReturnService
{
    Task<PagedSupplierReturnResponse> GetSupplierReturnsAsync(int? supplierId, int? warehouseId, SupplierReturnStatus? status, int page, int pageSize);
    Task<SupplierReturnResponse?> GetSupplierReturnByIdAsync(int id);
    Task<SupplierReturnResponse> CreateSupplierReturnAsync(int userId, SupplierReturnCreateRequest req);
    Task<SupplierReturnResponse> ConfirmSupplierReturnAsync(int userId, int id);
    Task<SupplierReturnResponse> CancelSupplierReturnAsync(int userId, int id);
}

public class SupplierReturnService : ISupplierReturnService
{
    private readonly AppDbContext _db;

    public SupplierReturnService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedSupplierReturnResponse> GetSupplierReturnsAsync(int? supplierId, int? warehouseId, SupplierReturnStatus? status, int page, int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.SupplierReturns
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Warehouse)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .AsQueryable();

        if (supplierId.HasValue)
        {
            query = query.Where(x => x.SupplierId == supplierId.Value);
        }

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

        return new PagedSupplierReturnResponse(page, pageSize, total, totalPages, responses);
    }

    public async Task<SupplierReturnResponse?> GetSupplierReturnByIdAsync(int id)
    {
        var sr = await _db.SupplierReturns
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Include(x => x.Warehouse)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(x => x.Id == id);

        return sr is null ? null : MapToResponse(sr);
    }

    public async Task<SupplierReturnResponse> CreateSupplierReturnAsync(int userId, SupplierReturnCreateRequest req)
    {
        if (req.Items == null || req.Items.Count == 0)
            throw new BusinessRuleException("Danh sách sản phẩm trả nhà cung cấp không được trống");

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == req.SupplierId && x.IsActive);
        if (supplier is null)
            throw new KeyNotFoundException("Không tìm thấy nhà cung cấp");

        var wh = await _db.Warehouses.FirstOrDefaultAsync(x => x.Id == req.WarehouseId && x.IsActive);
        if (wh is null)
            throw new KeyNotFoundException("Không tìm thấy kho hàng");

        var productIds = req.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();

        decimal total = 0m;
        var returnItems = new List<SupplierReturnItem>();

        foreach (var itemReq in req.Items)
        {
            var product = products.FirstOrDefault(p => p.Id == itemReq.ProductId);
            if (product is null)
                throw new KeyNotFoundException($"Không tìm thấy sản phẩm ID {itemReq.ProductId}");
            if (itemReq.Qty <= 0)
                throw new BusinessRuleException($"Số lượng trả của sản phẩm '{product.Name}' phải lớn hơn 0");
            if (itemReq.UnitCost < 0)
                throw new BusinessRuleException($"Đơn giá trả của sản phẩm '{product.Name}' không được âm");

            var lineTotal = itemReq.Qty * itemReq.UnitCost;
            total += lineTotal;

            returnItems.Add(new SupplierReturnItem
            {
                ProductId = product.Id,
                Qty = itemReq.Qty,
                UnitCost = itemReq.UnitCost,
                Total = lineTotal
            });
        }

        var code = $"SR-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper()}";

        var supplierReturn = new SupplierReturn
        {
            Code = code,
            SupplierId = req.SupplierId,
            WarehouseId = req.WarehouseId,
            Total = total,
            Status = SupplierReturnStatus.Draft,
            Note = req.Note?.Trim(),
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            Items = returnItems
        };

        _db.SupplierReturns.Add(supplierReturn);
        PosLogService.Write(_db, "SUPPLIER_RETURN_CREATED", userId, "supplier_return", code);
        await _db.SaveChangesAsync();

        await _db.Entry(supplierReturn).Reference(x => x.Supplier).LoadAsync();
        await _db.Entry(supplierReturn).Reference(x => x.Warehouse).LoadAsync();
        foreach (var item in supplierReturn.Items)
        {
            await _db.Entry(item).Reference(x => x.Product).LoadAsync();
        }

        return MapToResponse(supplierReturn);
    }

    public async Task<SupplierReturnResponse> ConfirmSupplierReturnAsync(int userId, int id)
    {
        var sr = await _db.SupplierReturns
            .Include(x => x.Supplier)
            .Include(x => x.Warehouse)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (sr is null)
            throw new KeyNotFoundException("Không tìm thấy phiếu trả nhà cung cấp");

        if (sr.Status != SupplierReturnStatus.Draft)
            throw new BusinessRuleException("Phiếu trả nhà cung cấp không ở trạng thái nháp để có thể duyệt");

        foreach (var item in sr.Items)
        {
            var stockRecord = await _db.WarehouseStocks
                .FirstOrDefaultAsync(s => s.WarehouseId == sr.WarehouseId && s.ProductId == item.ProductId);

            if (stockRecord is null || stockRecord.Stock < item.Qty)
            {
                throw new BusinessRuleException($"Tồn kho tại kho không đủ để trả sản phẩm '{item.Product.Name}' (Tồn: {stockRecord?.Stock ?? 0}, Yêu cầu: {item.Qty})");
            }

            stockRecord.Stock -= item.Qty;
            stockRecord.UpdatedAt = DateTime.UtcNow;

            _db.StockLedger.Add(new StockLedgerEntry
            {
                ProductId = item.ProductId,
                WarehouseId = sr.WarehouseId,
                Qty = -item.Qty,
                Balance = stockRecord.Stock,
                RefType = LedgerRefType.Return,
                RefId = sr.Code,
                Note = $"Trả hàng nhà cung cấp {sr.Supplier.Name} ({sr.Code})"
            });
        }

        sr.Status = SupplierReturnStatus.Completed;
        PosLogService.Write(_db, "SUPPLIER_RETURN_CONFIRMED", userId, "supplier_return", sr.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(sr);
    }

    public async Task<SupplierReturnResponse> CancelSupplierReturnAsync(int userId, int id)
    {
        var sr = await _db.SupplierReturns
            .Include(x => x.Supplier)
            .Include(x => x.Warehouse)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (sr is null)
            throw new KeyNotFoundException("Không tìm thấy phiếu trả nhà cung cấp");

        if (sr.Status != SupplierReturnStatus.Draft)
            throw new BusinessRuleException("Chỉ có thể hủy phiếu trả nhà cung cấp ở trạng thái nháp");

        sr.Status = SupplierReturnStatus.Cancelled;
        PosLogService.Write(_db, "SUPPLIER_RETURN_CANCELLED", userId, "supplier_return", sr.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(sr);
    }

    private static SupplierReturnResponse MapToResponse(SupplierReturn r) => new(
        r.Id,
        r.Code,
        r.SupplierId,
        r.Supplier?.Name ?? string.Empty,
        r.WarehouseId,
        r.Warehouse?.Name ?? string.Empty,
        r.Status,
        r.Total,
        r.Note,
        r.CreatedBy,
        r.CreatedAt,
        r.Items?.Select(i => new SupplierReturnItemResponse(
            i.Id,
            i.ProductId,
            i.Product?.Barcode ?? string.Empty,
            i.Product?.Name ?? string.Empty,
            i.Qty,
            i.UnitCost,
            i.Total
        )).ToList() ?? new()
    );
}
