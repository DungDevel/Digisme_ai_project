using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface IAreaService
{
    Task<PagedAreaResponse> GetAreasAsync(string? q, int page, int pageSize, bool includeInactive = false);
    Task<AreaResponse?> GetAreaByIdAsync(int id);
    Task<AreaResponse> CreateAreaAsync(int userId, AreaCreateRequest req);
    Task<AreaResponse> UpdateAreaAsync(int userId, int id, AreaUpdateRequest req);
    Task<bool> DeleteAreaAsync(int userId, int id);
}

public class AreaService : IAreaService
{
    private readonly AppDbContext _db;

    public AreaService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedAreaResponse> GetAreasAsync(string? q, int page, int pageSize, bool includeInactive = false)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.Areas.AsNoTracking().Include(x => x.Tables).AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(x => x.Name.Contains(q) || x.Code.Contains(q));
        }

        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var responses = items.Select(MapToResponse).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        return new PagedAreaResponse(page, pageSize, total, totalPages, responses);
    }

    public async Task<AreaResponse?> GetAreaByIdAsync(int id)
    {
        var area = await _db.Areas.AsNoTracking().Include(x => x.Tables).FirstOrDefaultAsync(x => x.Id == id);
        return area is null ? null : MapToResponse(area);
    }

    public async Task<AreaResponse> CreateAreaAsync(int userId, AreaCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            throw new BusinessRuleException("Mã khu vực không được để trống");
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new BusinessRuleException("Tên khu vực không được để trống");

        var code = req.Code.Trim();
        var exists = await _db.Areas.AnyAsync(x => x.Code.ToLower() == code.ToLower());
        if (exists)
            throw new BusinessRuleException("Mã khu vực đã tồn tại");

        var area = new Area
        {
            Code = code,
            Name = req.Name.Trim(),
            Description = req.Description?.Trim(),
            SortOrder = req.SortOrder,
            IsActive = true
        };

        _db.Areas.Add(area);
        PosLogService.Write(_db, "AREA_CREATED", userId, "area", area.Code);
        await _db.SaveChangesAsync();

        await _db.Entry(area).Collection(x => x.Tables).LoadAsync();
        return MapToResponse(area);
    }

    public async Task<AreaResponse> UpdateAreaAsync(int userId, int id, AreaUpdateRequest req)
    {
        var area = await _db.Areas.Include(x => x.Tables).FirstOrDefaultAsync(x => x.Id == id);
        if (area is null)
            throw new KeyNotFoundException("Không tìm thấy khu vực");

        if (string.IsNullOrWhiteSpace(req.Name))
            throw new BusinessRuleException("Tên khu vực không được để trống");

        area.Name = req.Name.Trim();
        area.Description = req.Description?.Trim();
        area.SortOrder = req.SortOrder;
        area.IsActive = req.IsActive;

        PosLogService.Write(_db, "AREA_UPDATED", userId, "area", area.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(area);
    }

    public async Task<bool> DeleteAreaAsync(int userId, int id)
    {
        var area = await _db.Areas.Include(x => x.Tables).FirstOrDefaultAsync(x => x.Id == id);
        if (area is null)
            return false;

        if (area.Tables.Any())
            throw new BusinessRuleException("Không thể xóa khu vực đang chứa bàn ăn. Vui lòng chuyển hoặc xóa bàn trước.");

        _db.Areas.Remove(area);
        PosLogService.Write(_db, "AREA_DELETED", userId, "area", area.Code);
        await _db.SaveChangesAsync();
        return true;
    }

    private static AreaResponse MapToResponse(Area a) => new(
        a.Id, a.Code, a.Name, a.Description, a.SortOrder, a.IsActive, a.Tables?.Count ?? 0, a.CreatedAt, a.UpdatedAt
    );
}
