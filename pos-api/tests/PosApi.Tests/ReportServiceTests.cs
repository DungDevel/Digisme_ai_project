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
}
