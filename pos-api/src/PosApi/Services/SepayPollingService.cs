using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace PosApi.Services;

public class SepayPollingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SepayPollingService> _log;

    public SepayPollingService(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<SepayPollingService> log)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sepayCfg = _configuration.GetSection("SePay");
        var enabled = sepayCfg.GetValue<bool>("Enabled");
        if (!enabled)
        {
            _log.LogInformation("SePay polling service is disabled in configuration.");
            return;
        }

        int intervalSeconds = sepayCfg.GetValue<int>("PollIntervalSeconds");
        if (intervalSeconds <= 0) intervalSeconds = 10;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error occurred during SePay polling loop execution.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var sepayCfg = configuration.GetSection("SePay");
            int lookbackHours = sepayCfg.GetValue<int>("LookbackHours");
            if (lookbackHours <= 0) lookbackHours = 24;

            var provider = scope.ServiceProvider.GetRequiredService<IBankTransactionProvider>();
            var reconciliationService = scope.ServiceProvider.GetRequiredService<BankReconciliationService>();

            var sinceUtc = DateTime.UtcNow.AddHours(-lookbackHours);

            string? accountNumber = null;
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
                var setting = await db.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
                accountNumber = setting?.VietQrAccountNo;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not load StoreSetting for SePay polling account number.");
            }

            var incoming = await provider.GetIncomingAsync(sinceUtc, accountNumber, ct);
            if (incoming.Count > 0)
            {
                await reconciliationService.ProcessAsync(incoming);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error in SepayPollingService.RunOnceAsync");
        }
    }
}
