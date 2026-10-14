using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface IShiftService
{
    Task<ShiftResponse> OpenShiftAsync(int userId, decimal openingCash);
    Task<ShiftResponse?> GetCurrentShiftAsync(int userId);
    Task<ShiftResponse> CloseShiftAsync(int userId, decimal countedCash);
    Task<PagedShiftResponse> ListShiftsAsync(int page, int pageSize);
}

public class ShiftService : IShiftService
{
    private readonly AppDbContext _context;
    private readonly INotificationService? _notificationService;
    private readonly ILogger<ShiftService> _log;

    public ShiftService(AppDbContext context, INotificationService? notificationService = null, ILogger<ShiftService>? log = null)
    {
        _context = context;
        _notificationService = notificationService;
        _log = log ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ShiftService>.Instance;
    }

    public async Task<ShiftResponse> OpenShiftAsync(int userId, decimal openingCash)
    {
        if (openingCash < 0)
            throw new BusinessRuleException("Tiền mặt đầu ca không được âm");

        var existingOpen = await _context.Shifts
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);

        if (existingOpen != null)
            throw new BusinessRuleException("Nhân viên đang có ca làm việc mở. Vui lòng đóng ca hiện tại trước khi mở ca mới.");

        var shift = new Shift
        {
            UserId = userId,
            OpeningCash = openingCash,
            Status = ShiftStatus.Open,
            StartedAt = DateTime.UtcNow
        };

        _context.Shifts.Add(shift);
        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(userId);
        var username = user?.Username ?? "";

        try
        {
            if (_notificationService != null)
            {
                await _notificationService.NotifyShiftOpenedAsync(shift.Id, userId, username);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to dispatch ShiftOpened notification for shift {ShiftId}", shift.Id);
        }

        return ToResponse(shift, username);
    }

    public async Task<ShiftResponse?> GetCurrentShiftAsync(int userId)
    {
        var shift = await _context.Shifts
            .Include(s => s.User)
            .Include(s => s.Orders)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);

        if (shift == null) return null;

        return ToResponse(shift, shift.User?.Username ?? "");
    }

    public async Task<ShiftResponse> CloseShiftAsync(int userId, decimal countedCash)
    {
        if (countedCash < 0)
            throw new BusinessRuleException("Tiền mặt kiểm đếm không được âm");

        var shift = await _context.Shifts
            .Include(s => s.User)
            .Include(s => s.Orders)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);

        if (shift == null)
            throw new BusinessRuleException("Không tìm thấy ca làm việc đang mở");

        decimal cashSales = shift.Orders
            .Where(o => o.Status == OrderStatus.Completed && o.PaymentMethod == PaymentMethod.Cash)
            .Sum(o => o.PaidAmount - o.Change);

        decimal expectedCash = shift.OpeningCash + cashSales;
        decimal diff = countedCash - expectedCash;

        shift.EndedAt = DateTime.UtcNow;
        shift.Status = ShiftStatus.Closed;
        shift.ExpectedCash = expectedCash;
        shift.CountedCash = countedCash;
        shift.Diff = diff;

        await _context.SaveChangesAsync();
        var username = shift.User?.Username ?? "";

        try
        {
            if (_notificationService != null)
            {
                await _notificationService.NotifyShiftClosedAsync(shift.Id, userId, username, countedCash, diff);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to dispatch ShiftClosed notification for shift {ShiftId}", shift.Id);
        }

        return ToResponse(shift, username);
    }

    public async Task<PagedShiftResponse> ListShiftsAsync(int page, int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _context.Shifts
            .AsNoTracking()
            .Include(s => s.User)
            .OrderByDescending(s => s.StartedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => ToResponse(s, s.User != null ? s.User.Username : ""))
            .ToListAsync();

        return new PagedShiftResponse(page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize), items);
    }

    private static ShiftResponse ToResponse(Shift s, string username) =>
        new(s.Id, s.UserId, username, s.StartedAt, s.EndedAt, s.Status.ToString(), s.OpeningCash, s.ExpectedCash ?? 0m, s.CountedCash ?? 0m, s.Diff ?? 0m);
}
