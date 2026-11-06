using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface IWarehouseService
{
    Task<PagedWarehouseResponse> GetWarehousesAsync(string? q, int page, int pageSize, bool includeInactive = false);
    Task<WarehouseResponse?> GetWarehouseByIdAsync(int id);
    Task<WarehouseResponse> CreateWarehouseAsync(int userId, WarehouseCreateRequest req);
    Task<WarehouseResponse> UpdateWarehouseAsync(int userId, int id, WarehouseUpdateRequest req);
    Task<bool> DeleteWarehouseAsync(int userId, int id);
}

public class WarehouseService : IWarehouseService
{
    private readonly AppDbContext _db;

    public WarehouseService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedWarehouseResponse> GetWarehousesAsync(string? q, int page, int pageSize, bool includeInactive = false)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.Warehouses.AsNoTracking().AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(x => x.Name.Contains(q) || x.Code.Contains(q));
        }

        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var responses = items.Select(MapToResponse).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        return new PagedWarehouseResponse(page, pageSize, total, totalPages, responses);
    }

    public async Task<WarehouseResponse?> GetWarehouseByIdAsync(int id)
    {
        var wh = await _db.Warehouses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        return wh is null ? null : MapToResponse(wh);
    }

    public async Task<WarehouseResponse> CreateWarehouseAsync(int userId, WarehouseCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            throw new BusinessRuleException("Mã kho không được để trống");
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new BusinessRuleException("Tên kho không được để trống");

        var code = req.Code.Trim().ToUpper();
        var exists = await _db.Warehouses.AnyAsync(x => x.Code.ToUpper() == code);
        if (exists)
            throw new BusinessRuleException("Mã kho đã tồn tại");

        if (req.IsDefault)
        {
            var defaults = await _db.Warehouses.Where(x => x.IsDefault).ToListAsync();
            foreach (var d in defaults)
            {
                d.IsDefault = false;
            }
        }

        var wh = new Warehouse
        {
            Code = code,
            Name = req.Name.Trim(),
            Address = req.Address?.Trim(),
            IsDefault = req.IsDefault,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.Warehouses.Add(wh);
        PosLogService.Write(_db, "WAREHOUSE_CREATED", userId, "warehouse", wh.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(wh);
    }

    public async Task<WarehouseResponse> UpdateWarehouseAsync(int userId, int id, WarehouseUpdateRequest req)
    {
        var wh = await _db.Warehouses.FirstOrDefaultAsync(x => x.Id == id);
        if (wh is null)
            throw new KeyNotFoundException("Không tìm thấy kho hàng");

        if (string.IsNullOrWhiteSpace(req.Name))
            throw new BusinessRuleException("Tên kho không được để trống");

        if (req.IsDefault && !wh.IsDefault)
        {
            var defaults = await _db.Warehouses.Where(x => x.IsDefault && x.Id != id).ToListAsync();
            foreach (var d in defaults)
            {
                d.IsDefault = false;
            }
        }

        wh.Name = req.Name.Trim();
        wh.Address = req.Address?.Trim();
        wh.IsDefault = req.IsDefault;
        wh.IsActive = req.IsActive;
        wh.UpdatedAt = DateTime.UtcNow;

        PosLogService.Write(_db, "WAREHOUSE_UPDATED", userId, "warehouse", wh.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(wh);
    }

    public async Task<bool> DeleteWarehouseAsync(int userId, int id)
    {
        var wh = await _db.Warehouses.Include(x => x.WarehouseStocks).FirstOrDefaultAsync(x => x.Id == id);
        if (wh is null)
            return false;

        if (wh.IsDefault)
            throw new BusinessRuleException("Không thể xóa kho mặc định");

        if (wh.WarehouseStocks.Any(s => s.Stock > 0))
            throw new BusinessRuleException("Không thể xóa kho còn tồn kho sản phẩm");

        _db.Warehouses.Remove(wh);
        PosLogService.Write(_db, "WAREHOUSE_DELETED", userId, "warehouse", wh.Code);
        await _db.SaveChangesAsync();
        return true;
    }

    private static WarehouseResponse MapToResponse(Warehouse w) => new(
        w.Id, w.Code, w.Name, w.Address, w.IsDefault, w.IsActive, w.CreatedAt, w.UpdatedAt
    );
}
