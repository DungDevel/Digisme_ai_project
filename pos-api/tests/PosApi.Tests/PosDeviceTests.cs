using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class PosDeviceTests
{
    private readonly DatabaseFixture _fixture = new();

    [Fact]
    public async Task CreatePosDevice_Success_And_NonExistentCounter_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new PosDeviceService(context);
            var counterService = new CounterService(context);

            var counter = await counterService.CreateCounterAsync(new CreateCounterRequest("c_dev", "Counter Dev"));

            var code = "dev_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var res = await service.CreatePosDeviceAsync(new CreatePosDeviceRequest(counter.Id, code, "POS 1"));

            Assert.NotNull(res);
            Assert.Equal(counter.Id, res.CounterId);
            Assert.Equal(code.ToUpperInvariant(), res.Code);

            // Non-existent counter
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreatePosDeviceAsync(new CreatePosDeviceRequest(99999, "dev_2", "POS 2")));
        }
    }

    [Fact]
    public async Task MaxPosDevices_Limit_Enforced()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var setting = await context.StoreSettings.FirstAsync();
            var initialCount = await context.PosDevices.CountAsync(d => d.IsActive);
            setting.MaxPosDevices = initialCount + 1;
            await context.SaveChangesAsync();

            var service = new PosDeviceService(context);
            var defCounter = await context.Counters.FirstAsync(c => c.IsDefault);

            var d1 = await service.CreatePosDeviceAsync(new CreatePosDeviceRequest(defCounter.Id, "d1", "Device 1"));
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreatePosDeviceAsync(new CreatePosDeviceRequest(defCounter.Id, "d2", "Device 2")));
        }
    }

    [Fact]
    public async Task MaxPosDevices_Null_NotBlocked()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var setting = await context.StoreSettings.FirstAsync();
            setting.MaxPosDevices = null;
            await context.SaveChangesAsync();

            var service = new PosDeviceService(context);
            var defCounter = await context.Counters.FirstAsync(c => c.IsDefault);

            var d1 = await service.CreatePosDeviceAsync(new CreatePosDeviceRequest(defCounter.Id, "d_null1", "Device 1"));
            var d2 = await service.CreatePosDeviceAsync(new CreatePosDeviceRequest(defCounter.Id, "d_null2", "Device 2"));
            Assert.NotNull(d1);
            Assert.NotNull(d2);
        }
    }

    [Fact]
    public async Task PosDevice_InactiveNotCountedInLimit()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var setting = await context.StoreSettings.FirstAsync();
            setting.MaxPosDevices = 1;
            await context.SaveChangesAsync();

            var service = new PosDeviceService(context);
            var defCounter = await context.Counters.FirstAsync(c => c.IsDefault);

            var d1 = await service.CreatePosDeviceAsync(new CreatePosDeviceRequest(defCounter.Id, "d_inact1", "Device 1", IsActive: false));
            var d2 = await service.CreatePosDeviceAsync(new CreatePosDeviceRequest(defCounter.Id, "d_act1", "Device 2", IsActive: true));
            Assert.NotNull(d2);
        }
    }
}
