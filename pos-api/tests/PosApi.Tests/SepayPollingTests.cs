using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Services;
using Xunit;
using Moq;

namespace PosApi.Tests;

public class SepayPollingTests
{
    private readonly DatabaseFixture _fixture = new();

    private async Task<User> SeedUserAsync(AppDbContext context, string username)
    {
        var user = new User
        {
            Username = username,
            FullName = username,
            Email = username + "@test.com",
            PasswordHash = "hash",
            Role = UserRole.Cashier
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task RunOnceAsync_ProviderThrows_DoesNotThrowOut()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var services = new ServiceCollection();
            var providerMock = new Mock<IBankTransactionProvider>();
            providerMock.Setup(p => p.GetIncomingAsync(It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("API down"));

            services.AddSingleton(providerMock.Object);
            services.AddScoped<BankReconciliationService>();
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger<BankReconciliationService>>(new Mock<ILogger<BankReconciliationService>>().Object);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    {"SePay:Enabled", "true"},
                    {"SePay:ApiToken", "token"},
                    {"SePay:LookbackHours", "24"}
                })
                .Build();

            services.AddSingleton<IConfiguration>(config);
            services.AddSingleton<ILogger<SepayPollingService>>(new Mock<ILogger<SepayPollingService>>().Object);
            services.AddSingleton(sp => context);

            var sp = services.BuildServiceProvider();
            var pollingService = new SepayPollingService(sp, config, new Mock<ILogger<SepayPollingService>>().Object);

            var exception = await Record.ExceptionAsync(() => pollingService.RunOnceAsync(CancellationToken.None));
            Assert.Null(exception);
        }
    }

    [Fact]
    public async Task RunOnceAsync_CallsProviderAndProcessAsync()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "poll_" + Guid.NewGuid().ToString("N").Substring(0,4));
            var shift = new Shift { UserId = user.Id, CounterId = 1, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var order = new Order
            {
                Code = "INV-20261124-0020",
                CashierId = user.Id,
                ShiftId = shift.Id,
                Subtotal = 100000m,
                Total = 100000m,
                PaymentMethod = PaymentMethod.BankTransfer,
                Status = OrderStatus.Pending
            };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var providerMock = new Mock<IBankTransactionProvider>();
            DateTime receivedSinceUtc = default;
            providerMock.Setup(p => p.GetIncomingAsync(It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .Callback<DateTime, string?, CancellationToken>((since, acc, ct) => receivedSinceUtc = since)
                .ReturnsAsync(new[]
                {
                    new IncomingBankTransaction(
                        ExternalId: Guid.NewGuid().ToString(),
                        AccountNumber: "",
                        Amount: 100000m,
                        Content: "INV-20261124-0020",
                        ReferenceCode: "FT999",
                        TransactionDateUtc: DateTime.UtcNow
                    )
                });

            var services = new ServiceCollection();
            services.AddSingleton(providerMock.Object);
            services.AddScoped<BankReconciliationService>();
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger<BankReconciliationService>>(new Mock<ILogger<BankReconciliationService>>().Object);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    {"SePay:Enabled", "true"},
                    {"SePay:ApiToken", "token"},
                    {"SePay:LookbackHours", "12"}
                })
                .Build();

            services.AddSingleton<IConfiguration>(config);
            services.AddSingleton<ILogger<SepayPollingService>>(new Mock<ILogger<SepayPollingService>>().Object);
            services.AddSingleton(sp => context);

            var sp = services.BuildServiceProvider();
            var pollingService = new SepayPollingService(sp, config, new Mock<ILogger<SepayPollingService>>().Object);

            await pollingService.RunOnceAsync(CancellationToken.None);

            var expectedSince = DateTime.UtcNow.AddHours(-12);
            Assert.True((receivedSinceUtc - expectedSince).Duration() < TimeSpan.FromSeconds(5));

            var updatedOrder = await context.Orders.FindAsync(order.Id);
            Assert.Equal(OrderStatus.Completed, updatedOrder!.Status);
        }
    }
}
