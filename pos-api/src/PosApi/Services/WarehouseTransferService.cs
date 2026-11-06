using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface IWarehouseTransferService
{
    Task<PagedWarehouseTransferResponse> GetTransfersAsync(int? warehouseId, TransferStatus? status, int page, int pageSize);
    Task<WarehouseTransferResponse?> GetTransferByIdAsync(int id);
    Task<WarehouseTransferResponse> CreateTransferAsync(int userId, WarehouseTransferCreateRequest req);
    Task<WarehouseTransferResponse> ConfirmTransferAsync(int userId, int id);
    Task<WarehouseTransferResponse> CancelTransferAsync(int userId, int id);
}

public class WarehouseTransferService : IWarehouseTransferService
{
    private readonly AppDbContext _db;

    public WarehouseTransferService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedWarehouseTransferResponse> GetTransfersAsync(int? warehouseId, TransferStatus? status, int page, int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.WarehouseTransfers
            .AsNoTracking()
            .Include(x => x.FromWarehouse)
            .Include(x => x.ToWarehouse)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .AsQueryable();

        if (warehouseId.HasValue)
        {
            query = query.Where(x => x.FromWarehouseId == warehouseId.Value || x.ToWarehouseId == warehouseId.Value);
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

        return new PagedWarehouseTransferResponse(page, pageSize, total, totalPages, responses);
    }

    public async Task<WarehouseTransferResponse?> GetTransferByIdAsync(int id)
    {
        var tr = await _db.WarehouseTransfers
            .AsNoTracking()
            .Include(x => x.FromWarehouse)
            .Include(x => x.ToWarehouse)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(x => x.Id == id);

        return tr is null ? null : MapToResponse(tr);
    }

    public async Task<WarehouseTransferResponse> CreateTransferAsync(int userId, WarehouseTransferCreateRequest req)
    {
        if (req.FromWarehouseId == req.ToWarehouseId)
            throw new BusinessRuleException("Kho đi và kho đến không được trùng nhau");

        if (req.Items == null || req.Items.Count == 0)
            throw new BusinessRuleException("Danh sách sản phẩm chuyển kho không được trống");

        var fromWh = await _db.Warehouses.FirstOrDefaultAsync(x => x.Id == req.FromWarehouseId && x.IsActive);
        if (fromWh is null)
            throw new KeyNotFoundException("Không tìm thấy kho đi");

        var toWh = await _db.Warehouses.FirstOrDefaultAsync(x => x.Id == req.ToWarehouseId && x.IsActive);
        if (toWh is null)
            throw new KeyNotFoundException("Không tìm thấy kho đến");

        var productIds = req.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();

        var transferItems = new List<WarehouseTransferItem>();
        foreach (var itemReq in req.Items)
        {
            var product = products.FirstOrDefault(p => p.Id == itemReq.ProductId);
            if (product is null)
                throw new KeyNotFoundException($"Không tìm thấy sản phẩm ID {itemReq.ProductId}");
            if (itemReq.Quantity <= 0)
                throw new BusinessRuleException($"Số lượng chuyển của sản phẩm '{product.Name}' phải lớn hơn 0");

            transferItems.Add(new WarehouseTransferItem
            {
                ProductId = product.Id,
                Qty = itemReq.Quantity,
                UnitCost = itemReq.UnitCost
            });
        }

        var code = $"WHT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper()}";

        var transfer = new WarehouseTransfer
        {
            Code = code,
            FromWarehouseId = req.FromWarehouseId,
            ToWarehouseId = req.ToWarehouseId,
            Status = TransferStatus.Draft,
            Note = req.Note?.Trim(),
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            Items = transferItems
        };

        _db.WarehouseTransfers.Add(transfer);
        PosLogService.Write(_db, "WAREHOUSE_TRANSFER_CREATED", userId, "warehouse_transfer", code);
        await _db.SaveChangesAsync();

        await _db.Entry(transfer).Reference(x => x.FromWarehouse).LoadAsync();
        await _db.Entry(transfer).Reference(x => x.ToWarehouse).LoadAsync();
        foreach (var item in transfer.Items)
        {
            await _db.Entry(item).Reference(x => x.Product).LoadAsync();
        }

        return MapToResponse(transfer);
    }

    public async Task<WarehouseTransferResponse> ConfirmTransferAsync(int userId, int id)
    {
        var transfer = await _db.WarehouseTransfers
            .Include(x => x.FromWarehouse)
            .Include(x => x.ToWarehouse)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (transfer is null)
            throw new KeyNotFoundException("Không tìm thấy phiếu chuyển kho");

        if (transfer.Status != TransferStatus.Draft)
            throw new BusinessRuleException("Phiếu chuyển kho không ở trạng thái nháp để có thể duyệt");

        foreach (var item in transfer.Items)
        {
            var fromStock = await _db.WarehouseStocks
                .FirstOrDefaultAsync(s => s.WarehouseId == transfer.FromWarehouseId && s.ProductId == item.ProductId);

            if (fromStock is null || fromStock.Stock < item.Qty)
            {
                throw new BusinessRuleException($"Tồn kho tại kho đi không đủ cho sản phẩm '{item.Product.Name}' (Tồn: {fromStock?.Stock ?? 0}, Yêu cầu: {item.Qty})");
            }

            fromStock.Stock -= item.Qty;
            fromStock.UpdatedAt = DateTime.UtcNow;

            var toStock = await _db.WarehouseStocks
                .FirstOrDefaultAsync(s => s.WarehouseId == transfer.ToWarehouseId && s.ProductId == item.ProductId);

            if (toStock is null)
            {
                toStock = new WarehouseStock
                {
                    WarehouseId = transfer.ToWarehouseId,
                    ProductId = item.ProductId,
                    Stock = item.Qty,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.WarehouseStocks.Add(toStock);
            }
            else
            {
                toStock.Stock += item.Qty;
                toStock.UpdatedAt = DateTime.UtcNow;
            }

            _db.StockLedger.Add(new StockLedgerEntry
            {
                ProductId = item.ProductId,
                WarehouseId = transfer.FromWarehouseId,
                Qty = -item.Qty,
                Balance = fromStock.Stock,
                RefType = LedgerRefType.Adjustment,
                RefId = transfer.Code,
                Note = $"Chuyển kho đi {transfer.Code} sang kho {transfer.ToWarehouse.Name}"
            });

            _db.StockLedger.Add(new StockLedgerEntry
            {
                ProductId = item.ProductId,
                WarehouseId = transfer.ToWarehouseId,
                Qty = item.Qty,
                Balance = toStock.Stock,
                RefType = LedgerRefType.Adjustment,
                RefId = transfer.Code,
                Note = $"Nhận chuyển kho {transfer.Code} từ kho {transfer.FromWarehouse.Name}"
            });
        }

        transfer.Status = TransferStatus.Completed;
        PosLogService.Write(_db, "WAREHOUSE_TRANSFER_CONFIRMED", userId, "warehouse_transfer", transfer.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(transfer);
    }

    public async Task<WarehouseTransferResponse> CancelTransferAsync(int userId, int id)
    {
        var transfer = await _db.WarehouseTransfers
            .Include(x => x.FromWarehouse)
            .Include(x => x.ToWarehouse)
            .Include(x => x.Items).ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (transfer is null)
            throw new KeyNotFoundException("Không tìm thấy phiếu chuyển kho");

        if (transfer.Status != TransferStatus.Draft)
            throw new BusinessRuleException("Chỉ có thể hủy phiếu chuyển kho ở trạng thái nháp");

        transfer.Status = TransferStatus.Cancelled;
        PosLogService.Write(_db, "WAREHOUSE_TRANSFER_CANCELLED", userId, "warehouse_transfer", transfer.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(transfer);
    }

    private static WarehouseTransferResponse MapToResponse(WarehouseTransfer t) => new(
        t.Id,
        t.Code,
        t.FromWarehouseId,
        t.FromWarehouse?.Name ?? string.Empty,
        t.ToWarehouseId,
        t.ToWarehouse?.Name ?? string.Empty,
        t.Status,
        t.Note,
        t.CreatedBy,
        t.CreatedAt,
        t.Items?.Select(i => new WarehouseTransferItemResponse(
            i.Id,
            i.ProductId,
            i.Product?.Barcode ?? string.Empty,
            i.Product?.Name ?? string.Empty,
            i.Qty,
            i.UnitCost
        )).ToList() ?? new()
    );
}
