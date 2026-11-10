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
    Task<TaxDeclarationReportResponse> GetTaxDeclarationAsync(DateTime? from, DateTime? to);
    Task<AdvancedDashboardResponse> GetAdvancedDashboardAsync(DateTime? from, DateTime? to);
    Task<SlowMovingReportResponse> GetSlowMovingProductsAsync(DateTime? from, DateTime? to);
    Task<CashFlowReportResponse> GetCashFlowReportAsync(DateTime? from, DateTime? to);
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

        var totalReturned = await _context.Returns
            .AsNoTracking()
            .Where(r => r.CreatedAt >= startDate && r.CreatedAt <= endDate)
            .SumAsync(r => (decimal?)r.TotalReturned) ?? 0m;

        totalRevenue = Math.Max(0m, totalRevenue - totalReturned);
        subtotal = Math.Max(0m, subtotal - totalReturned);

        return new SalesReportResponse(startDate, endDate, totalOrders, subtotal, discount, surchargeTotal, vat, totalRevenue, totalPaid);
    }

    public async Task<TopProductsReportResponse> GetTopProductsAsync(DateTime? from, DateTime? to, int top = 10)
    {
        var startDate = from ?? DateTime.UtcNow.Date.AddDays(-30);
        var endDate = to ?? DateTime.UtcNow;

        var raw = await (
            from oi in _context.OrderItems
            join o in _context.Orders on oi.OrderId equals o.Id
            join p in _context.Products on oi.ProductId equals p.Id
            where o.Status == OrderStatus.Completed && o.CreatedAt >= startDate && o.CreatedAt <= endDate && !oi.Cancelled
            select new { oi.ProductId, p.Name, p.Barcode, oi.Qty, oi.ReturnedQty, oi.Total }
        )
        .ToListAsync();

        var items = raw
            .GroupBy(r => new { r.ProductId, r.Name, r.Barcode })
            .Select(g => {
                var netQty = g.Sum(x => x.Qty - x.ReturnedQty);
                var netRevenue = g.Sum(x => x.Qty > 0 ? x.Total * ((x.Qty - x.ReturnedQty) / x.Qty) : 0m);
                return new TopProductReportItem(
                    g.Key.ProductId,
                    g.Key.Name,
                    g.Key.Barcode,
                    netQty,
                    netRevenue
                );
            })
            .Where(x => x.TotalQtySold > 0)
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

    public async Task<TaxDeclarationReportResponse> GetTaxDeclarationAsync(DateTime? from, DateTime? to)
    {
        var startDate = from ?? DateTime.UtcNow.Date.AddDays(-30);
        var endDate = to ?? DateTime.UtcNow;

        var ordersQuery = _context.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Completed && o.CreatedAt >= startDate && o.CreatedAt <= endDate);

        var totalSalesRevenue = await ordersQuery.SumAsync(o => (decimal?)o.Total) ?? 0m;
        var totalOutputVat = await ordersQuery.SumAsync(o => (decimal?)o.Vat) ?? 0m;

        var totalReturned = await _context.Returns
            .AsNoTracking()
            .Where(r => r.CreatedAt >= startDate && r.CreatedAt <= endDate)
            .SumAsync(r => (decimal?)r.TotalReturned) ?? 0m;

        totalSalesRevenue = Math.Max(0m, totalSalesRevenue - totalReturned);

        var stockInsQuery = _context.StockIns
            .AsNoTracking()
            .Where(s => s.CreatedAt >= startDate && s.CreatedAt <= endDate);

        var totalPurchaseAmount = await stockInsQuery.SumAsync(s => (decimal?)s.Total) ?? 0m;
        var totalInputVat = totalPurchaseAmount * 0.1m;

        return new TaxDeclarationReportResponse(
            startDate,
            endDate,
            totalSalesRevenue,
            totalOutputVat,
            totalPurchaseAmount,
            totalInputVat
        );
    }

    public async Task<AdvancedDashboardResponse> GetAdvancedDashboardAsync(DateTime? from, DateTime? to)
    {
        var startDate = from ?? DateTime.UtcNow.Date.AddDays(-30);
        var endDate = to ?? DateTime.UtcNow;

        var salesReport = await GetSalesReportAsync(startDate, endDate);
        var paymentMethodsReport = await GetPaymentMethodsReportAsync(startDate, endDate);
        var topProductsReport = await GetTopProductsAsync(startDate, endDate, 10);

        var lowStockProducts = await _context.Products
            .AsNoTracking()
            .Where(p => p.IsActive && p.CurrentStock <= p.MinStock)
            .OrderBy(p => p.CurrentStock)
            .Take(20)
            .Select(p => new InventoryItemBrief(p.Id, p.Name, p.Barcode, p.CurrentStock, "Piece", true))
            .ToListAsync();

        var orderItems = await (
            from oi in _context.OrderItems
            join o in _context.Orders on oi.OrderId equals o.Id
            join p in _context.Products on oi.ProductId equals p.Id
            where o.Status == OrderStatus.Completed && o.CreatedAt >= startDate && o.CreatedAt <= endDate && !oi.Cancelled
            select new { oi.Qty, p.CostPrice }
        ).ToListAsync();

        decimal totalCostOfGoodsSold = orderItems.Sum(x => x.Qty * x.CostPrice);
        decimal totalRevenue = salesReport.TotalRevenue;
        decimal grossProfit = totalRevenue - totalCostOfGoodsSold;
        decimal grossProfitMarginPercent = totalRevenue > 0 ? (grossProfit / totalRevenue) * 100m : 0m;

        var fundBookService = new CashFundBookService(_context);
        var cashSummary = await fundBookService.GetFundBookSummaryAsync(startDate, endDate);
        decimal currentCashBalance = cashSummary.ClosingBalance;

        return new AdvancedDashboardResponse(
            startDate,
            endDate,
            totalRevenue,
            totalCostOfGoodsSold,
            grossProfit,
            grossProfitMarginPercent,
            paymentMethodsReport.Items,
            topProductsReport.Items,
            lowStockProducts,
            currentCashBalance
        );
    }

    public async Task<SlowMovingReportResponse> GetSlowMovingProductsAsync(DateTime? from, DateTime? to)
    {
        var startDate = from ?? DateTime.UtcNow.Date.AddDays(-30);
        var endDate = to ?? DateTime.UtcNow;

        var soldProductIds = await _context.OrderItems
            .AsNoTracking()
            .Join(_context.Orders, oi => oi.OrderId, o => o.Id, (oi, o) => new { oi, o })
            .Where(x => x.o.Status == OrderStatus.Completed && x.o.CreatedAt >= startDate && x.o.CreatedAt <= endDate && !x.oi.Cancelled)
            .Select(x => x.oi.ProductId)
            .Distinct()
            .ToListAsync();

        var slowProducts = await _context.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive && p.CurrentStock > 0 && !soldProductIds.Contains(p.Id))
            .OrderByDescending(p => p.CurrentStock)
            .Take(50)
            .ToListAsync();

        var items = slowProducts.Select(p => new SlowMovingProductItem(
            p.Id,
            p.CategoryId,
            p.Category?.Name ?? "",
            p.Name,
            p.Barcode,
            p.CurrentStock,
            p.CostPrice,
            p.CurrentStock * p.CostPrice
        )).ToList();

        return new SlowMovingReportResponse(startDate, endDate, items);
    }

    public async Task<CashFlowReportResponse> GetCashFlowReportAsync(DateTime? from, DateTime? to)
    {
        var startDate = from ?? DateTime.UtcNow.Date.AddDays(-30);
        var endDate = to ?? DateTime.UtcNow;

        var fundBookService = new CashFundBookService(_context);
        var summary = await fundBookService.GetFundBookSummaryAsync(startDate, endDate);

        var vouchers = await _context.CashVouchers
            .AsNoTracking()
            .Where(v => v.Status == CashVoucherStatus.Confirmed && v.CreatedAt >= startDate && v.CreatedAt <= endDate)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();

        var customerIds = vouchers.Where(x => x.PartnerType == PartnerType.Customer && x.PartnerId.HasValue).Select(x => x.PartnerId!.Value).Distinct().ToList();
        var supplierIds = vouchers.Where(x => x.PartnerType == PartnerType.Supplier && x.PartnerId.HasValue).Select(x => x.PartnerId!.Value).Distinct().ToList();

        var customers = await _context.Customers.Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
        var suppliers = await _context.Suppliers.Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name);

        var voucherResponses = vouchers.Select(v =>
        {
            string? partnerName = null;
            if (v.PartnerType == PartnerType.Customer && v.PartnerId.HasValue)
                partnerName = customers.GetValueOrDefault(v.PartnerId.Value);
            else if (v.PartnerType == PartnerType.Supplier && v.PartnerId.HasValue)
                partnerName = suppliers.GetValueOrDefault(v.PartnerId.Value);

            return new CashVoucherResponse(
                v.Id,
                v.Code,
                v.Type,
                v.PartnerType,
                v.PartnerId,
                partnerName,
                v.Amount,
                v.PaymentMethod,
                v.ReferenceType,
                v.ReferenceId,
                v.ShiftId,
                v.Note,
                v.Status,
                v.CreatedBy,
                v.CreatedAt,
                v.ConfirmedAt
            );
        }).ToList();

        return new CashFlowReportResponse(
            startDate,
            endDate,
            summary.OpeningBalance,
            summary.TotalReceipts,
            summary.TotalPayments,
            summary.ClosingBalance,
            voucherResponses
        );
    }
}
