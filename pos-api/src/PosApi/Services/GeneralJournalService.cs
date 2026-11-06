using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Dto;

namespace PosApi.Services;

public interface IGeneralJournalService
{
    Task<PagedGeneralJournalResponse> GetJournalEntriesAsync(DateTime? fromDate, DateTime? toDate, int page, int pageSize);
}

public class GeneralJournalService : IGeneralJournalService
{
    private readonly AppDbContext _db;

    public GeneralJournalService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedGeneralJournalResponse> GetJournalEntriesAsync(DateTime? fromDate, DateTime? toDate, int page, int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        DateTime start = fromDate ?? DateTime.UtcNow.AddMonths(-1);
        DateTime end = toDate ?? DateTime.UtcNow.AddDays(1);

        // Gather from Orders
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Cashier)
            .Where(o => o.CreatedAt >= start && o.CreatedAt < end && o.Status == Domain.OrderStatus.Completed)
            .Select(o => new GeneralJournalItemResponse(
                $"ORDER-{o.Id}",
                o.CreatedAt,
                "Bán hàng",
                o.Code,
                $"Bán hàng đơn {o.Code}",
                o.Total,
                o.Customer != null ? o.Customer.Name : "Khách lẻ",
                o.Cashier.FullName
            ))
            .ToListAsync();

        // Gather from CashVouchers (Confirmed)
        var vouchers = await _db.CashVouchers
            .AsNoTracking()
            .Where(v => v.CreatedAt >= start && v.CreatedAt < end && v.Status == Domain.CashVoucherStatus.Confirmed)
            .Select(v => new GeneralJournalItemResponse(
                $"VOUCHER-{v.Id}",
                v.CreatedAt,
                v.Type == Domain.CashVoucherType.Receipt ? "Phiếu thu" : "Phiếu chi",
                v.Code,
                v.Note ?? (v.Type == Domain.CashVoucherType.Receipt ? "Thu tiền" : "Chi tiền"),
                v.Amount,
                v.PartnerType.ToString(),
                v.CreatedBy.ToString()
            ))
            .ToListAsync();

        // Gather from StockIns
        var stockIns = await _db.StockIns
            .AsNoTracking()
            .Where(s => s.CreatedAt >= start && s.CreatedAt < end)
            .Select(s => new GeneralJournalItemResponse(
                $"STOCKIN-{s.Id}",
                s.CreatedAt,
                "Nhập kho",
                s.Code,
                s.Note ?? $"Nhập kho từ {s.SupplierName}",
                s.Total,
                s.SupplierName ?? "NCC",
                "System"
            ))
            .ToListAsync();

        // Combine all journal entries
        var allEntries = orders.Concat(vouchers).Concat(stockIns)
            .OrderByDescending(x => x.Timestamp)
            .ToList();

        var total = allEntries.Count;
        var pagedItems = allEntries.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        return new PagedGeneralJournalResponse(page, pageSize, total, totalPages, pagedItems);
    }
}
