using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class ShiftCounterTests
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

    [Fact]
    public async Task OpenShift_NoCounterId_UsesDefault()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u1_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var service = new ShiftService(context);

            var res = await service.OpenShiftAsync(user.Id, 1000m);
            Assert.NotNull(res);
            var defCounter = await context.Counters.FirstAsync(c => c.IsDefault);
            Assert.Equal(defCounter.Id, res.CounterId);
            Assert.Equal(defCounter.Name, res.CounterName);
        }
    }

    [Fact]
    public async Task OpenShift_WithCounterId_AssignsCounter()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u2_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var counter = new Counter { Code = "C_CUSTOM", Name = "Quầy Custom", IsActive = true };
            context.Counters.Add(counter);
            await context.SaveChangesAsync();

            var service = new ShiftService(context);
            var res = await service.OpenShiftAsync(user.Id, 500m, counterId: counter.Id);

            Assert.NotNull(res);
            Assert.Equal(counter.Id, res.CounterId);
            Assert.Equal("Quầy Custom", res.CounterName);
        }
    }

    [Fact]
    public async Task OpenShift_InactiveCounter_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "u3_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var counter = new Counter { Code = "C_OFF", Name = "Quầy Off", IsActive = false };
            context.Counters.Add(counter);
            await context.SaveChangesAsync();

            var service = new ShiftService(context);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.OpenShiftAsync(user.Id, 500m, counterId: counter.Id));
        }
    }

    [Fact]
    public async Task OpenShift_CounterAlreadyHasOpenShift_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var userA = await SeedUserAsync(context, "ua_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var userB = await SeedUserAsync(context, "ub_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var counter = new Counter { Code = "C_SHARED", Name = "Quầy Shared", IsActive = true };
            context.Counters.Add(counter);
            await context.SaveChangesAsync();

            var service = new ShiftService(context);
            // User A opens shift on counter
            await service.OpenShiftAsync(userA.Id, 100m, counterId: counter.Id);

            // User B tries to open shift on same counter -> throws
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.OpenShiftAsync(userB.Id, 100m, counterId: counter.Id));
        }
    }

    [Fact]
    public async Task OpenShift_SameUserSecondShift_StillThrows()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "usingle_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var c1 = new Counter { Code = "C1_" + Guid.NewGuid().ToString("N").Substring(0, 4), Name = "C1", IsActive = true };
            var c2 = new Counter { Code = "C2_" + Guid.NewGuid().ToString("N").Substring(0, 4), Name = "C2", IsActive = true };
            context.Counters.AddRange(c1, c2);
            await context.SaveChangesAsync();

            var service = new ShiftService(context);
            await service.OpenShiftAsync(user.Id, 100m, counterId: c1.Id);

            // Same user tries to open on c2 -> throws because user already has open shift
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.OpenShiftAsync(user.Id, 100m, counterId: c2.Id));
        }
    }

    [Fact]
    public async Task OpenShift_DeviceFromOtherCounter_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "udev1_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var c1 = new Counter { Code = "C1_" + Guid.NewGuid().ToString("N").Substring(0, 4), Name = "C1", IsActive = true };
            var c2 = new Counter { Code = "C2_" + Guid.NewGuid().ToString("N").Substring(0, 4), Name = "C2", IsActive = true };
            context.Counters.AddRange(c1, c2);
            await context.SaveChangesAsync();

            var devC1 = new PosDevice { CounterId = c1.Id, Code = "DEV1", Name = "Dev 1", IsActive = true };
            context.PosDevices.Add(devC1);
            await context.SaveChangesAsync();

            var service = new ShiftService(context);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.OpenShiftAsync(user.Id, 100m, counterId: c2.Id, posDeviceId: devC1.Id));
        }
    }

    [Fact]
    public async Task OpenShift_InactiveDevice_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "udev2_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var c1 = new Counter { Code = "C1_" + Guid.NewGuid().ToString("N").Substring(0, 4), Name = "C1", IsActive = true };
            context.Counters.Add(c1);
            await context.SaveChangesAsync();

            var inactiveDev = new PosDevice { CounterId = c1.Id, Code = "DEV_OFF", Name = "Inactive Dev", IsActive = false };
            context.PosDevices.Add(inactiveDev);
            await context.SaveChangesAsync();

            var service = new ShiftService(context);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.OpenShiftAsync(user.Id, 100m, counterId: c1.Id, posDeviceId: inactiveDev.Id));
        }
    }

    [Fact]
    public async Task OpenShift_ReturnsCounterAndDeviceInDto()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "udto_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var counter = new Counter { Code = "C_DTO", Name = "Quầy DTO", IsActive = true };
            context.Counters.Add(counter);
            await context.SaveChangesAsync();

            var device = new PosDevice { CounterId = counter.Id, Code = "POS_DTO", Name = "Máy POS DTO", IsActive = true };
            context.PosDevices.Add(device);
            await context.SaveChangesAsync();

            var service = new ShiftService(context);
            var res = await service.OpenShiftAsync(user.Id, 300m, counterId: counter.Id, posDeviceId: device.Id);

            Assert.NotNull(res);
            Assert.Equal(counter.Id, res.CounterId);
            Assert.Equal("Quầy DTO", res.CounterName);
            Assert.Equal(device.Id, res.PosDeviceId);
        }
    }

    [Fact]
    public async Task OpenShift_AfterCounterShiftClosed_CanOpenAgain()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var userA = await SeedUserAsync(context, "uclose1_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var userB = await SeedUserAsync(context, "uclose2_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var counter = new Counter { Code = "C_REOPEN", Name = "Quầy Reopen", IsActive = true };
            context.Counters.Add(counter);
            await context.SaveChangesAsync();

            var service = new ShiftService(context);
            await service.OpenShiftAsync(userA.Id, 100m, counterId: counter.Id);

            // Close userA's shift
            await service.CloseShiftAsync(userA.Id, 100m);

            // Now userB can open shift on same counter
            var res = await service.OpenShiftAsync(userB.Id, 200m, counterId: counter.Id);
            Assert.NotNull(res);
            Assert.Equal(counter.Id, res.CounterId);
        }
    }

    [Fact]
    public async Task OpenShift_NoDefaultCounter_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = await SeedUserAsync(context, "unodefault_" + Guid.NewGuid().ToString("N").Substring(0, 4));
            var defCounters = await context.Counters.Where(c => c.IsDefault).ToListAsync();
            foreach (var dc in defCounters)
            {
                dc.IsDefault = false;
            }
            await context.SaveChangesAsync();

            var service = new ShiftService(context);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.OpenShiftAsync(user.Id, 100m));
            Assert.Contains("quầy mặc định", ex.Message);
        }
    }
}
