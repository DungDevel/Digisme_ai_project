using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class BankManualResolveTests
{
    private readonly DatabaseFixture _fixture = new();

    private async Task<User> SeedUserAsync(AppDbContext context, string username, UserRole role = UserRole.Manager)
    {
        var user = new User
        {
            Username = username,
            FullName = username,
            Email = username + "@test.com",
            PasswordHash = "hash",
            Role = role
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task ManualMatch_Completes()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u1_" + Guid.NewGuid().ToString("N").Substring(0,4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261124-0008", CashierId = user.Id, ShiftId = shift.Id, Subtotal = 100000m, Total = 100000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var bankTx = new BankTransaction { Provider = "SePay", ExternalId = Guid.NewGuid().ToString(), Amount = 100000m, Status = BankTransactionStatus.Unmatched, UnmatchedReason = UnmatchedReason.OrderNotFound };
            context.BankTransactions.Add(bankTx);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            var result = await service.ManualMatchAsync(bankTx.Id, order.Id, "Khớp tay", user.Id);

            Assert.NotNull(result);
            Assert.Equal("Resolved", result.Status);
            var updatedOrder = await context.Orders.FindAsync(order.Id);
            Assert.Equal(OrderStatus.Completed, updatedOrder!.Status);
        }
    }

    [Fact]
    public async Task ManualMatch_Mismatch_WithoutNote_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u2_" + Guid.NewGuid().ToString("N").Substring(0,4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261124-0009", CashierId = user.Id, ShiftId = shift.Id, Subtotal = 100000m, Total = 100000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var bankTx = new BankTransaction { Provider = "SePay", ExternalId = Guid.NewGuid().ToString(), Amount = 90000m, Status = BankTransactionStatus.Unmatched, UnmatchedReason = UnmatchedReason.AmountMismatch };
            context.BankTransactions.Add(bankTx);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.ManualMatchAsync(bankTx.Id, order.Id, null, user.Id));
        }
    }

    [Fact]
    public async Task ManualMatch_AlreadyResolved_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u3_" + Guid.NewGuid().ToString("N").Substring(0,4));
            var bankTx = new BankTransaction { Provider = "SePay", ExternalId = Guid.NewGuid().ToString(), Amount = 100000m, Status = BankTransactionStatus.Resolved };
            context.BankTransactions.Add(bankTx);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.ManualMatchAsync(bankTx.Id, 1, "note", user.Id));
        }
    }

    [Fact]
    public async Task Ignore_RequiresNote()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u4_" + Guid.NewGuid().ToString("N").Substring(0,4));
            var bankTx = new BankTransaction { Provider = "SePay", ExternalId = Guid.NewGuid().ToString(), Amount = 100000m, Status = BankTransactionStatus.Unmatched };
            context.BankTransactions.Add(bankTx);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.ManualIgnoreAsync(bankTx.Id, "", user.Id));

            var result = await service.ManualIgnoreAsync(bankTx.Id, "Bỏ qua rác", user.Id);
            Assert.Equal("Ignored", result.Status);
        }
    }

    [Fact]
    public async Task Ignore_MatchedTransaction_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u5_" + Guid.NewGuid().ToString("N").Substring(0,4));
            var bankTx = new BankTransaction { Provider = "SePay", ExternalId = Guid.NewGuid().ToString(), Amount = 100000m, Status = BankTransactionStatus.Matched };
            context.BankTransactions.Add(bankTx);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.ManualIgnoreAsync(bankTx.Id, "Bỏ qua", user.Id));
        }
    }

    [Fact]
    public async Task ManualMatch_OrderAlreadyCompleted_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u6_" + Guid.NewGuid().ToString("N").Substring(0,4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261124-0010", CashierId = user.Id, ShiftId = shift.Id, Subtotal = 100000m, Total = 100000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var bankTx1 = new BankTransaction { Provider = "SePay", ExternalId = Guid.NewGuid().ToString(), Amount = 100000m, Status = BankTransactionStatus.Unmatched };
            var bankTx2 = new BankTransaction { Provider = "SePay", ExternalId = Guid.NewGuid().ToString(), Amount = 100000m, Status = BankTransactionStatus.Unmatched };
            context.BankTransactions.AddRange(bankTx1, bankTx2);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            // Match first transaction to order
            await service.ManualMatchAsync(bankTx1.Id, order.Id, "Khớp 1", user.Id);

            // Matching second transaction to same order (which is now Completed) should throw
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.ManualMatchAsync(bankTx2.Id, order.Id, "Khớp 2", user.Id));
        }
    }
}
