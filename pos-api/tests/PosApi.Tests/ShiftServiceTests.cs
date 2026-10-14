using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class ShiftServiceTests : IAsyncLifetime
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

    private async Task<User> SeedUserAsync()
    {
        var user = new User
        {
            Username = "cashier1",
            FullName = "Thu Ngân 1",
            Email = "c1@pos.com",
            PasswordHash = "hash",
            Role = UserRole.Cashier
        };
        _context!.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task OpenShift_Success()
    {
        var user = await SeedUserAsync();
        var service = new ShiftService(_context!);

        var result = await service.OpenShiftAsync(user.Id, 1000000m);

        Assert.NotNull(result);
        Assert.Equal("Open", result.Status);
        Assert.Equal(1000000m, result.OpeningCash);
    }

    [Fact]
    public async Task OpenShift_AlreadyOpen_ThrowsBusinessRuleException()
    {
        var user = await SeedUserAsync();
        var service = new ShiftService(_context!);

        await service.OpenShiftAsync(user.Id, 500000m);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.OpenShiftAsync(user.Id, 500000m));
    }

    [Fact]
    public async Task CloseShift_Success_CalculatesExpectedCashAndDiff()
    {
        var user = await SeedUserAsync();
        var service = new ShiftService(_context!);

        await service.OpenShiftAsync(user.Id, 1000000m);

        var cat = new Category { Name = "C1" };
        _context!.Categories.Add(cat);
        await _context.SaveChangesAsync();

        var product = new Product
        {
            Name = "P1",
            Barcode = "P001",
            CategoryId = cat.Id,
            RetailPrice = 50000m,
            CurrentStock = 10m
        };
        _context.Products.Add(product);
        await _context.SaveChangesAsync();

        var shift = await _context.Shifts.FirstAsync(s => s.UserId == user.Id && s.Status == ShiftStatus.Open);

        var order = new Order
        {
            Code = "INV-001",
            CashierId = user.Id,
            ShiftId = shift.Id,
            Subtotal = 100000m,
            Total = 100000m,
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 100000m,
            Change = 0m,
            Status = OrderStatus.Completed
        };
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        var closed = await service.CloseShiftAsync(user.Id, 1150000m);

        Assert.NotNull(closed);
        Assert.Equal("Closed", closed.Status);
        Assert.Equal(1100000m, closed.ExpectedCash);
        Assert.Equal(1150000m, closed.CountedCash);
        Assert.Equal(50000m, closed.Diff);
    }
}
