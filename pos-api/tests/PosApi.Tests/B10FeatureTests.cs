using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class B10FeatureTests
{
    private readonly DatabaseFixture _fixture = new();

    private async Task<User> SeedUserAsync(AppDbContext ctx, string username)
    {
        var user = new User
        {
            Username = username,
            FullName = username,
            Email = username + "@test.com",
            PasswordHash = "hash",
            Role = UserRole.Cashier
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    private async Task<Customer> SeedCustomerAsync(AppDbContext ctx, string phone)
    {
        var cust = new Customer { Phone = phone, Name = "Customer " + phone };
        ctx.Customers.Add(cust);
        await ctx.SaveChangesAsync();
        return cust;
    }

    private async Task EnsureWarehouseAsync(AppDbContext context)
    {
        if (!await context.Warehouses.AnyAsync())
        {
            context.Warehouses.Add(new Warehouse { Code = "WH-DEFAULT", Name = "Kho mặc định", IsDefault = true, IsActive = true });
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ShiftOpen_GiuNguyenCa()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_1_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0001", CashierId = user.Id, ShiftId = shift.Id, Subtotal = 100000m, Total = 100000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, NullLogger<BankReconciliationService>.Instance);
            await service.ProcessAsync(new[] { new PosApi.Services.IncomingBankTransaction(Guid.NewGuid().ToString(), "", 100000m, "INV-20261125-0001", null, DateTime.UtcNow) });

            var updatedOrder = await context.Orders.FirstAsync(o => o.Id == order.Id);
            Assert.Equal(shift.Id, updatedOrder.ShiftId);
            Assert.Equal(OrderStatus.Completed, updatedOrder.Status);
        }
    }

    [Fact]
    public async Task CaDaDong_GanVaoCaDangMoCungCounter()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_2_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var counter = new Counter { Code = "C1", Name = "Quầy 1", IsActive = true };
            context.Counters.Add(counter);
            await context.SaveChangesAsync();

            var oldShift = new Shift { UserId = user.Id, CounterId = counter.Id, Status = ShiftStatus.Closed, OpeningCash = 100m };
            context.Shifts.Add(oldShift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0002", CashierId = user.Id, ShiftId = oldShift.Id, Subtotal = 150000m, Total = 150000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var newShift = new Shift { UserId = user.Id, CounterId = counter.Id, Status = ShiftStatus.Open, OpeningCash = 200m };
            context.Shifts.Add(newShift);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, NullLogger<BankReconciliationService>.Instance);
            await service.ProcessAsync(new[] { new PosApi.Services.IncomingBankTransaction(Guid.NewGuid().ToString(), "", 150000m, "INV-20261125-0002", null, DateTime.UtcNow) });

            var updatedOrder = await context.Orders.FirstAsync(o => o.Id == order.Id);
            Assert.Equal(newShift.Id, updatedOrder.ShiftId);
        }
    }

    [Fact]
    public async Task KhongCoCaMo_ShiftIdNull()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_3_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var counter = new Counter { Code = "C2", Name = "Quầy 2", IsActive = true };
            context.Counters.Add(counter);
            await context.SaveChangesAsync();

            var oldShift = new Shift { UserId = user.Id, CounterId = counter.Id, Status = ShiftStatus.Closed, OpeningCash = 100m };
            context.Shifts.Add(oldShift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0003", CashierId = user.Id, ShiftId = oldShift.Id, Subtotal = 120000m, Total = 120000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, NullLogger<BankReconciliationService>.Instance);
            await service.ProcessAsync(new[] { new PosApi.Services.IncomingBankTransaction(Guid.NewGuid().ToString(), "", 120000m, "INV-20261125-0003", null, DateTime.UtcNow) });

            var updatedOrder = await context.Orders.FirstAsync(o => o.Id == order.Id);
            Assert.Null(updatedOrder.ShiftId);
        }
    }

    [Fact]
    public async Task MoCaMoi_GanDonCho()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_4_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var counter = new Counter { Code = "C3", Name = "Quầy 3", IsActive = true };
            context.Counters.Add(counter);
            await context.SaveChangesAsync();

            var orderWithTx = new Order { Code = "INV-20261125-0004", CashierId = user.Id, ShiftId = null, Subtotal = 100000m, Total = 100000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Completed };
            var orderWithoutTx = new Order { Code = "INV-20261125-0005", CashierId = user.Id, ShiftId = null, Subtotal = 100000m, Total = 100000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Completed };
            context.Orders.AddRange(orderWithTx, orderWithoutTx);
            await context.SaveChangesAsync();

            var bankTx = new BankTransaction { Provider = "SePay", ExternalId = "ext-004", AccountNumber = "", Amount = 100000m, OrderId = orderWithTx.Id, Status = BankTransactionStatus.Matched };
            context.BankTransactions.Add(bankTx);
            await context.SaveChangesAsync();

            var shiftService = new ShiftService(context);
            var newShiftRes = await shiftService.OpenShiftAsync(user.Id, 500m, counterId: counter.Id);

            var updatedOrderWithTx = await context.Orders.FirstAsync(o => o.Id == orderWithTx.Id);
            var updatedOrderWithoutTx = await context.Orders.FirstAsync(o => o.Id == orderWithoutTx.Id);

            Assert.Equal(newShiftRes.Id, updatedOrderWithTx.ShiftId);
            Assert.Null(updatedOrderWithoutTx.ShiftId);
        }
    }

    [Fact]
    public async Task TongCa_BaoGomDonChuyenKhoanDuocGan()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_5_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var shiftService = new ShiftService(context);
            var shiftRes = await shiftService.OpenShiftAsync(user.Id, 1000m);

            var order = new Order { Code = "INV-20261125-0006", CashierId = user.Id, ShiftId = shiftRes.Id, Subtotal = 300000m, Total = 300000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, NullLogger<BankReconciliationService>.Instance);
            await service.ProcessAsync(new[] { new PosApi.Services.IncomingBankTransaction(Guid.NewGuid().ToString(), "", 300000m, "INV-20261125-0006", null, DateTime.UtcNow) });

            var summary = await shiftService.GetShiftSummaryAsync(user.Id);
            Assert.Equal(300000m, summary.TransferSales);
        }
    }

    [Fact]
    public async Task DonChuyenKhoan_CoKhach_DuocTichDiem()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_6_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var cust = await SeedCustomerAsync(context, "0912345678");
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0007", CashierId = user.Id, CustomerId = cust.Id, ShiftId = shift.Id, Subtotal = 50000m, Total = 50000m, ShippingFee = 5000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, NullLogger<BankReconciliationService>.Instance);
            await service.ProcessAsync(new[] { new PosApi.Services.IncomingBankTransaction(Guid.NewGuid().ToString(), "", 50000m, "INV-20261125-0007", null, DateTime.UtcNow) });

            var updatedOrder = await context.Orders.FirstAsync(o => o.Id == order.Id);
            Assert.Equal(45, updatedOrder.LoyaltyEarned);

            var loyalty = await context.LoyaltyAccounts.FirstAsync(l => l.CustomerId == cust.Id);
            Assert.Equal(45, loyalty.Points);
            Assert.Equal(45000m, loyalty.LifetimeSpend);
        }
    }

    [Fact]
    public async Task KhongCoKhach_KhongTich()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_7_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0008", CashierId = user.Id, CustomerId = null, ShiftId = shift.Id, Subtotal = 50000m, Total = 50000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, NullLogger<BankReconciliationService>.Instance);
            await service.ProcessAsync(new[] { new PosApi.Services.IncomingBankTransaction(Guid.NewGuid().ToString(), "", 50000m, "INV-20261125-0008", null, DateTime.UtcNow) });

            var updatedOrder = await context.Orders.Include(o => o.Payments).FirstAsync(o => o.Id == order.Id);
            Assert.Equal(0, updatedOrder.LoyaltyEarned);
            var loyaltyCount = await context.LoyaltyAccounts.CountAsync();
            Assert.Equal(0, loyaltyCount);

            Assert.Equal(OrderStatus.Completed, updatedOrder.Status);
            Assert.Single(updatedOrder.Payments);
            var ptCount = await context.PointTransactions.CountAsync(pt => pt.OrderId == order.Id);
            Assert.Equal(0, ptCount);
        }
    }

    [Fact]
    public async Task GoiHaiLan_ChiTichMotLan()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_8_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var cust = await SeedCustomerAsync(context, "0987654321");
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0009", CashierId = user.Id, CustomerId = cust.Id, ShiftId = shift.Id, Subtotal = 20000m, Total = 20000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Completed };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var conn = context.Database.IsRelational() ? context.Database.GetDbConnection() : null;
            await OrderService.EarnLoyaltyForCompletedOrderAsync(context, order, conn, null);
            await context.SaveChangesAsync();

            // Call second time
            await OrderService.EarnLoyaltyForCompletedOrderAsync(context, order, conn, null);
            await context.SaveChangesAsync();

            var ptCount = await context.PointTransactions.CountAsync(pt => pt.OrderId == order.Id);
            Assert.Equal(1, ptCount);

            var loyalty = await context.LoyaltyAccounts.FirstAsync(l => l.CustomerId == cust.Id);
            Assert.Equal(20, loyalty.Points);
            Assert.Equal(20000m, loyalty.LifetimeSpend);
        }
    }

    [Fact]
    public async Task ChotLoyaltyEarned_KhongTichThem()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_8a_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var cust = await SeedCustomerAsync(context, "0987654322");
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0009a", CashierId = user.Id, CustomerId = cust.Id, ShiftId = shift.Id, Subtotal = 20000m, Total = 20000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Completed, LoyaltyEarned = 20 };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var conn = context.Database.IsRelational() ? context.Database.GetDbConnection() : null;
            await OrderService.EarnLoyaltyForCompletedOrderAsync(context, order, conn, null);
            await context.SaveChangesAsync();

            var ptCount = await context.PointTransactions.CountAsync(pt => pt.OrderId == order.Id);
            Assert.Equal(0, ptCount);

            var loyaltyAccCount = await context.LoyaltyAccounts.CountAsync(l => l.CustomerId == cust.Id);
            Assert.Equal(0, loyaltyAccCount);
        }
    }

    [Fact]
    public async Task ChotHasEarnTx_KhongTichThem()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_8b_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var cust = await SeedCustomerAsync(context, "0987654323");
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0009b", CashierId = user.Id, CustomerId = cust.Id, ShiftId = shift.Id, Subtotal = 20000m, Total = 20000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Completed, LoyaltyEarned = 0 };
            var loyaltyAcc = new LoyaltyAccount { CustomerId = cust.Id, Points = 20, LifetimeSpend = 0m };
            context.Orders.Add(order);
            context.LoyaltyAccounts.Add(loyaltyAcc);
            context.PointTransactions.Add(new PointTransaction { LoyaltyAccount = loyaltyAcc, Order = order, Type = PointTransactionType.Earn, Points = 20 });
            await context.SaveChangesAsync();

            var conn = context.Database.IsRelational() ? context.Database.GetDbConnection() : null;
            await OrderService.EarnLoyaltyForCompletedOrderAsync(context, order, conn, null);
            await context.SaveChangesAsync();

            var ptCount = await context.PointTransactions.CountAsync(pt => pt.OrderId == order.Id && pt.Type == PointTransactionType.Earn);
            Assert.Equal(1, ptCount);

            var reloadedLoyalty = await context.LoyaltyAccounts.FirstAsync(l => l.CustomerId == cust.Id);
            Assert.Equal(20, reloadedLoyalty.Points);
            Assert.Equal(0m, reloadedLoyalty.LifetimeSpend);
        }
    }

    [Fact]
    public async Task ManualMatch_TichDiem()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_9_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var cust = await SeedCustomerAsync(context, "0900000001");
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0010", CashierId = user.Id, CustomerId = cust.Id, ShiftId = shift.Id, Subtotal = 100000m, Total = 100000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            var bankTx = new BankTransaction { Provider = "SePay", ExternalId = "ext-010", AccountNumber = "", Amount = 100000m, Status = BankTransactionStatus.Unmatched };
            context.Orders.Add(order);
            context.BankTransactions.Add(bankTx);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, NullLogger<BankReconciliationService>.Instance);
            await service.ManualMatchAsync(bankTx.Id, order.Id, "Matched manually", user.Id);

            var updatedOrder = await context.Orders.FirstAsync(o => o.Id == order.Id);
            Assert.Equal(100, updatedOrder.LoyaltyEarned);
        }
    }

    [Fact]
    public async Task ConfirmPaymentAsync_TichDiem()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_10_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var cust = await SeedCustomerAsync(context, "0900000002");
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0011", CashierId = user.Id, CustomerId = cust.Id, ShiftId = shift.Id, Subtotal = 80000m, Total = 80000m, PaymentMethod = PaymentMethod.VietQr, Status = OrderStatus.Pending };
            var payment = new Payment { Order = order, Method = PaymentMethod.VietQr, Amount = 80000m, Status = PaymentStatus.Pending };
            context.Orders.Add(order);
            context.Payments.Add(payment);
            await context.SaveChangesAsync();

            var paymentService = new PaymentService(context);
            await paymentService.ConfirmPaymentAsync(payment.Id, user.Id, PaymentStatus.Paid, "REF11");

            var updatedOrder = await context.Orders.FirstAsync(o => o.Id == order.Id);
            Assert.Equal(80, updatedOrder.LoyaltyEarned);
        }
    }

    [Fact]
    public async Task AutoMatch_CallsNotification_Correctly()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_11_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0012", CashierId = user.Id, ShiftId = shift.Id, Subtotal = 50000m, Total = 50000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var fakeNotifier = new FakeNotificationService();
            var service = new BankReconciliationService(context, NullLogger<BankReconciliationService>.Instance, fakeNotifier);
            await service.ProcessAsync(new[] { new PosApi.Services.IncomingBankTransaction(Guid.NewGuid().ToString(), "", 50000m, "INV-20261125-0012", null, DateTime.UtcNow) });

            Assert.Equal(1, fakeNotifier.PaymentConfirmedCount);
            Assert.Equal("INV-20261125-0012", fakeNotifier.LastOrderCode);
            Assert.Equal(50000m, fakeNotifier.LastAmount);

            await service.ProcessAsync(new[] { new PosApi.Services.IncomingBankTransaction(Guid.NewGuid().ToString(), "", 99999m, "INV-99999999-9999", null, DateTime.UtcNow) });
            Assert.Equal(1, fakeNotifier.PaymentConfirmedCount);
        }
    }

    [Fact]
    public async Task ManualMatch_GoiThongBaoDungMotLan()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_11a_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0012a", CashierId = user.Id, ShiftId = shift.Id, Subtotal = 50000m, Total = 50000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            var bankTx = new BankTransaction { Provider = "SePay", ExternalId = "ext-012a", AccountNumber = "", Amount = 50000m, Status = BankTransactionStatus.Unmatched };
            context.Orders.Add(order);
            context.BankTransactions.Add(bankTx);
            await context.SaveChangesAsync();

            var fakeNotifier = new FakeNotificationService();
            var service = new BankReconciliationService(context, NullLogger<BankReconciliationService>.Instance, fakeNotifier);
            await service.ManualMatchAsync(bankTx.Id, order.Id, "Manual match notification test", user.Id);

            Assert.Equal(1, fakeNotifier.PaymentConfirmedCount);
            Assert.Equal("INV-20261125-0012a", fakeNotifier.LastOrderCode);
            Assert.Equal(50000m, fakeNotifier.LastAmount);
        }
    }

    [Fact]
    public async Task ManualMatch_ThongBaoLoi_DoiSoatVanThanhCong()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_11b_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0012b", CashierId = user.Id, ShiftId = shift.Id, Subtotal = 50000m, Total = 50000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            var bankTx = new BankTransaction { Provider = "SePay", ExternalId = "ext-012b", AccountNumber = "", Amount = 50000m, Status = BankTransactionStatus.Unmatched };
            context.Orders.Add(order);
            context.BankTransactions.Add(bankTx);
            await context.SaveChangesAsync();

            var fakeNotifier = new FakeNotificationService { ThrowOnNotify = true };
            var service = new BankReconciliationService(context, NullLogger<BankReconciliationService>.Instance, fakeNotifier);
            await service.ManualMatchAsync(bankTx.Id, order.Id, "Error notify test", user.Id);

            var completedOrder = await context.Orders.Include(o => o.Payments).FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.Completed, completedOrder.Status);
            Assert.NotEmpty(completedOrder.Payments);

            var resolvedBankTx = await context.BankTransactions.FirstAsync(b => b.Id == bankTx.Id);
            Assert.Equal(BankTransactionStatus.Resolved, resolvedBankTx.Status);
        }
    }

    [Fact]
    public async Task AutoMatch_ThongBaoLoi_DoiSoatVanThanhCong()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_13_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order2 = new Order { Code = "INV-20261125-0013", CashierId = user.Id, ShiftId = shift.Id, Subtotal = 60000m, Total = 60000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.Add(order2);
            await context.SaveChangesAsync();

            var fakeNotifier = new FakeNotificationService { ThrowOnNotify = true };
            var service = new BankReconciliationService(context, NullLogger<BankReconciliationService>.Instance, fakeNotifier);
            await service.ProcessAsync(new[] { new PosApi.Services.IncomingBankTransaction(Guid.NewGuid().ToString(), "", 60000m, "INV-20261125-0013", null, DateTime.UtcNow) });

            var completedOrder2 = await context.Orders.Include(o => o.Payments).FirstAsync(o => o.Id == order2.Id);
            Assert.Equal(OrderStatus.Completed, completedOrder2.Status);
            Assert.NotEmpty(completedOrder2.Payments);

            var matchedBankTx = await context.BankTransactions.FirstAsync(b => b.OrderId == order2.Id);
            Assert.Equal(BankTransactionStatus.Matched, matchedBankTx.Status);
        }
    }

    [Fact]
    public async Task HaiQuay_CaCuCaMoi()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_14_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var counterA = new Counter { Code = "CA", Name = "Quầy A", IsActive = true };
            var counterB = new Counter { Code = "CB", Name = "Quầy B", IsActive = true };
            context.Counters.AddRange(counterA, counterB);
            await context.SaveChangesAsync();

            var shiftB = new Shift { UserId = user.Id, CounterId = counterB.Id, Status = ShiftStatus.Open, OpeningCash = 100m, StartedAt = DateTime.UtcNow };
            var shiftAClosed = new Shift { UserId = user.Id, CounterId = counterA.Id, Status = ShiftStatus.Closed, OpeningCash = 100m, StartedAt = DateTime.UtcNow.AddHours(-3) };
            context.Shifts.AddRange(shiftB, shiftAClosed);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0014", CashierId = user.Id, ShiftId = shiftAClosed.Id, Subtotal = 100000m, Total = 100000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Pending };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var shiftANew = new Shift { UserId = user.Id, CounterId = counterA.Id, Status = ShiftStatus.Open, OpeningCash = 200m, StartedAt = DateTime.UtcNow.AddHours(-1) };
            context.Shifts.Add(shiftANew);
            await context.SaveChangesAsync();

            var service = new BankReconciliationService(context, NullLogger<BankReconciliationService>.Instance);
            await service.ProcessAsync(new[] { new PosApi.Services.IncomingBankTransaction(Guid.NewGuid().ToString(), "", 100000m, "INV-20261125-0014", null, DateTime.UtcNow) });

            var updatedOrder = await context.Orders.FirstAsync(o => o.Id == order.Id);
            Assert.Equal(shiftANew.Id, updatedOrder.ShiftId);
            Assert.NotEqual(shiftB.Id, updatedOrder.ShiftId);
        }
    }

    [Fact]
    public async Task MoCaMoi_KhongGanLaiDonDaCoCa()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_15_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var counter = new Counter { Code = "C4", Name = "Quầy 4", IsActive = true };
            context.Counters.Add(counter);
            await context.SaveChangesAsync();

            var oldClosedShift = new Shift { UserId = user.Id, CounterId = counter.Id, Status = ShiftStatus.Closed, OpeningCash = 100m };
            context.Shifts.Add(oldClosedShift);
            await context.SaveChangesAsync();

            var order = new Order { Code = "INV-20261125-0015", CashierId = user.Id, ShiftId = oldClosedShift.Id, Subtotal = 100000m, Total = 100000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Completed };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var bankTx = new BankTransaction { Provider = "SePay", ExternalId = "ext-015", AccountNumber = "", Amount = 100000m, OrderId = order.Id, Status = BankTransactionStatus.Matched };
            context.BankTransactions.Add(bankTx);
            await context.SaveChangesAsync();

            var shiftService = new ShiftService(context);
            var newShiftRes = await shiftService.OpenShiftAsync(user.Id, 500m, counterId: counter.Id);

            var updatedOrder = await context.Orders.FirstAsync(o => o.Id == order.Id);
            Assert.Equal(oldClosedShift.Id, updatedOrder.ShiftId);
        }
    }

    [Fact]
    public async Task ConfirmPaymentAsync_GanCa_Scenarios()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_16_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var counter = new Counter { Code = "C5", Name = "Quầy 5", IsActive = true };
            context.Counters.Add(counter);
            await context.SaveChangesAsync();

            var openShift = new Shift { UserId = user.Id, CounterId = counter.Id, Status = ShiftStatus.Open, OpeningCash = 100m };
            var closedShift = new Shift { UserId = user.Id, CounterId = counter.Id, Status = ShiftStatus.Closed, OpeningCash = 100m };
            context.Shifts.AddRange(openShift, closedShift);
            await context.SaveChangesAsync();

            var order1 = new Order { Code = "INV-20261125-0016", CashierId = user.Id, ShiftId = openShift.Id, Subtotal = 50000m, Total = 50000m, PaymentMethod = PaymentMethod.VietQr, Status = OrderStatus.Pending };
            var payment1 = new Payment { Order = order1, Method = PaymentMethod.VietQr, Amount = 50000m, Status = PaymentStatus.Pending };
            context.Orders.Add(order1);
            context.Payments.Add(payment1);
            await context.SaveChangesAsync();

            var paymentService = new PaymentService(context);
            await paymentService.ConfirmPaymentAsync(payment1.Id, user.Id, PaymentStatus.Paid, null);
            var updated1 = await context.Orders.FirstAsync(o => o.Id == order1.Id);
            Assert.Equal(openShift.Id, updated1.ShiftId);

            var order2 = new Order { Code = "INV-20261125-0017", CashierId = user.Id, ShiftId = closedShift.Id, Subtotal = 60000m, Total = 60000m, PaymentMethod = PaymentMethod.VietQr, Status = OrderStatus.Pending };
            var payment2 = new Payment { Order = order2, Method = PaymentMethod.VietQr, Amount = 60000m, Status = PaymentStatus.Pending };
            context.Orders.Add(order2);
            context.Payments.Add(payment2);
            await context.SaveChangesAsync();

            await paymentService.ConfirmPaymentAsync(payment2.Id, user.Id, PaymentStatus.Paid, null);
            var updated2 = await context.Orders.FirstAsync(o => o.Id == order2.Id);
            Assert.Equal(openShift.Id, updated2.ShiftId);

            // c) ca tạo đơn đã đóng và KHÔNG có ca nào Open cùng quầy -> ShiftId == null
            var counter6 = new Counter { Code = "C6", Name = "Quầy 6", IsActive = true };
            context.Counters.Add(counter6);
            await context.SaveChangesAsync();
            var closedShift6 = new Shift { UserId = user.Id, CounterId = counter6.Id, Status = ShiftStatus.Closed, OpeningCash = 100m };
            context.Shifts.Add(closedShift6);
            await context.SaveChangesAsync();

            var order3 = new Order { Code = "INV-20261125-0017c", CashierId = user.Id, ShiftId = closedShift6.Id, Subtotal = 70000m, Total = 70000m, PaymentMethod = PaymentMethod.VietQr, Status = OrderStatus.Pending };
            var payment3 = new Payment { Order = order3, Method = PaymentMethod.VietQr, Amount = 70000m, Status = PaymentStatus.Pending };
            context.Orders.Add(order3);
            context.Payments.Add(payment3);
            await context.SaveChangesAsync();

            await paymentService.ConfirmPaymentAsync(payment3.Id, user.Id, PaymentStatus.Paid, null);
            var updated3 = await context.Orders.FirstAsync(o => o.Id == order3.Id);
            Assert.Null(updated3.ShiftId);
        }
    }

    [Fact]
    public async Task OrderService_CashOrder_LoyaltyEarned_Test()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            await EnsureWarehouseAsync(context);
            var user = await SeedUserAsync(context, "u_b10_18_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var cust = await SeedCustomerAsync(context, "0909090909");
            var category = new Category { Name = "Cat1", IsActive = true };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            var product = new Product { Name = "Sp", CategoryId = category.Id, Barcode = "P18", RetailPrice = 250000m, CurrentStock = 10m };
            var unit = new Unit { Name = "cái" };
            context.Shifts.Add(shift);
            context.Products.Add(product);
            context.Units.Add(unit);
            await context.SaveChangesAsync();
            context.ProductUnits.Add(new ProductUnit { ProductId = product.Id, UnitId = unit.Id, ConversionRate = 1m, IsBase = true });
            var wh = await context.Warehouses.FirstAsync(w => w.IsDefault);
            context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m });
            await context.SaveChangesAsync();

            var orderService = new OrderService(context, NullLogger<OrderService>.Instance);
            var req = new CreateOrderRequest(
                CustomerId: cust.Id,
                PaymentMethod: "Cash",
                PaidAmount: 250000m,
                Discount: 0m,
                Surcharges: new List<OrderSurchargeRequest>(),
                Items: new List<OrderItemRequest> { new(ProductId: product.Id, UnitId: unit.Id, Qty: 1m, UnitPrice: 250000m) }
            );

            var res = await orderService.CreateOrderAsync(user.Id, req, null);
            Assert.Equal(250, res.LoyaltyEarned);

            var loyalty = await context.LoyaltyAccounts.FirstAsync(l => l.CustomerId == cust.Id);
            Assert.Equal(250, loyalty.Points);
            Assert.Equal(250000m, loyalty.LifetimeSpend);
        }
    }

    [Fact]
    public async Task OrderService_SmallCashOrder_LoyaltyEarned_Test()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            await EnsureWarehouseAsync(context);
            var user = await SeedUserAsync(context, "u_b10_18b_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var cust = await SeedCustomerAsync(context, "0909090910");
            var category = new Category { Name = "Cat2", IsActive = true };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            var product = new Product { Name = "SpSmall", CategoryId = category.Id, Barcode = "P19", RetailPrice = 500m, CurrentStock = 10m };
            var unit = new Unit { Name = "cái" };
            context.Shifts.Add(shift);
            context.Products.Add(product);
            context.Units.Add(unit);
            await context.SaveChangesAsync();
            context.ProductUnits.Add(new ProductUnit { ProductId = product.Id, UnitId = unit.Id, ConversionRate = 1m, IsBase = true });
            var wh = await context.Warehouses.FirstAsync(w => w.IsDefault);
            context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m });
            await context.SaveChangesAsync();

            var orderService = new OrderService(context, NullLogger<OrderService>.Instance);
            var req = new CreateOrderRequest(
                CustomerId: cust.Id,
                PaymentMethod: "Cash",
                PaidAmount: 500m,
                Discount: 0m,
                Surcharges: new List<OrderSurchargeRequest>(),
                Items: new List<OrderItemRequest> { new(ProductId: product.Id, UnitId: unit.Id, Qty: 1m, UnitPrice: 500m) }
            );

            var res = await orderService.CreateOrderAsync(user.Id, req, null);
            Assert.Equal(0, res.LoyaltyEarned);

            var loyalty = await context.LoyaltyAccounts.FirstAsync(l => l.CustomerId == cust.Id);
            Assert.Equal(0, loyalty.Points);
            Assert.Equal(500m, loyalty.LifetimeSpend);
        }
    }

    [Fact]
    public async Task TongCa_SummaryEqualsCloseShift()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u_b10_19_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var shiftService = new ShiftService(context);
            var shiftRes = await shiftService.OpenShiftAsync(user.Id, 1000m);

            var orderCash = new Order { Code = "INV-20261125-0018", CashierId = user.Id, ShiftId = shiftRes.Id, Subtotal = 100000m, Total = 100000m, PaymentMethod = PaymentMethod.Cash, PaidAmount = 100000m, Status = OrderStatus.Completed };
            var orderTransfer = new Order { Code = "INV-20261125-0019", CashierId = user.Id, ShiftId = shiftRes.Id, Subtotal = 200000m, Total = 200000m, PaymentMethod = PaymentMethod.BankTransfer, Status = OrderStatus.Completed };
            context.Orders.AddRange(orderCash, orderTransfer);
            await context.SaveChangesAsync();

            var summary = await shiftService.GetShiftSummaryAsync(user.Id);
            var closed = await shiftService.CloseShiftAsync(user.Id, 101000m);

            Assert.Equal(summary.CashSales, closed.ExpectedCash - closed.OpeningCash);
            Assert.Equal(summary.TransferSales, closed.TransferSales);
        }
    }
}

public class FakeNotificationService : INotificationService
{
    public int PaymentConfirmedCount { get; private set; }
    public bool ThrowOnNotify { get; set; }
    public string? LastOrderCode { get; private set; }
    public decimal? LastAmount { get; private set; }

    public Task NotifyOrderCreatedAsync(int orderId, string orderCode, decimal total) => Task.CompletedTask;
    public Task NotifyOrderCompletedAsync(int orderId, string orderCode, decimal total) => Task.CompletedTask;
    public Task NotifyPaymentConfirmedAsync(int paymentId, int orderId, string orderCode, decimal amount)
    {
        if (ThrowOnNotify) throw new Exception("Notification failed");
        PaymentConfirmedCount++;
        LastOrderCode = orderCode;
        LastAmount = amount;
        return Task.CompletedTask;
    }
    public Task NotifyLowStockAsync(int productId, string productName, string barcode, decimal stock, decimal minStock) => Task.CompletedTask;
    public Task NotifyShiftOpenedAsync(int shiftId, int userId, string username) => Task.CompletedTask;
    public Task NotifyShiftClosedAsync(int shiftId, int userId, string username, decimal countedCash, decimal diff) => Task.CompletedTask;
}
