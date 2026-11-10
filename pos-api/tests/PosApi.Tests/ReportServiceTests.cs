using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class ReportServiceTests : IAsyncLifetime
{
    private AppDbContext? _context;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetSalesReportAsync_CalculatesCorrectly()
    {
        var cat = new Category { Name = "C1" };
        _context!.Categories.Add(cat);
        await _context.SaveChangesAsync();

        var product = new Product
        {
            Name = "P1",
            Barcode = "P001",
            CategoryId = cat.Id,
            RetailPrice = 100000m,
            CurrentStock = 10m
        };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var order = new Order
        {
            Code = "INV-001",
            CashierId = 1,
            Subtotal = 100000m,
            Discount = 10000m,
            SurchargeTotal = 0m,
            Vat = 9000m,
            Total = 99000m,
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 100000m,
            Change = 1000m,
            Status = OrderStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var service = new ReportService(_context);
        var report = await service.GetSalesReportAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        Assert.Equal(1, report.TotalOrders);
        Assert.Equal(100000m, report.Subtotal);
        Assert.Equal(10000m, report.Discount);
        Assert.Equal(99000m, report.TotalRevenue);
    }

    [Fact]
    public async Task GetTopProductsAsync_GroupsByProductAndRanksByRevenue()
    {
        var cat = new Category { Name = "C1" };
        _context!.Categories.Add(cat);
        await _context.SaveChangesAsync();

        var p1 = new Product { Name = "P1", Barcode = "P001", CategoryId = cat.Id, RetailPrice = 100000m, CurrentStock = 10m };
        var p2 = new Product { Name = "P2", Barcode = "P002", CategoryId = cat.Id, RetailPrice = 50000m, CurrentStock = 10m };
        _context.Products.AddRange(p1, p2);
        await _context.SaveChangesAsync();

        var order = new Order
        {
            Code = "INV-001",
            CashierId = 1,
            Subtotal = 250000m,
            Discount = 0m,
            SurchargeTotal = 0m,
            Vat = 0m,
            Total = 250000m,
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 250000m,
            Change = 0m,
            Status = OrderStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        var items = new[]
        {
            new OrderItem { OrderId = 0, ProductId = p1.Id, UnitPrice = 100000m, Qty = 2m, Total = 200000m, Cancelled = false },
            new OrderItem { OrderId = 0, ProductId = p2.Id, UnitPrice = 50000m, Qty = 1m, Total = 50000m, Cancelled = false }
        };
        order.Items.AddRange(items);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var service = new ReportService(_context);
        var report = await service.GetTopProductsAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), 10);

        Assert.Equal(2, report.Items.Count);
        Assert.Equal(p1.Id, report.Items[0].ProductId);
        Assert.Equal("P1", report.Items[0].ProductName);
        Assert.Equal(2m, report.Items[0].TotalQtySold);
        Assert.Equal(200000m, report.Items[0].TotalRevenue);
        Assert.Equal(50000m, report.Items[1].TotalRevenue);
    }

    [Fact]
    public async Task GetTaxDeclarationAsync_CalculatesOutputAndInputVat()
    {
        var cat = new Category { Name = "C1" };
        _context!.Categories.Add(cat);
        await _context.SaveChangesAsync();

        var order = new Order
        {
            Code = "INV-001",
            CashierId = 1,
            Total = 110000m,
            Vat = 10000m,
            Status = OrderStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        _context.Orders.Add(order);

        var stockIn = new StockIn
        {
            Code = "PHNK-001",
            Total = 550000m,
            CreatedAt = DateTime.UtcNow
        };
        _context.StockIns.Add(stockIn);
        await _context.SaveChangesAsync();

        var service = new ReportService(_context);
        var report = await service.GetTaxDeclarationAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        Assert.Equal(110000m, report.TotalSalesRevenue);
        Assert.Equal(10000m, report.TotalOutputVat);
        Assert.Equal(550000m, report.TotalPurchaseAmount);
        Assert.Equal(55000m, report.TotalInputVat);
    }

    [Fact]
    public async Task GetAdvancedDashboardAsync_CalculatesCogsAndGrossProfit()
    {
        var cat = new Category { Name = "C1" };
        _context!.Categories.Add(cat);
        await _context.SaveChangesAsync();

        var product = new Product
        {
            Name = "P1",
            Barcode = "P001",
            CategoryId = cat.Id,
            CostPrice = 60000m,
            RetailPrice = 100000m,
            CurrentStock = 5m,
            MinStock = 10m
        };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var order = new Order
        {
            Code = "INV-001",
            CashierId = 1,
            Subtotal = 100000m,
            Discount = 0m,
            SurchargeTotal = 0m,
            Vat = 0m,
            Total = 100000m,
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 100000m,
            Change = 0m,
            Status = OrderStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        order.Items.Add(new OrderItem
        {
            ProductId = product.Id,
            UnitPrice = 100000m,
            Qty = 1m,
            Total = 100000m,
            Cancelled = false
        });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var service = new ReportService(_context);
        var report = await service.GetAdvancedDashboardAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        Assert.Equal(100000m, report.TotalRevenue);
        Assert.Equal(60000m, report.TotalCostOfGoodsSold);
        Assert.Equal(40000m, report.GrossProfit);
        Assert.Equal(40m, report.GrossProfitMarginPercent);
        Assert.Single(report.LowStockItems);
    }

    [Fact]
    public async Task GetSlowMovingProductsAsync_ReturnsUnsoldProducts()
    {
        var cat = new Category { Name = "C1" };
        _context!.Categories.Add(cat);
        await _context.SaveChangesAsync();

        var p1 = new Product { Name = "SoldProduct", Barcode = "P001", CategoryId = cat.Id, CurrentStock = 10m };
        var p2 = new Product { Name = "SlowMoving", Barcode = "P002", CategoryId = cat.Id, CurrentStock = 20m, CostPrice = 15000m };
        _context.Products.AddRange(p1, p2);
        await _context.SaveChangesAsync();

        var order = new Order
        {
            Code = "INV-001",
            CashierId = 1,
            Total = 100000m,
            Status = OrderStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };
        order.Items.Add(new OrderItem { ProductId = p1.Id, UnitPrice = 100000m, Qty = 1m, Total = 100000m });
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var service = new ReportService(_context);
        var report = await service.GetSlowMovingProductsAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        Assert.Single(report.Items);
        Assert.Equal("SlowMoving", report.Items[0].ProductName);
        Assert.Equal(20m, report.Items[0].CurrentStock);
    }

    [Fact]
    public async Task GetCashFlowReportAsync_ReturnsFundBookAndVouchers()
    {
        var voucherService = new CashVoucherService(_context!);
        var request = new CashVoucherCreateRequest(
            CashVoucherType.Receipt,
            PartnerType.Other,
            null,
            300000m,
            PaymentMethod.Cash,
            "Manual",
            null,
            null,
            "Thu test"
        );
        var created = await voucherService.CreateVoucherAsync(1, request);
        await voucherService.ConfirmVoucherAsync(1, created.Id);

        var service = new ReportService(_context!);
        var report = await service.GetCashFlowReportAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        Assert.Equal(300000m, report.TotalReceipts);
        Assert.Equal(300000m, report.ClosingBalance);
        Assert.Single(report.Vouchers);
    }

    [Fact]
    public async Task GetSalesReportAsync_SubstractsReturnsCorrectly()
    {
        var cat = new Category { Name = "C1" };
        _context!.Categories.Add(cat);
        await _context.SaveChangesAsync();

        var product = new Product
        {
            Name = "P1",
            Barcode = "P001",
            CategoryId = cat.Id,
            RetailPrice = 100000m,
            CurrentStock = 10m
        };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var order = new Order
        {
            Code = "INV-001",
            CashierId = 1,
            Subtotal = 100000m,
            Total = 100000m,
            Status = OrderStatus.Completed,
            CreatedAt = DateTime.UtcNow,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = product.Id, Qty = 1, UnitPrice = 100000m, Total = 100000m, ReturnedQty = 1 }
            }
        };
        _context.Orders.Add(order);

        var returnRecord = new ReturnRecord
        {
            Code = "PHTRA-001",
            OrderId = order.Id,
            TotalReturned = 100000m,
            CreatedAt = DateTime.UtcNow
        };
        _context.Returns.Add(returnRecord);
        await _context.SaveChangesAsync();

        var service = new ReportService(_context);
        var report = await service.GetSalesReportAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        Assert.Equal(0m, report.TotalRevenue);
        Assert.Equal(0m, report.Subtotal);
    }
}
