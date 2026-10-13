using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

/// <summary>
/// Test cho DebtService - tập trung vào logic áp dụng thanh toán công nợ.
/// Sử dụng EF Core In-Memory Database để test độc lập và nhanh chóng.
/// Mỗi test có một context riêng với dữ liệu sạch.
/// </summary>
public class DebtServiceTests : IAsyncLifetime
{
    private AppDbContext? _context;

    public async Task InitializeAsync()
    {
        // Tạo options cho in-memory database
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task ApplyPayment_FullPayment_SetsSettledStatus()
    {
        // Seed data: khách hàng và công nợ Open
        var customer = new Customer { Phone = "0909090909", Name = "Test Customer", IsActive = true };
        _context!.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var debt = new Debt
        {
            CustomerId = customer.Id,
            Amount = 100000m,
            PaidAmount = 0m,
            Status = DebtStatus.Open,
            Note = null
        };
        _context.Debts.Add(debt);
        await _context.SaveChangesAsync();

        var service = new DebtService(_context);
        var userId = 1;

        // Act: Trả đủ 100000
        var (updatedDebt, newRemaining) = service.ApplyPayment(debt, 100000m, "Trả nợ đơn INV-20261009-0001", userId);

        // Assert
        Assert.Equal(DebtStatus.Settled, updatedDebt.Status);
        Assert.Equal(100000m, updatedDebt.PaidAmount);
        Assert.Equal(0m, newRemaining);
        Assert.Contains("Trả nợ đơn INV-20261009-0001", updatedDebt.Note ?? "");
    }

    [Fact]
    public async Task ApplyPayment_PartialPayment_SetsPartiallyPaidStatus()
    {
        var customer = new Customer { Phone = "0909090910", Name = "Test Customer 2", IsActive = true };
        _context!.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var debt = new Debt
        {
            CustomerId = customer.Id,
            Amount = 100000m,
            PaidAmount = 0m,
            Status = DebtStatus.Open,
            Note = null
        };
        _context.Debts.Add(debt);
        await _context.SaveChangesAsync();

        var service = new DebtService(_context);
        var userId = 1;

        // Act: Trả một phần 30000
        var (updatedDebt, newRemaining) = service.ApplyPayment(debt, 30000m, "Đợt 1", userId);

        // Assert
        Assert.Equal(DebtStatus.PartiallyPaid, updatedDebt.Status);
        Assert.Equal(30000m, updatedDebt.PaidAmount);
        Assert.Equal(70000m, newRemaining);
        Assert.Equal("Đợt 1", updatedDebt.Note);
    }

    [Fact]
    public async Task ApplyPayment_ExceedsRemaining_ThrowsException()
    {
        var customer = new Customer { Phone = "0909090911", Name = "Test Customer 3", IsActive = true };
        _context!.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var debt = new Debt
        {
            CustomerId = customer.Id,
            Amount = 100000m,
            PaidAmount = 20000m,
            Status = DebtStatus.PartiallyPaid,
            Note = null
        };
        _context.Debts.Add(debt);
        await _context.SaveChangesAsync();

        var service = new DebtService(_context);
        var userId = 1;

        var exception = Assert.Throws<BusinessRuleException>(
            () => service.ApplyPayment(debt, 90000m, "Over payment", userId)
        );
        Assert.Contains("vượt số còn nợ", exception.Message);
        Assert.Contains("80,000", exception.Message);
    }

    [Fact]
    public async Task ApplyPayment_AlreadySettled_ThrowsException()
    {
        var customer = new Customer { Phone = "0909090912", Name = "Test Customer 4", IsActive = true };
        _context!.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var debt = new Debt
        {
            CustomerId = customer.Id,
            Amount = 100000m,
            PaidAmount = 100000m,
            Status = DebtStatus.Settled,
            Note = "Đã thanh toán"
        };
        _context.Debts.Add(debt);
        await _context.SaveChangesAsync();

        var service = new DebtService(_context);
        var userId = 1;

        var exception = Assert.Throws<BusinessRuleException>(
            () => service.ApplyPayment(debt, 10000m, "Extra", userId)
        );
        Assert.Contains("đã thanh toán xong", exception.Message);
    }

    [Fact]
    public async Task ApplyPayment_ZeroOrNegativeAmount_ThrowsException()
    {
        var customer = new Customer { Phone = "0909090913", Name = "Test Customer 5", IsActive = true };
        _context!.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var debt = new Debt
        {
            CustomerId = customer.Id,
            Amount = 50000m,
            PaidAmount = 0m,
            Status = DebtStatus.Open,
            Note = null
        };
        _context.Debts.Add(debt);
        await _context.SaveChangesAsync();

        var service = new DebtService(_context);
        var userId = 1;

        var exception1 = Assert.Throws<BusinessRuleException>(
            () => service.ApplyPayment(debt, 0m, "Zero", userId)
        );
        Assert.Contains("phải lớn hơn 0", exception1.Message);

        var exception2 = Assert.Throws<BusinessRuleException>(
            () => service.ApplyPayment(debt, -1000m, "Negative", userId)
        );
        Assert.Contains("phải lớn hơn 0", exception2.Message);
    }

    [Fact]
    public async Task ApplyPayment_WritesPosLog()
    {
        var customer = new Customer { Phone = "0909090914", Name = "Test Customer 6", IsActive = true };
        _context!.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var debt = new Debt
        {
            CustomerId = customer.Id,
            Amount = 50000m,
            PaidAmount = 0m,
            Status = DebtStatus.Open,
            Note = null
        };
        _context.Debts.Add(debt);
        await _context.SaveChangesAsync();

        var service = new DebtService(_context);
        var userId = 5;

        service.ApplyPayment(debt, 25000m, "Thanh toán một phần", userId);
        await _context.SaveChangesAsync();

        var logEntry = await _context.PosLogs.FirstOrDefaultAsync(l => l.EntityId == debt.Id.ToString());
        Assert.NotNull(logEntry);
        Assert.Equal("DEBT_PAID", logEntry.Action);
        Assert.Equal(userId, logEntry.UserId);
    }

    [Fact]
    public async Task ApplyPayment_MultiplePayments_StatusUpdatesCorrectly()
    {
        var customer = new Customer { Phone = "0909090915", Name = "Test Customer 7", IsActive = true };
        _context!.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var debt = new Debt
        {
            CustomerId = customer.Id,
            Amount = 100000m,
            PaidAmount = 0m,
            Status = DebtStatus.Open,
            Note = null
        };
        _context.Debts.Add(debt);
        await _context.SaveChangesAsync();

        var service = new DebtService(_context);
        var userId = 1;

        var (debt1, rem1) = service.ApplyPayment(debt, 40000m, "Đợt 1", userId);
        Assert.Equal(DebtStatus.PartiallyPaid, debt1.Status);
        Assert.Equal(60000m, rem1);

        var (debt2, rem2) = service.ApplyPayment(debt, 60000m, "Đợt 2", userId);
        Assert.Equal(DebtStatus.Settled, debt2.Status);
        Assert.Equal(0m, rem2);
        Assert.Equal(100000m, debt2.PaidAmount);
        Assert.Contains("Đợt 1", debt2.Note ?? "");
        Assert.Contains("Đợt 2", debt2.Note ?? "");
    }

    [Fact]
    public async Task ApplyPayment_PartialPayment_FractionalAmount()
    {
        var customer = new Customer { Phone = "0909090916", Name = "Test Customer 8", IsActive = true };
        _context!.Customers.Add(customer);
        await _context.SaveChangesAsync();

        var debt = new Debt
        {
            CustomerId = customer.Id,
            Amount = 100000m,
            PaidAmount = 0m,
            Status = DebtStatus.Open,
            Note = null
        };
        _context.Debts.Add(debt);
        await _context.SaveChangesAsync();

        var service = new DebtService(_context);
        var userId = 1;

        // Trả một phần nhỏ hơn 1 VNĐ
        var (updatedDebt, newRemaining) = service.ApplyPayment(debt, 0.0001m, "Đợt 1", userId);
        Assert.Equal(DebtStatus.PartiallyPaid, updatedDebt.Status);
        Assert.Equal(0.0001m, updatedDebt.PaidAmount);
        Assert.Equal(99999.9999m, newRemaining);
    }

    public async Task DisposeAsync()
    {
        _context?.Dispose();
        _context = null;
    }
}
