using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public class CustomerGroupService : ICustomerGroupService
{
    private readonly AppDbContext _db;

    public CustomerGroupService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedCustomerGroupResponse> GetCustomerGroupsAsync(string? q, bool? isSupplier, int page, int pageSize, bool includeInactive = false)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.CustomerGroups.AsNoTracking().Include(x => x.Parent).AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (isSupplier.HasValue)
        {
            query = query.Where(x => x.IsSupplier == isSupplier.Value);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(x => x.Name.Contains(q) || x.Code.Contains(q));
        }

        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var responses = items.Select(MapToResponse).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        return new PagedCustomerGroupResponse(page, pageSize, total, totalPages, responses);
    }

    public async Task<CustomerGroupResponse?> GetCustomerGroupByIdAsync(int id)
    {
        var group = await _db.CustomerGroups.AsNoTracking().Include(x => x.Parent).FirstOrDefaultAsync(x => x.Id == id);
        return group is null ? null : MapToResponse(group);
    }

    public async Task<CustomerGroupResponse> CreateCustomerGroupAsync(int userId, CustomerGroupCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            throw new BusinessRuleException("Mã nhóm bắt buộc");
        if (req.Code.Length > 50)
            throw new BusinessRuleException("Mã nhóm tối đa 50 ký tự");

        if (string.IsNullOrWhiteSpace(req.Name))
            throw new BusinessRuleException("Tên nhóm bắt buộc");
        if (req.Name.Length > 200)
            throw new BusinessRuleException("Tên nhóm tối đa 200 ký tự");

        if (req.DefaultDiscountRate < 0m || req.DefaultDiscountRate > 100m)
            throw new BusinessRuleException("Tỷ lệ chiết khấu mặc định phải từ 0 đến 100");

        var trimmedCode = req.Code.Trim();
        var existsCode = await _db.CustomerGroups.AnyAsync(x => x.Code.ToLower() == trimmedCode.ToLower());
        if (existsCode)
            throw new BusinessRuleException("Mã nhóm đã tồn tại");

        var trimmedName = req.Name.Trim();

        int? parentId = null;
        if (req.ParentId.HasValue)
        {
            var parent = await _db.CustomerGroups.FirstOrDefaultAsync(x => x.Id == req.ParentId.Value && x.IsActive);
            if (parent is null)
                throw new BusinessRuleException("Không tìm thấy nhóm cha");
            if (parent.IsSupplier != req.IsSupplier)
                throw new BusinessRuleException("Nhóm cha phải cùng loại (khách hàng hoặc nhà cung cấp)");

            int parentDepth = await GetGroupDepthAsync(parent.Id);
            if (parentDepth >= 5)
                throw new BusinessRuleException("Phân cấp nhóm vượt quá giới hạn độ sâu cho phép (tối đa 5 tầng)");

            parentId = parent.Id;
        }

        var group = new CustomerGroup
        {
            Code = trimmedCode,
            Name = trimmedName,
            IsSupplier = req.IsSupplier,
            DefaultDiscountRate = req.DefaultDiscountRate,
            ParentId = parentId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            _db.CustomerGroups.Add(group);
            PosLogService.Write(_db, "CUSTOMER_GROUP_CREATED", userId, "customer_group", group.Code);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new BusinessRuleException("Mã nhóm đã tồn tại");
        }

        await _db.Entry(group).Reference(x => x.Parent).LoadAsync();
        return MapToResponse(group);
    }

    public async Task<CustomerGroupResponse> UpdateCustomerGroupAsync(int userId, int id, CustomerGroupUpdateRequest req)
    {
        var group = await _db.CustomerGroups
            .Include(x => x.Children)
            .Include(x => x.Customers)
            .Include(x => x.Suppliers)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (group is null)
            throw new BusinessRuleException("Không tìm thấy nhóm");

        if (string.IsNullOrWhiteSpace(req.Name))
            throw new BusinessRuleException("Tên nhóm bắt buộc");
        if (req.Name.Length > 200)
            throw new BusinessRuleException("Tên nhóm tối đa 200 ký tự");

        if (req.DefaultDiscountRate < 0m || req.DefaultDiscountRate > 100m)
            throw new BusinessRuleException("Tỷ lệ chiết khấu mặc định phải từ 0 đến 100");

        // Rule (a): Cannot change IsSupplier if group has active child groups or members
        if (group.IsSupplier != req.IsSupplier)
        {
            var hasActiveChildren = group.Children.Any(x => x.IsActive);
            var hasActiveCustomers = group.Customers.Any(x => x.IsActive);
            var hasActiveSuppliers = group.Suppliers.Any(x => x.IsActive);

            if (hasActiveChildren || hasActiveCustomers || hasActiveSuppliers)
            {
                throw new BusinessRuleException("Không thể đổi loại nhóm khi nhóm đã có nhóm con hoặc thành viên hoạt động");
            }
        }

        int? parentId = null;
        if (req.ParentId.HasValue)
        {
            if (req.ParentId.Value == id)
                throw new BusinessRuleException("Nhóm không thể là cha của chính mình");

            // Check cycle and parent type
            int? checkPid = req.ParentId;
            while (checkPid.HasValue)
            {
                if (checkPid.Value == id)
                    throw new BusinessRuleException("Phát hiện vòng lặp phân cấp nhóm");
                var p = await _db.CustomerGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == checkPid.Value);
                if (p is null)
                    throw new BusinessRuleException("Không tìm thấy nhóm cha");
                if (p.IsSupplier != req.IsSupplier)
                    throw new BusinessRuleException("Nhóm cha phải cùng loại (khách hàng hoặc nhà cung cấp)");
                checkPid = p.ParentId;
            }

            int newParentDepth = await GetGroupDepthAsync(req.ParentId.Value);
            int subtreeMaxHeight = await GetSubtreeMaxHeightAsync(id);
            if (newParentDepth + 1 + subtreeMaxHeight > 5)
                throw new BusinessRuleException("Phân cấp nhóm vượt quá giới hạn độ sâu cho phép (tối đa 5 tầng)");

            parentId = req.ParentId.Value;
        }

        group.Name = req.Name.Trim();
        group.IsSupplier = req.IsSupplier;
        group.DefaultDiscountRate = req.DefaultDiscountRate;
        group.ParentId = parentId;
        group.UpdatedAt = DateTime.UtcNow;

        try
        {
            PosLogService.Write(_db, "CUSTOMER_GROUP_UPDATED", userId, "customer_group", group.Code);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new BusinessRuleException("Mã nhóm đã tồn tại");
        }

        await _db.Entry(group).Reference(x => x.Parent).LoadAsync();
        return MapToResponse(group);
    }

    public async Task<bool> DeleteCustomerGroupAsync(int userId, int id)
    {
        var group = await _db.CustomerGroups
            .Include(x => x.Children)
            .Include(x => x.Customers)
            .Include(x => x.Suppliers)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (group is null || !group.IsActive)
            return false;

        var hasActiveChildren = group.Children.Any(x => x.IsActive);
        if (hasActiveChildren)
        {
            throw new BusinessRuleException("Không thể xóa nhóm khi vẫn còn nhóm con hoạt động");
        }

        var hasActiveCustomers = group.Customers.Any(x => x.IsActive);
        var hasActiveSuppliers = group.Suppliers.Any(x => x.IsActive);
        if (hasActiveCustomers || hasActiveSuppliers)
        {
            throw new BusinessRuleException("Không thể xóa nhóm khi vẫn còn thành viên hoạt động");
        }

        group.IsActive = false;
        group.UpdatedAt = DateTime.UtcNow;

        PosLogService.Write(_db, "CUSTOMER_GROUP_DELETED", userId, "customer_group", group.Code);
        await _db.SaveChangesAsync();
        return true;
    }

    private async Task<int> GetGroupDepthAsync(int groupId)
    {
        int depth = 1;
        var group = await _db.CustomerGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == groupId);
        while (group?.ParentId != null)
        {
            depth++;
            group = await _db.CustomerGroups.AsNoTracking().FirstOrDefaultAsync(x => x.Id == group.ParentId.Value);
        }
        return depth;
    }

    private async Task<int> GetSubtreeMaxHeightAsync(int groupId)
    {
        var children = await _db.CustomerGroups.AsNoTracking().Where(x => x.ParentId == groupId && x.IsActive).ToListAsync();
        if (!children.Any())
            return 0;
        int maxHeight = 0;
        foreach (var child in children)
        {
            int h = 1 + await GetSubtreeMaxHeightAsync(child.Id);
            if (h > maxHeight) maxHeight = h;
        }
        return maxHeight;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
            return true;
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) || msg.Contains("unique constraint", StringComparison.OrdinalIgnoreCase);
    }

    private static CustomerGroupResponse MapToResponse(CustomerGroup g) => new(
        g.Id, g.Code, g.Name, g.IsSupplier, g.DefaultDiscountRate, g.ParentId, g.Parent?.Name, g.IsActive, g.CreatedAt, g.UpdatedAt
    );
}
