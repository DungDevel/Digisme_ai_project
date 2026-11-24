using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class BankReconciliationTests
{
    private readonly DatabaseFixture _fixture = new();

    private async Task<User> SeedUserAsync(AppDbContext context, string username, UserRole role = UserRole.Cashier)
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
    public async Task Match_ExactAmount_CompletesOrderAndPayment()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u1_" + Guid.NewGuid().ToString("N").Substring(0,4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order
            {
                Code = "INV-20261124-0001",
                CashierId = user.Id,
                ShiftId = shift.Id,
                Subtotal = 100000m,
                Total = 100000m,
                PaymentMethod = PaymentMethod.BankTransfer,
                Status = OrderStatus.Pending
            };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var payment = new Payment
            {
                OrderId = order.Id,
                Method = PaymentMethod.BankTransfer,
                Amount = 100000m,
                Status = PaymentStatus.Pending
            };
            context.Payments.Add(payment);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            var incoming = new[]
            {
                new IncomingBankTransaction(
                    ExternalId: Guid.NewGuid().ToString(),
                    AccountNumber: "",
                    Amount: 100000m,
                    Content: "Thanh toán đơn INV-20261124-0001",
                    ReferenceCode: "FT123",
                    TransactionDateUtc: DateTime.UtcNow
                )
            };

            await service.ProcessAsync(incoming);

            var updatedOrder = await context.Orders.FindAsync(order.Id);
            Assert.Equal(OrderStatus.Completed, updatedOrder!.Status);
            Assert.Equal(100000m, updatedOrder.PaidAmount);

            var updatedPayment = await context.Payments.FirstOrDefaultAsync(p => p.OrderId == order.Id);
            Assert.Equal(PaymentStatus.Paid, updatedPayment!.Status);

            var bankTx = await context.BankTransactions.FirstOrDefaultAsync(b => b.OrderId == order.Id);
            Assert.NotNull(bankTx);
            Assert.Equal(BankTransactionStatus.Matched, bankTx.Status);
        }
    }

    [Fact]
    public async Task Match_ContentWithoutHyphen_Matches()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u2_" + Guid.NewGuid().ToString("N").Substring(0,4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order
            {
                Code = "INV-20261124-0002",
                CashierId = user.Id,
                ShiftId = shift.Id,
                Subtotal = 50000m,
                Total = 50000m,
                PaymentMethod = PaymentMethod.VietQr,
                Status = OrderStatus.Pending
            };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            var incoming = new[]
            {
                new IncomingBankTransaction(
                    ExternalId: Guid.NewGuid().ToString(),
                    AccountNumber: "",
                    Amount: 50000m,
                    Content: "CK INV202611240002",
                    ReferenceCode: null,
                    TransactionDateUtc: DateTime.UtcNow
                )
            };

            await service.ProcessAsync(incoming);

            var updatedOrder = await context.Orders.FindAsync(order.Id);
            Assert.Equal(OrderStatus.Completed, updatedOrder!.Status);
        }
    }

    [Fact]
    public async Task Duplicate_ExternalId_Ignored()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u3_" + Guid.NewGuid().ToString("N").Substring(0,4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order
            {
                Code = "INV-20261124-0003",
                CashierId = user.Id,
                ShiftId = shift.Id,
                Subtotal = 50000m,
                Total = 50000m,
                PaymentMethod = PaymentMethod.BankTransfer,
                Status = OrderStatus.Pending
            };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            var extId = Guid.NewGuid().ToString();
            var incoming = new[]
            {
                new IncomingBankTransaction(ExternalId: extId, AccountNumber: "", Amount: 50000m, Content: "INV-20261124-0003", ReferenceCode: null, TransactionDateUtc: DateTime.UtcNow)
            };

            await service.ProcessAsync(incoming);
            await service.ProcessAsync(incoming);

            var count = await context.BankTransactions.CountAsync(b => b.ExternalId == extId);
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task AmountMismatch_Unmatched_OrderStaysPending()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u4_" + Guid.NewGuid().ToString("N").Substring(0,4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order1 = new Order { Code = "INV-20261124-0004", CashierId = user.Id, ShiftId = shift.Id, Subtotal = 100000m, Total = 100000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            var order2 = new Order { Code = "INV-20261124-0005", CashierId = user.Id, ShiftId = shift.Id, Subtotal = 100000m, Total = 100000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.AddRange(order1, order2);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);

            await service.ProcessAsync(new[] { new IncomingBankTransaction(Guid.NewGuid().ToString(), "", 50000m, "INV-20261124-0004", null, DateTime.UtcNow) });
            await service.ProcessAsync(new[] { new IncomingBankTransaction(Guid.NewGuid().ToString(), "", 150000m, "INV-20261124-0005", null, DateTime.UtcNow) });

            var o1 = await context.Orders.FindAsync(order1.Id);
            var o2 = await context.Orders.FindAsync(order2.Id);
            Assert.Equal(OrderStatus.Pending, o1!.Status);
            Assert.Equal(OrderStatus.Pending, o2!.Status);

            var tx1 = await context.BankTransactions.FirstOrDefaultAsync(b => b.OrderId == order1.Id);
            var tx2 = await context.BankTransactions.FirstOrDefaultAsync(b => b.OrderId == order2.Id);
            Assert.Equal(BankTransactionStatus.Unmatched, tx1!.Status);
            Assert.Equal(UnmatchedReason.AmountMismatch, tx1.UnmatchedReason);
            Assert.Equal(BankTransactionStatus.Unmatched, tx2!.Status);
            Assert.Equal(UnmatchedReason.AmountMismatch, tx2.UnmatchedReason);
        }
    }

    [Fact]
    public async Task OrderNotFound_Unmatched()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            await service.ProcessAsync(new[] { new IncomingBankTransaction(Guid.NewGuid().ToString(), "", 100000m, "INV-20261124-9999", null, DateTime.UtcNow) });

            var bankTx = await context.BankTransactions.FirstOrDefaultAsync();
            Assert.NotNull(bankTx);
            Assert.Equal(BankTransactionStatus.Unmatched, bankTx.Status);
            Assert.Equal(UnmatchedReason.OrderNotFound, bankTx.UnmatchedReason);
        }
    }

    [Fact]
    public async Task OrderAlreadyCompleted_Unmatched_OrderNotPending()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u5_" + Guid.NewGuid().ToString("N").Substring(0,4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261124-0006", CashierId = user.Id, ShiftId = shift.Id, Subtotal = 100000m, Total = 100000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Completed };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            await service.ProcessAsync(new[] { new IncomingBankTransaction(Guid.NewGuid().ToString(), "", 100000m, "INV-20261124-0006", null, DateTime.UtcNow) });

            var bankTx = await context.BankTransactions.FirstOrDefaultAsync();
            Assert.NotNull(bankTx);
            Assert.Equal(BankTransactionStatus.Unmatched, bankTx.Status);
            Assert.Equal(UnmatchedReason.OrderNotPending, bankTx.UnmatchedReason);
        }
    }

    [Fact]
    public async Task NoOrderCode_Unmatched()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            await service.ProcessAsync(new[] { new IncomingBankTransaction(Guid.NewGuid().ToString(), "", 100000m, "Chuyen tien khong ro noi dung", null, DateTime.UtcNow) });

            var bankTx = await context.BankTransactions.FirstOrDefaultAsync();
            Assert.NotNull(bankTx);
            Assert.Equal(BankTransactionStatus.Unmatched, bankTx.Status);
            Assert.Equal(UnmatchedReason.NoOrderCode, bankTx.UnmatchedReason);
        }
    }

    [Fact]
    public async Task OutgoingAmount_Ignored()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            await service.ProcessAsync(new[] { new IncomingBankTransaction(Guid.NewGuid().ToString(), "", 0m, "INV-20261124-0001", null, DateTime.UtcNow, AmountIn: 0m, AmountOut: 100000m) });

            var count = await context.BankTransactions.CountAsync();
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task OtherAccountNumber_Ignored()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var setting = await context.StoreSettings.FirstAsync();
            setting.VietQrAccountNo = "123456789";
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            await service.ProcessAsync(new[] { new IncomingBankTransaction(Guid.NewGuid().ToString(), "987654321", 100000m, "INV-20261124-0001", null, DateTime.UtcNow) });

            var count = await context.BankTransactions.CountAsync();
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task Match_OrderAppearsInShiftTransferSales()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u6_" + Guid.NewGuid().ToString("N").Substring(0,4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261124-0007", CashierId = user.Id, ShiftId = shift.Id, Subtotal = 200000m, Total = 200000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<BankReconciliationService>.Instance);
            await service.ProcessAsync(new[] { new IncomingBankTransaction(Guid.NewGuid().ToString(), "", 200000m, "INV-20261124-0007", null, DateTime.UtcNow) });

            var shiftService = new ShiftService(context);
            var summary = await shiftService.GetShiftSummaryAsync(user.Id);
            Assert.Equal(200000m, summary.TransferSales);
        }
    }
}
