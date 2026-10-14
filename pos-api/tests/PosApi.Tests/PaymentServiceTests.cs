using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class PaymentServiceTests : IAsyncLifetime
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
    public async Task GenerateVietQrAsync_CreatesPaymentAndReturnsUrl()
    {
        var storeSetting = new StoreSetting
        {
            Id = 1,
            StoreName = "Test Store",
            VietQrBankCode = "VCB",
            VietQrAccountNo = "123456789",
            VietQrAccountName = "TEST STORE"
        };
        _context!.StoreSettings.Add(storeSetting);

        var order = new Order
        {
            Code = "ORD-001",
            CashierId = 1,
            Subtotal = 200000m,
            Total = 200000m,
            PaymentMethod = PaymentMethod.VietQr,
            Status = OrderStatus.Pending
        };
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var service = new PaymentService(_context);
        var result = await service.GenerateVietQrAsync(order.Id);

        Assert.NotNull(result);
        Assert.Equal("ORD-001", result.OrderCode);
        Assert.Equal(200000m, result.Amount);
        Assert.Equal("VCB", result.BankCode);
        Assert.Equal("123456789", result.AccountNo);
        Assert.Contains("img.vietqr.io", result.QrImageUrl);
        Assert.Equal("Pending", result.Status);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_UpdatesPaymentAndOrderToPaid()
    {
        var payment = new Payment
        {
            OrderId = 1,
            Method = PaymentMethod.VietQr,
            Amount = 150000m,
            Reference = "ORD-002",
            Status = PaymentStatus.Pending
        };
        var order = new Order
        {
            Id = 1,
            Code = "ORD-002",
            CashierId = 1,
            Subtotal = 150000m,
            Total = 150000m,
            Status = OrderStatus.Pending
        };
        _context!.Payments.Add(payment);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var service = new PaymentService(_context);
        var result = await service.ConfirmPaymentAsync(payment.Id, userId: 1, PaymentStatus.Paid, "FT240101");

        Assert.NotNull(result);
        Assert.Equal("Paid", result.Status);
        Assert.Equal("FT240101", result.Reference);
        Assert.NotNull(result.PaidAt);

        var updatedOrder = await _context.Orders.FindAsync(1);
        Assert.Equal(OrderStatus.Completed, updatedOrder!.Status);
        Assert.Equal(150000m, updatedOrder.PaidAmount);
    }
}
