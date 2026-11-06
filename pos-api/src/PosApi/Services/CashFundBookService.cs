using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Dto;

namespace PosApi.Services;

public interface ICashFundBookService
{
    Task<CashFundBookSummaryResponse> GetFundBookSummaryAsync(DateTime? fromDate, DateTime? toDate);
    Task<PagedCashLedgerResponse> GetFundLedgerEntriesAsync(DateTime? fromDate, DateTime? toDate, int page, int pageSize);
}

public class CashFundBookService : ICashFundBookService
{
    private readonly AppDbContext _db;

    public CashFundBookService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CashFundBookSummaryResponse> GetFundBookSummaryAsync(DateTime? fromDate, DateTime? toDate)
    {
        var query = _db.CashLedger.AsNoTracking().AsQueryable();

        DateTime start = fromDate ?? DateTime.UtcNow.Date;
        DateTime end = toDate ?? DateTime.UtcNow.AddDays(1).Date;

        // Opening balance: balance before start date (or 0 if no entry before start)
        decimal openingBalance = 0m;
        var lastBeforeStart = await _db.CashLedger
            .AsNoTracking()
            .Where(x => x.CreatedAt < start)
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (lastBeforeStart is not null)
        {
            openingBalance = lastBeforeStart.Balance;
        }

        var periodEntries = await query
            .Where(x => x.CreatedAt >= start && x.CreatedAt < end)
            .ToListAsync();

        decimal totalReceipts = periodEntries.Where(x => x.Amount > 0).Sum(x => x.Amount);
        decimal totalPayments = periodEntries.Where(x => x.Amount < 0).Sum(x => Math.Abs(x.Amount));
        decimal closingBalance = openingBalance + totalReceipts - totalPayments;

        return new CashFundBookSummaryResponse(openingBalance, totalReceipts, totalPayments, closingBalance);
    }

    public async Task<PagedCashLedgerResponse> GetFundLedgerEntriesAsync(DateTime? fromDate, DateTime? toDate, int page, int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.CashLedger.AsNoTracking().AsQueryable();

        DateTime start = fromDate ?? DateTime.UtcNow.AddYears(-1);
        DateTime end = toDate ?? DateTime.UtcNow.AddDays(1);

        query = query.Where(x => x.CreatedAt >= start && x.CreatedAt < end);

        var summary = await GetFundBookSummaryAsync(fromDate, toDate);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var responses = items.Select(x => new CashLedgerItemResponse(
            x.Id,
            x.Amount,
            x.Balance,
            x.PaymentMethod,
            x.RefType,
            x.RefId,
            x.Note,
            x.CreatedBy,
            x.CreatedAt
        )).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        return new PagedCashLedgerResponse(page, pageSize, total, totalPages, summary.OpeningBalance, summary.ClosingBalance, responses);
    }
}
