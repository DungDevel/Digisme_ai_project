using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class CounterTests
{
    private readonly DatabaseFixture _fixture = new();

    [Fact]
    public async Task CreateCounter_Success_And_DuplicateCode_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CounterService(context);
            var code = "c_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            var req = new CreateCounterRequest(code, "Quầy A");
            var res = await service.CreateCounterAsync(req);

            Assert.NotNull(res);
            Assert.Equal(code.ToUpperInvariant(), res.Code);
            Assert.False(res.IsDefault);
            Assert.True(res.IsActive);

            await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateCounterAsync(req));
        }
    }

    [Fact]
    public async Task DeactivateCounter_Default_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var def = await context.Counters.FirstOrDefaultAsync(c => c.IsDefault);
            if (def == null)
            {
                def = new Counter { Code = "DEFAULT", Name = "Quầy mặc định", IsDefault = true, IsActive = true };
                context.Counters.Add(def);
                await context.SaveChangesAsync();
            }
            Assert.True(def.IsDefault);

            var service = new CounterService(context);

            await Assert.ThrowsAsync<BusinessRuleException>(() => service.DeactivateCounterAsync(def.Id));
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateCounterAsync(def.Id, new UpdateCounterRequest("Test", false)));
        }
    }

    [Fact]
    public async Task DeactivateCounter_WithActiveDeviceOrOpenShift_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CounterService(context);
            var c = await service.CreateCounterAsync(new CreateCounterRequest("c_test1", "Test Counter"));

            var user = new User { Username = "u_" + Guid.NewGuid().ToString("N").Substring(0, 6), FullName = "U", Email = Guid.NewGuid().ToString("N") + "@test.com" };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var dev = new PosDevice { CounterId = c.Id, Code = "d_test1", Name = "Device 1", IsActive = true };
            context.PosDevices.Add(dev);
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<BusinessRuleException>(() => service.DeactivateCounterAsync(c.Id));

            context.PosDevices.Remove(dev);
            await context.SaveChangesAsync();

            var shift = new Shift { UserId = user.Id, CounterId = c.Id, Status = ShiftStatus.Open, OpeningCash = 100m };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            await Assert.ThrowsAsync<BusinessRuleException>(() => service.DeactivateCounterAsync(c.Id));
        }
    }

    [Fact]
    public async Task MaxCounters_Limit_Enforced_And_InactiveIgnored()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var setting = await context.StoreSettings.FirstAsync();
            var initialActiveCount = await context.Counters.CountAsync(c => c.IsActive);
            setting.MaxCounters = initialActiveCount + 1;
            await context.SaveChangesAsync();

            var service = new CounterService(context);
            var c1 = await service.CreateCounterAsync(new CreateCounterRequest("c1", "Counter 1"));

            // Next counter should exceed limit (initialActiveCount + 1)
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateCounterAsync(new CreateCounterRequest("c2", "Counter 2")));

            // Deactivate c1, now should be allowed
            await service.DeactivateCounterAsync(c1.Id);
            var c2 = await service.CreateCounterAsync(new CreateCounterRequest("c2", "Counter 2"));
            Assert.NotNull(c2);
        }
    }
}
