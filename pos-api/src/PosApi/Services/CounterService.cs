using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface ICounterService
{
    Task<List<CounterResponse>> ListCountersAsync(bool? activeOnly = null);
    Task<CounterResponse> GetCounterByIdAsync(int id);
    Task<CounterResponse> CreateCounterAsync(CreateCounterRequest req);
    Task<CounterResponse> UpdateCounterAsync(int id, UpdateCounterRequest req);
    Task DeactivateCounterAsync(int id);
}

public class CounterService : ICounterService
{
    private readonly AppDbContext _context;

    public CounterService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<CounterResponse>> ListCountersAsync(bool? activeOnly = null)
    {
        var query = _context.Counters.AsNoTracking();
        if (activeOnly == true)
            query = query.Where(c => c.IsActive);

        return await query
            .OrderBy(c => c.IsDefault ? 0 : 1)
            .ThenBy(c => c.Name)
            .Select(c => new CounterResponse(c.Id, c.Code, c.Name, c.IsDefault, c.IsActive, c.CreatedAt))
            .ToListAsync();
    }

    public async Task<CounterResponse> GetCounterByIdAsync(int id)
    {
        var c = await _context.Counters.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new KeyNotFoundException($"Không tìm thấy quầy {id}");
        return new CounterResponse(c.Id, c.Code, c.Name, c.IsDefault, c.IsActive, c.CreatedAt);
    }

    public async Task<CounterResponse> CreateCounterAsync(CreateCounterRequest req)
    {
        var code = req.Code.Trim().ToUpperInvariant();
        if (await _context.Counters.AnyAsync(c => c.Code == code))
            throw new BusinessRuleException($"Mã quầy '{code}' đã tồn tại");

        var setting = await _context.StoreSettings.FirstOrDefaultAsync() ?? new StoreSetting();
        if (setting.MaxCounters.HasValue)
        {
            var activeCount = await _context.Counters.CountAsync(c => c.IsActive);
            if (activeCount >= setting.MaxCounters.Value)
                throw new BusinessRuleException($"Đã đạt giới hạn số lượng quầy hoạt động tối đa ({setting.MaxCounters.Value})");
        }

        var counter = new Counter
        {
            Code = code,
            Name = req.Name.Trim(),
            IsDefault = false,
            IsActive = req.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _context.Counters.Add(counter);
        await _context.SaveChangesAsync();

        return new CounterResponse(counter.Id, counter.Code, counter.Name, counter.IsDefault, counter.IsActive, counter.CreatedAt);
    }

    public async Task<CounterResponse> UpdateCounterAsync(int id, UpdateCounterRequest req)
    {
        var counter = await _context.Counters.FindAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy quầy {id}");

        if (counter.IsDefault && !req.IsActive)
            throw new BusinessRuleException("Không được tắt quầy mặc định (Counter-Default)");

        if (!req.IsActive)
        {
            var hasActiveDevices = await _context.PosDevices.AnyAsync(d => d.CounterId == id && d.IsActive);
            if (hasActiveDevices)
                throw new BusinessRuleException("Không thể vô hiệu hóa quầy đang có máy POS hoạt động");

            var hasOpenShifts = await _context.Shifts.AnyAsync(s => s.CounterId == id && s.Status == ShiftStatus.Open);
            if (hasOpenShifts)
                throw new BusinessRuleException("Không thể vô hiệu hóa quầy đang có ca làm việc mở");
        }

        counter.Name = req.Name.Trim();
        counter.IsActive = req.IsActive;

        await _context.SaveChangesAsync();

        return new CounterResponse(counter.Id, counter.Code, counter.Name, counter.IsDefault, counter.IsActive, counter.CreatedAt);
    }

    public async Task DeactivateCounterAsync(int id)
    {
        var counter = await _context.Counters.FindAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy quầy {id}");

        if (counter.IsDefault)
            throw new BusinessRuleException("Không được xóa/vô hiệu hóa quầy mặc định (Counter-Default)");

        var hasActiveDevices = await _context.PosDevices.AnyAsync(d => d.CounterId == id && d.IsActive);
        if (hasActiveDevices)
            throw new BusinessRuleException("Không thể vô hiệu hóa quầy đang có máy POS hoạt động");

        var hasOpenShifts = await _context.Shifts.AnyAsync(s => s.CounterId == id && s.Status == ShiftStatus.Open);
        if (hasOpenShifts)
            throw new BusinessRuleException("Không thể vô hiệu hóa quầy đang có ca làm việc mở");

        counter.IsActive = false;
        await _context.SaveChangesAsync();
    }
}
