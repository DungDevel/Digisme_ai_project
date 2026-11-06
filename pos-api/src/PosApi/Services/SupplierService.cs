using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public class SupplierService : ISupplierService
{
    private readonly AppDbContext _db;

    public SupplierService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedSupplierResponse> GetSuppliersAsync(string? q, int page, int pageSize, bool includeInactive = false)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.Suppliers.AsNoTracking().Include(x => x.CustomerGroup).AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            query = query.Where(x => x.Name.Contains(q) || x.Code.Contains(q) || (x.Phone != null && x.Phone.Contains(q)));
        }

        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var responses = items.Select(MapToResponse).ToList();
        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        return new PagedSupplierResponse(page, pageSize, total, totalPages, responses);
    }

    public async Task<SupplierResponse?> GetSupplierByIdAsync(int id)
    {
        var supplier = await _db.Suppliers.AsNoTracking().Include(x => x.CustomerGroup).FirstOrDefaultAsync(x => x.Id == id);
        return supplier is null ? null : MapToResponse(supplier);
    }

    public async Task<SupplierResponse> CreateSupplierAsync(int userId, SupplierCreateRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new BusinessRuleException("Tên nhà cung cấp bắt buộc");
        if (req.Name.Length > 128)
            throw new BusinessRuleException("Tên nhà cung cấp tối đa 128 ký tự");

        var trimmedName = req.Name.Trim();
        var existsName = await _db.Suppliers.AnyAsync(x => x.IsActive && x.Name.ToLower() == trimmedName.ToLower());
        if (existsName)
            throw new BusinessRuleException("Tên nhà cung cấp đã tồn tại");

        int? customerGroupId = null;
        if (req.CustomerGroupId.HasValue)
        {
            var group = await _db.CustomerGroups.FirstOrDefaultAsync(x => x.Id == req.CustomerGroupId.Value && x.IsActive);
            if (group is null || !group.IsSupplier)
                throw new BusinessRuleException("Nhóm nhà cung cấp không tồn tại hoặc không hợp lệ");
            customerGroupId = group.Id;
        }

        string code;
        if (!string.IsNullOrWhiteSpace(req.Code))
        {
            code = req.Code.Trim();
            if (code.Length > 32)
                throw new BusinessRuleException("Mã nhà cung cấp tối đa 32 ký tự");
            var existsCode = await _db.Suppliers.AnyAsync(x => x.Code == code);
            if (existsCode)
                throw new BusinessRuleException("Mã nhà cung cấp đã tồn tại");
        }
        else
        {
            code = await GenerateNextCodeAsync();
        }

        var supplier = new Supplier
        {
            Code = code,
            Name = trimmedName,
            Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
            Address = string.IsNullOrWhiteSpace(req.Address) ? null : req.Address.Trim(),
            TaxCode = string.IsNullOrWhiteSpace(req.TaxCode) ? null : req.TaxCode.Trim(),
            CustomerGroupId = customerGroupId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            _db.Suppliers.Add(supplier);
            PosLogService.Write(_db, "SUPPLIER_CREATED", userId, "supplier", supplier.Code);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new BusinessRuleException("Mã nhà cung cấp đã tồn tại");
        }

        await _db.Entry(supplier).Reference(x => x.CustomerGroup).LoadAsync();
        return MapToResponse(supplier);
    }

    public async Task<SupplierResponse?> UpdateSupplierAsync(int userId, int id, SupplierUpdateRequest req)
    {
        var supplier = await _db.Suppliers.Include(x => x.CustomerGroup).FirstOrDefaultAsync(x => x.Id == id);
        if (supplier is null)
            return null;

        if (string.IsNullOrWhiteSpace(req.Name))
            throw new BusinessRuleException("Tên nhà cung cấp bắt buộc");
        if (req.Name.Length > 128)
            throw new BusinessRuleException("Tên nhà cung cấp tối đa 128 ký tự");

        var trimmedName = req.Name.Trim();
        var existsName = await _db.Suppliers.AnyAsync(x => x.Id != id && x.IsActive && x.Name.ToLower() == trimmedName.ToLower());
        if (existsName)
            throw new BusinessRuleException("Tên nhà cung cấp đã tồn tại");

        int? customerGroupId = null;
        if (req.CustomerGroupId.HasValue)
        {
            var group = await _db.CustomerGroups.FirstOrDefaultAsync(x => x.Id == req.CustomerGroupId.Value && x.IsActive);
            if (group is null || !group.IsSupplier)
                throw new BusinessRuleException("Nhóm nhà cung cấp không tồn tại hoặc không hợp lệ");
            customerGroupId = group.Id;
        }

        supplier.Name = trimmedName;
        supplier.Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim();
        supplier.Address = string.IsNullOrWhiteSpace(req.Address) ? null : req.Address.Trim();
        supplier.TaxCode = string.IsNullOrWhiteSpace(req.TaxCode) ? null : req.TaxCode.Trim();
        supplier.CustomerGroupId = customerGroupId;
        supplier.UpdatedAt = DateTime.UtcNow;

        try
        {
            PosLogService.Write(_db, "SUPPLIER_UPDATED", userId, "supplier", supplier.Code);
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            throw new BusinessRuleException("Mã nhà cung cấp đã tồn tại");
        }

        await _db.Entry(supplier).Reference(x => x.CustomerGroup).LoadAsync();
        return MapToResponse(supplier);
    }

    public async Task<bool> DeleteSupplierAsync(int userId, int id)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id);
        if (supplier is null || !supplier.IsActive)
            return false;

        supplier.IsActive = false;
        supplier.UpdatedAt = DateTime.UtcNow;

        PosLogService.Write(_db, "SUPPLIER_DELETED", userId, "supplier", supplier.Code);
        await _db.SaveChangesAsync();
        return true;
    }

    private async Task<string> GenerateNextCodeAsync()
    {
        for (int i = 0; i < 5; i++)
        {
            var maxCode = await _db.Suppliers
                .Where(x => x.Code.StartsWith("NCC-"))
                .OrderByDescending(x => x.Code)
                .Select(x => x.Code)
                .FirstOrDefaultAsync();

            int nextNum = 1;
            if (maxCode != null && maxCode.Length > 4 && int.TryParse(maxCode.Substring(4), out int parsed))
            {
                nextNum = parsed + 1;
            }

            var candidate = $"NCC-{nextNum:D5}";
            var exists = await _db.Suppliers.AnyAsync(x => x.Code == candidate);
            if (!exists) return candidate;
        }

        return $"NCC-{Guid.NewGuid().ToString().Substring(0, 5).ToUpper()}";
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
            return true;
        var msg = ex.InnerException?.Message ?? ex.Message;
        return msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) || msg.Contains("unique constraint", StringComparison.OrdinalIgnoreCase);
    }

    private static SupplierResponse MapToResponse(Supplier s) => new(
        s.Id, s.Code, s.Name, s.Phone, s.Address, s.TaxCode, s.CustomerGroupId, s.CustomerGroup?.Name, s.IsActive, s.CreatedAt, s.UpdatedAt
    );
}
