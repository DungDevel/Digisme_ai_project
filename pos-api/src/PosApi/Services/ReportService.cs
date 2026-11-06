using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface IReportService
{
    Task<SalesReportResponse> GetSalesReportAsync(DateTime? from, DateTime? to);
    Task<TopProductsReportResponse> GetTopProductsAsync(DateTime? from, DateTime? to, int top = 10);
    Task<PaymentMethodsReportResponse> GetPaymentMethodsReportAsync(DateTime? from, DateTime? to);
}

public class ReportService : IReportService
{
    private readonly AppDbContext _context;

    public ReportService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<SalesReportResponse> GetSalesReportAsync(DateTime? from, DateTime? to)
    {
        var startDate = from ?? DateTime.UtcNow.Date.AddDays(-30);
        var endDate = to ?? DateTime.UtcNow;

        var query = _context.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Completed && o.CreatedAt >= startDate && o.CreatedAt <= endDate);

        var totalOrders = await query.CountAsync();
        var subtotal = await query.SumAsync(o => (decimal?)o.Subtotal) ?? 0m;
        var discount = await query.SumAsync(o => (decimal?)o.Discount) ?? 0m;
        var surchargeTotal = await query.SumAsync(o => (decimal?)o.SurchargeTotal) ?? 0m;
        var vat = await query.SumAsync(o => (decimal?)o.Vat) ?? 0m;
        var totalRevenue = await query.SumAsync(o => (decimal?)o.Total) ?? 0m;
        var totalPaid = await query.SumAsync(o => (decimal?)o.PaidAmount) ?? 0m;

        return new SalesReportResponse(startDate, endDate, totalOrders, subtotal, discount, surchargeTotal, vat, totalRevenue, totalPaid);
    }

    public async Task<TopProductsReportResponse> GetTopProductsAsync(DateTime? from, DateTime? to, int top = 10)
    {
        var startDate = from ?? DateTime.UtcNow.Date.AddDays(-30);
        var endDate = to ?? DateTime.UtcNow;

        // EF Core 8 không translate được GroupBy composite key (Id, Name, Barcode) từ join + aggregate
        // theo navigation trong cùng query. Báo cáo theo thời gian có giới hạn (mặc định 30 ngày),
        // số dòng OrderItem hoàn thành là nhỏ → lấy về group phía client (thủ tục đã ghi nhận).
        var raw = await (
            from oi in _context.OrderItems
            join o in _context.Orders on oi.OrderId equals o.Id
            join p in _context.Products on oi.ProductId equals p.Id
            where o.Status == OrderStatus.Completed && o.CreatedAt >= startDate && o.CreatedAt <= endDate && !oi.Cancelled
            select new { oi.ProductId, p.Name, p.Barcode, oi.Qty, oi.Total }
        )
        .ToListAsync();

        var items = raw
            .GroupBy(r => new { r.ProductId, r.Name, r.Barcode })
            .Select(g => new TopProductReportItem(
                g.Key.ProductId,
                g.Key.Name,
                g.Key.Barcode,
                g.Sum(x => x.Qty),
                g.Sum(x => x.Total)
            ))
            .OrderByDescending(x => x.TotalRevenue)
            .Take(top)
            .ToList();

        return new TopProductsReportResponse(startDate, endDate, items);
    }

    public async Task<PaymentMethodsReportResponse> GetPaymentMethodsReportAsync(DateTime? from, DateTime? to)
    {
        var startDate = from ?? DateTime.UtcNow.Date.AddDays(-30);
        var endDate = to ?? DateTime.UtcNow;

        var summaries = await _context.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Completed && o.CreatedAt >= startDate && o.CreatedAt <= endDate)
            .GroupBy(o => o.PaymentMethod)
            .Select(g => new PaymentMethodSummary(
                g.Key.ToString(),
                g.Count(),
                g.Sum(o => o.Total)
            ))
            .ToListAsync();

        return new PaymentMethodsReportResponse(startDate, endDate, summaries);
    }
}
