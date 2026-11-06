using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
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
}
