using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface IPosDeviceService
{
    Task<List<PosDeviceResponse>> ListPosDevicesAsync(int? counterId = null, bool? activeOnly = null);
    Task<PosDeviceResponse> GetPosDeviceByIdAsync(int id);
    Task<PosDeviceResponse> CreatePosDeviceAsync(CreatePosDeviceRequest req);
    Task<PosDeviceResponse> UpdatePosDeviceAsync(int id, UpdatePosDeviceRequest req);
    Task DeactivatePosDeviceAsync(int id);
}

public class PosDeviceService : IPosDeviceService
{
    private readonly AppDbContext _context;

    public PosDeviceService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<PosDeviceResponse>> ListPosDevicesAsync(int? counterId = null, bool? activeOnly = null)
    {
        var query = _context.PosDevices
            .Include(d => d.Counter)
            .AsNoTracking();

        if (counterId.HasValue)
            query = query.Where(d => d.CounterId == counterId.Value);

        if (activeOnly == true)
            query = query.Where(d => d.IsActive && d.Counter.IsActive);

        return await query
            .OrderBy(d => d.Name)
            .Select(d => new PosDeviceResponse(d.Id, d.CounterId, d.Counter.Name, d.Code, d.Name, d.IsActive, d.CreatedAt))
            .ToListAsync();
    }

    public async Task<PosDeviceResponse> GetPosDeviceByIdAsync(int id)
    {
        var d = await _context.PosDevices
            .Include(x => x.Counter)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new KeyNotFoundException($"Không tìm thấy máy POS {id}");
        return new PosDeviceResponse(d.Id, d.CounterId, d.Counter.Name, d.Code, d.Name, d.IsActive, d.CreatedAt);
    }

    public async Task<PosDeviceResponse> CreatePosDeviceAsync(CreatePosDeviceRequest req)
    {
        var code = req.Code.Trim().ToUpperInvariant();
        if (await _context.PosDevices.AnyAsync(d => d.Code == code))
            throw new BusinessRuleException($"Mã máy POS '{code}' đã tồn tại");

        var counter = await _context.Counters.FindAsync(req.CounterId)
            ?? throw new KeyNotFoundException($"Không tìm thấy quầy {req.CounterId}");

        if (!counter.IsActive)
            throw new BusinessRuleException("Không thể gán máy POS cho quầy đã bị vô hiệu hóa");

        var setting = await _context.StoreSettings.FirstOrDefaultAsync() ?? new StoreSetting();
        if (setting.MaxPosDevices.HasValue)
        {
            var activeCount = await _context.PosDevices.CountAsync(d => d.IsActive);
            if (activeCount >= setting.MaxPosDevices.Value)
                throw new BusinessRuleException($"Đã đạt giới hạn số lượng máy POS hoạt động tối đa ({setting.MaxPosDevices.Value})");
        }

        var device = new PosDevice
        {
            CounterId = req.CounterId,
            Code = code,
            Name = req.Name.Trim(),
            IsActive = req.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        _context.PosDevices.Add(device);
        await _context.SaveChangesAsync();

        return new PosDeviceResponse(device.Id, counter.Id, counter.Name, device.Code, device.Name, device.IsActive, device.CreatedAt);
    }

    public async Task<PosDeviceResponse> UpdatePosDeviceAsync(int id, UpdatePosDeviceRequest req)
    {
        var device = await _context.PosDevices.Include(d => d.Counter).FirstOrDefaultAsync(d => d.Id == id)
            ?? throw new KeyNotFoundException($"Không tìm thấy máy POS {id}");

        var counter = await _context.Counters.FindAsync(req.CounterId)
            ?? throw new KeyNotFoundException($"Không tìm thấy quầy {req.CounterId}");

        if (!counter.IsActive)
            throw new BusinessRuleException("Không thể chuyển máy POS sang quầy đã bị vô hiệu hóa");

        device.CounterId = req.CounterId;
        device.Name = req.Name.Trim();
        device.IsActive = req.IsActive;

        await _context.SaveChangesAsync();

        return new PosDeviceResponse(device.Id, counter.Id, counter.Name, device.Code, device.Name, device.IsActive, device.CreatedAt);
    }

    public async Task DeactivatePosDeviceAsync(int id)
    {
        var device = await _context.PosDevices.FindAsync(id)
            ?? throw new KeyNotFoundException($"Không tìm thấy máy POS {id}");

        device.IsActive = false;
        await _context.SaveChangesAsync();
    }
}
