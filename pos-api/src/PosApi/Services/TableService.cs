using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface ITableService
{
    Task<PagedTableResponse> GetTablesAsync(int? areaId, TableStatus? status, int page, int pageSize, bool includeInactive = false);
    Task<TableResponse?> GetTableByIdAsync(int id);
    Task<TableResponse> CreateTableAsync(int userId, TableCreateRequest req);
    Task<TableResponse> UpdateTableAsync(int userId, int id, TableUpdateRequest req);
    Task<TableResponse> UpdateTableStatusAsync(int userId, int id, TableStatus status);
    Task<bool> TransferTableAsync(int userId, int sourceTableId, int targetTableId);
    Task<bool> DeleteTableAsync(int userId, int id);
}

public class TableService : ITableService
{
    private readonly AppDbContext _db;

    public TableService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedTableResponse> GetTablesAsync(int? areaId, TableStatus? status, int page, int pageSize, bool includeInactive = false)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.Tables.AsNoTracking().Include(x => x.Area).Include(x => x.CurrentOrder).AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (areaId.HasValue)
        {
            query = query.Where(x => x.AreaId == areaId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.Area.SortOrder).ThenBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var responses = items.Select(MapToResponse).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        return new PagedTableResponse(page, pageSize, total, totalPages, responses);
    }

    public async Task<TableResponse?> GetTableByIdAsync(int id)
    {
        var table = await _db.Tables.AsNoTracking().Include(x => x.Area).Include(x => x.CurrentOrder).FirstOrDefaultAsync(x => x.Id == id);
        return table is null ? null : MapToResponse(table);
    }

    public async Task<TableResponse> CreateTableAsync(int userId, TableCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            throw new BusinessRuleException("Mã bàn không được để trống");
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new BusinessRuleException("Tên bàn không được để trống");
        if (req.Capacity < 1)
            throw new BusinessRuleException("Sức chứa bàn phải lớn hơn hoặc bằng 1");

        var area = await _db.Areas.FirstOrDefaultAsync(x => x.Id == req.AreaId && x.IsActive);
        if (area is null)
            throw new BusinessRuleException("Không tìm thấy khu vực hợp lệ");

        var code = req.Code.Trim();
        var exists = await _db.Tables.AnyAsync(x => x.Code.ToLower() == code.ToLower());
        if (exists)
            throw new BusinessRuleException("Mã bàn đã tồn tại");

        var table = new Table
        {
            Code = code,
            Name = req.Name.Trim(),
            AreaId = req.AreaId,
            Capacity = req.Capacity,
            Status = TableStatus.Available,
            IsActive = true
        };

        _db.Tables.Add(table);
        PosLogService.Write(_db, "TABLE_CREATED", userId, "table", table.Code);
        await _db.SaveChangesAsync();

        await _db.Entry(table).Reference(x => x.Area).LoadAsync();
        return MapToResponse(table);
    }

    public async Task<TableResponse> UpdateTableAsync(int userId, int id, TableUpdateRequest req)
    {
        var table = await _db.Tables.Include(x => x.Area).Include(x => x.CurrentOrder).FirstOrDefaultAsync(x => x.Id == id);
        if (table is null)
            throw new KeyNotFoundException("Không tìm thấy bàn");

        if (string.IsNullOrWhiteSpace(req.Name))
            throw new BusinessRuleException("Tên bàn không được để trống");
        if (req.Capacity < 1)
            throw new BusinessRuleException("Sức chứa bàn phải lớn hơn hoặc bằng 1");

        if (table.AreaId != req.AreaId)
        {
            var area = await _db.Areas.FirstOrDefaultAsync(x => x.Id == req.AreaId && x.IsActive);
            if (area is null)
                throw new BusinessRuleException("Không tìm thấy khu vực mới hợp lệ");
            table.AreaId = req.AreaId;
        }

        table.Name = req.Name.Trim();
        table.Capacity = req.Capacity;
        table.IsActive = req.IsActive;

        PosLogService.Write(_db, "TABLE_UPDATED", userId, "table", table.Code);
        await _db.SaveChangesAsync();

        await _db.Entry(table).Reference(x => x.Area).LoadAsync();
        return MapToResponse(table);
    }

    public async Task<TableResponse> UpdateTableStatusAsync(int userId, int id, TableStatus status)
    {
        var table = await _db.Tables.Include(x => x.Area).Include(x => x.CurrentOrder).FirstOrDefaultAsync(x => x.Id == id);
        if (table is null)
            throw new KeyNotFoundException("Không tìm thấy bàn");

        table.Status = status;
        if (status == TableStatus.Available)
        {
            table.CurrentOrderId = null;
            table.CurrentOrder = null;
        }

        PosLogService.Write(_db, "TABLE_STATUS_UPDATED", userId, "table", table.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(table);
    }

    public async Task<bool> TransferTableAsync(int userId, int sourceTableId, int targetTableId)
    {
        if (sourceTableId == targetTableId)
            throw new BusinessRuleException("Không thể chuyển cùng một bàn");

        var source = await _db.Tables.Include(x => x.CurrentOrder).FirstOrDefaultAsync(x => x.Id == sourceTableId);
        var target = await _db.Tables.FirstOrDefaultAsync(x => x.Id == targetTableId);

        if (source is null || target is null)
            throw new KeyNotFoundException("Không tìm thấy bàn nguồn hoặc bàn đích");

        if (target.Status != TableStatus.Available)
            throw new BusinessRuleException("Bàn đích đang bận hoặc bị khóa");

        if (!source.CurrentOrderId.HasValue)
            throw new BusinessRuleException("Bàn nguồn không có đơn hàng đang phục vụ");

        target.CurrentOrderId = source.CurrentOrderId;
        target.Status = TableStatus.Occupied;

        source.CurrentOrderId = null;
        source.Status = TableStatus.Available;

        PosLogService.Write(_db, "TABLE_TRANSFERRED", userId, "table", $"{source.Code} -> {target.Code}");
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteTableAsync(int userId, int id)
    {
        var table = await _db.Tables.FirstOrDefaultAsync(x => x.Id == id);
        if (table is null)
            return false;

        if (table.Status == TableStatus.Occupied)
            throw new BusinessRuleException("Không thể xóa bàn đang có khách phục vụ");

        _db.Tables.Remove(table);
        PosLogService.Write(_db, "TABLE_DELETED", userId, "table", table.Code);
        await _db.SaveChangesAsync();
        return true;
    }

    private static TableResponse MapToResponse(Table t) => new(
        t.Id, t.Code, t.Name, t.AreaId, t.Area?.Name ?? string.Empty, t.Capacity, t.Status,
        t.CurrentOrderId, t.CurrentOrder?.Code, t.IsActive
    );
}
