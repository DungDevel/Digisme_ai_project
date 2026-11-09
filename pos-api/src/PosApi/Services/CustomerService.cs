using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface ICustomerService
{
    Task<CustomerResponse> GetByPhoneAsync(string phone);
    Task<PagedCustomerResponse> SearchAsync(string? q, int page, int pageSize);
    Task<int> CreateAsync(CustomerCreateRequest req, int userId);
    Task UpdateAsync(int id, CustomerUpdateRequest req, int userId);
    Task DeleteAsync(int id, int userId);
}

public class CustomerService : ICustomerService
{
    private readonly AppDbContext _db;

    public CustomerService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CustomerResponse> GetByPhoneAsync(string phone)
    {
        var normalized = NormalizePhone(phone);
        if (string.IsNullOrEmpty(normalized)) return null!;

        var c = await _db.Customers.AsNoTracking()
            .Include(x => x.Loyalty)
            .FirstOrDefaultAsync(x => x.Phone == normalized && x.IsActive);

        if (c == null) return null!;

        var totalDebt = await _db.Debts.AsNoTracking()
            .Where(d => d.CustomerId == c.Id && d.Status != DebtStatus.Settled)
            .SumAsync(d => d.Amount - d.PaidAmount);

        return ToResponse(c, totalDebt);
    }

    public async Task<PagedCustomerResponse> SearchAsync(string? q, int page, int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.Customers.AsNoTracking().Include(x => x.Loyalty).Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(x => x.Name.Contains(q) || x.Phone.Contains(q));

        var total = await query.CountAsync();
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var ids = items.Select(i => i.Id).ToList();
        var debts = await _db.Debts.AsNoTracking()
            .Where(d => ids.Contains(d.CustomerId) && d.Status != DebtStatus.Settled)
            .GroupBy(d => d.CustomerId)
            .Select(g => new { id = g.Key, sum = g.Sum(x => x.Amount - x.PaidAmount) })
            .ToDictionaryAsync(k => k.id, v => v.sum);

        return new PagedCustomerResponse(page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize),
            items.Select(c => ToResponse(c, debts.GetValueOrDefault(c.Id, 0m))).ToList());
    }

    public async Task<int> CreateAsync(CustomerCreateRequest req, int userId)
    {
        var phone = NormalizePhone(req.Phone);
        if (string.IsNullOrEmpty(phone)) throw new BusinessRuleException("Số điện thoại không hợp lệ");
        if (string.IsNullOrWhiteSpace(req.Name)) throw new BusinessRuleException("Họ tên bắt buộc");

        var existing = await _db.Customers.FirstOrDefaultAsync(x => x.Phone == phone);
        if (existing is not null) throw new ConflictException($"Khách với SĐT '{phone}' đã tồn tại");

        var cust = new Customer
        {
            Phone = phone,
            Name = req.Name.Trim(),
            Address = string.IsNullOrWhiteSpace(req.Address) ? null : req.Address.Trim()
        };
        cust.Loyalty = new LoyaltyAccount { Points = 0, Tier = "Silver", LifetimeSpend = 0m };

        _db.Customers.Add(cust);
        PosLogService.Write(_db, "CUSTOMER_CREATED", userId, "customer", phone);
        await _db.SaveChangesAsync();

        return cust.Id;
    }

    public async Task UpdateAsync(int id, CustomerUpdateRequest req, int userId)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) throw new KeyNotFoundException($"Không tìm thấy khách hàng #{id}");

        c.Name = req.Name.Trim();
        c.Address = string.IsNullOrWhiteSpace(req.Address) ? null : req.Address.Trim();
        c.IsActive = req.IsActive;

        PosLogService.Write(_db, "CUSTOMER_UPDATED", userId, "customer", c.Phone, new { c.Name });
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id, int userId)
    {
        var c = await _db.Customers.FirstOrDefaultAsync(x => x.Id == id);
        if (c == null) throw new KeyNotFoundException($"Không tìm thấy khách hàng #{id}");

        c.IsActive = false; // Soft Delete
        PosLogService.Write(_db, "CUSTOMER_DEACTIVATED", userId, "customer", c.Phone, new { c.Name });
        await _db.SaveChangesAsync();
    }

    private static CustomerResponse ToResponse(Customer c, decimal totalDebt) =>
        new(c.Id, c.Phone, c.Name, c.Address, c.IsActive, c.Loyalty?.Points ?? 0, c.Loyalty?.Tier ?? "Silver", totalDebt);

    private static string? NormalizePhone(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return null;
        var digits = new string(p.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("84") && digits.Length is 11 or 12) digits = digits[2..];
        return digits.Length is >= 9 and <= 10 ? digits : null;
    }
}
