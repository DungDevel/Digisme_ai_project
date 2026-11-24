using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface IShiftService
{
    Task<ShiftResponse> OpenShiftAsync(int userId, decimal openingCash, int? counterId = null, int? posDeviceId = null);
    Task<ShiftResponse?> GetCurrentShiftAsync(int userId);
    Task<ShiftSummaryResponse> GetShiftSummaryAsync(int userId);
    Task<ShiftResponse> CloseShiftAsync(int userId, decimal countedCash, decimal? countedTransfer = null, Dictionary<int, int>? denominations = null, string? note = null);
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

    public async Task<ShiftResponse> OpenShiftAsync(int userId, decimal openingCash, int? counterId = null, int? posDeviceId = null)
    {
        if (openingCash < 0)
            throw new BusinessRuleException("Tiền mặt đầu ca không được âm");

        Counter counter;
        if (counterId.HasValue)
        {
            counter = await _context.Counters.FirstOrDefaultAsync(c => c.Id == counterId.Value)
                ?? throw new KeyNotFoundException($"Không tìm thấy quầy {counterId.Value}");
        }
        else
        {
            counter = await _context.Counters.FirstOrDefaultAsync(c => c.IsDefault)
                ?? throw new BusinessRuleException("Chưa có quầy mặc định — liên hệ quản trị");
        }

        if (!counter.IsActive)
            throw new BusinessRuleException("Quầy đã bị vô hiệu hóa — không thể mở ca");

        if (posDeviceId.HasValue)
        {
            var posDevice = await _context.PosDevices.FirstOrDefaultAsync(d => d.Id == posDeviceId.Value)
                ?? throw new KeyNotFoundException($"Không tìm thấy máy POS {posDeviceId.Value}");

            if (!posDevice.IsActive)
                throw new BusinessRuleException("Máy POS đã bị vô hiệu hóa — không thể sử dụng");

            if (posDevice.CounterId != counter.Id)
                throw new BusinessRuleException("Máy POS không thuộc quầy đã chọn");
        }

        var existingUserOpen = await _context.Shifts
            .Include(s => s.User)
            .Include(s => s.Counter)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);

        if (existingUserOpen != null)
            throw new BusinessRuleException("Nhân viên đang có ca làm việc mở. Vui lòng đóng ca hiện tại trước khi mở ca mới.");

        var strategy = _context.Database.CreateExecutionStrategy();
        Shift shift = null!;

        await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _context.Database.CurrentTransaction;
            await using var tx = existingTx == null && _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync() : null;

            if (_context.Database.IsRelational())
            {
                var conn = _context.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync();

                using (var cmd = conn.CreateCommand() as NpgsqlCommand)
                {
                    cmd!.CommandText = "SELECT \"Id\" FROM \"Counters\" WHERE \"Id\" = @cid FOR UPDATE";
                    if (_context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx)
                    {
                        cmd.Transaction = npgTx;
                    }
                    cmd.Parameters.Add(new NpgsqlParameter("@cid", counter.Id));
                    await cmd.ExecuteScalarAsync();
                }
            }

            var counterHasOpen = await _context.Shifts
                .AnyAsync(s => s.CounterId == counter.Id && s.Status == ShiftStatus.Open);
            if (counterHasOpen)
                throw new BusinessRuleException("Quầy đang có ca mở");

            shift = new Shift
            {
                UserId = userId,
                CounterId = counter.Id,
                PosDeviceId = posDeviceId,
                OpeningCash = openingCash,
                Status = ShiftStatus.Open,
                StartedAt = DateTime.UtcNow
            };

            _context.Shifts.Add(shift);
            await _context.SaveChangesAsync();
            if (tx != null)
                await tx.CommitAsync();
        });

        shift.Counter = counter;
        if (shift.User == null)
            await _context.Entry(shift).Reference(s => s.User).LoadAsync();

        var username = shift.User?.Username ?? "";

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
            .Include(s => s.Counter)
            .Include(s => s.Orders)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);

        if (shift == null) return null;

        return ToResponse(shift, shift.User?.Username ?? "");
    }

    public async Task<ShiftSummaryResponse> GetShiftSummaryAsync(int userId)
    {
        var shift = await _context.Shifts
            .Include(s => s.User)
            .Include(s => s.Counter)
            .Include(s => s.Orders)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);

        if (shift == null)
            throw new KeyNotFoundException("Không có ca làm việc nào đang mở");

        decimal cashSales = shift.Orders
            .Where(o => o.Status == OrderStatus.Completed && o.PaymentMethod == PaymentMethod.Cash)
            .Sum(o => o.PaidAmount - o.Change);

        decimal transferSales = shift.Orders
            .Where(o => o.Status == OrderStatus.Completed && (o.PaymentMethod == PaymentMethod.VietQr || o.PaymentMethod == PaymentMethod.BankTransfer))
            .Sum(o => o.Total);

        var cashVouchers = await _context.CashVouchers
            .Where(cv => cv.ShiftId == shift.Id && cv.Status == CashVoucherStatus.Confirmed)
            .ToListAsync();

        decimal cashReceipts = cashVouchers.Where(cv => cv.Type == CashVoucherType.Receipt && cv.PaymentMethod == PaymentMethod.Cash).Sum(cv => cv.Amount);
        decimal cashPayments = cashVouchers.Where(cv => cv.Type == CashVoucherType.Payment && cv.PaymentMethod == PaymentMethod.Cash).Sum(cv => cv.Amount);

        decimal expectedCash = shift.OpeningCash + cashSales + cashReceipts - cashPayments;
        decimal expectedTransfer = transferSales;

        return new ShiftSummaryResponse(
            shift.Id,
            shift.UserId,
            shift.User?.Username ?? "",
            shift.StartedAt,
            shift.OpeningCash,
            cashSales,
            transferSales,
            cashReceipts,
            cashPayments,
            expectedCash,
            expectedTransfer,
            shift.Orders.Count,
            shift.Orders.Select(o => new ShiftOrderBrief(o.Id, o.Code, o.Total, o.PaymentMethod.ToString(), o.CreatedAt)).ToList()
        );
    }

    public async Task<ShiftResponse> CloseShiftAsync(int userId, decimal countedCash, decimal? countedTransfer = null, Dictionary<int, int>? denominations = null, string? note = null)
    {
        if (denominations != null && denominations.Count > 0)
        {
            var calculatedCash = denominations.Sum(kv => kv.Key * kv.Value);
            countedCash = calculatedCash;
        }

        if (countedCash < 0)
            throw new BusinessRuleException("Tiền mặt kiểm đếm không được âm");

        var shift = await _context.Shifts
            .Include(s => s.User)
            .Include(s => s.Counter)
            .Include(s => s.Orders)
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);

        if (shift == null)
            throw new KeyNotFoundException("Không tìm thấy ca làm việc đang mở");

        decimal cashSales = shift.Orders
            .Where(o => o.Status == OrderStatus.Completed && o.PaymentMethod == PaymentMethod.Cash)
            .Sum(o => o.PaidAmount - o.Change);

        decimal transferSales = shift.Orders
            .Where(o => o.Status == OrderStatus.Completed && (o.PaymentMethod == PaymentMethod.VietQr || o.PaymentMethod == PaymentMethod.BankTransfer))
            .Sum(o => o.Total);

        var cashVouchers = await _context.CashVouchers
            .Where(cv => cv.ShiftId == shift.Id && cv.Status == CashVoucherStatus.Confirmed)
            .ToListAsync();

        decimal cashReceipts = cashVouchers.Where(cv => cv.Type == CashVoucherType.Receipt && cv.PaymentMethod == PaymentMethod.Cash).Sum(cv => cv.Amount);
        decimal cashPayments = cashVouchers.Where(cv => cv.Type == CashVoucherType.Payment && cv.PaymentMethod == PaymentMethod.Cash).Sum(cv => cv.Amount);

        decimal expectedCash = shift.OpeningCash + cashSales + cashReceipts - cashPayments;
        decimal diff = countedCash - expectedCash;

        decimal expectedTransfer = transferSales;
        decimal? transferDiff = countedTransfer.HasValue ? countedTransfer.Value - expectedTransfer : (decimal?)null;

        string? denominationsJson = denominations != null && denominations.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(denominations)
            : null;

        shift.EndedAt = DateTime.UtcNow;
        shift.Status = ShiftStatus.Closed;
        shift.ExpectedCash = expectedCash;
        shift.CountedCash = countedCash;
        shift.Diff = diff;
        shift.TransferSales = transferSales;
        shift.CountedTransfer = countedTransfer;
        shift.TransferDiff = transferDiff;
        shift.DenominationsJson = denominationsJson;
        shift.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

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
            .Include(s => s.Counter)
            .OrderByDescending(s => s.StartedAt);

        var total = await query.CountAsync();
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => ToResponse(s, s.User != null ? s.User.Username : ""))
            .ToListAsync();

        return new PagedShiftResponse(page, pageSize, total, (int)Math.Ceiling(total / (double)pageSize), items);
    }

    private static ShiftResponse ToResponse(Shift s, string username) =>
        new(s.Id, s.UserId, username, s.CounterId, s.Counter?.Name ?? "Quầy mặc định", s.PosDeviceId, s.StartedAt, s.EndedAt, s.Status.ToString(), s.OpeningCash, s.ExpectedCash ?? 0m, s.CountedCash ?? 0m, s.Diff ?? 0m, s.TransferSales, s.CountedTransfer, s.TransferDiff, s.DenominationsJson, s.Note);
}
