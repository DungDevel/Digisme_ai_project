using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;

namespace PosApi.Tests;

public static class ShiftTestHelper
{
    public static async Task<int> GetDefaultCounterIdAsync(AppDbContext ctx)
    {
        var defaultCounter = await ctx.Counters.FirstOrDefaultAsync(c => c.IsDefault);
        if (defaultCounter != null) return defaultCounter.Id;

        defaultCounter = new Counter
        {
            Code = "DEFAULT",
            Name = "Quầy mặc định",
            IsDefault = true,
            IsActive = true
        };
        ctx.Counters.Add(defaultCounter);
        await ctx.SaveChangesAsync();
        return defaultCounter.Id;
    }

    public static async Task<Shift> EnsureOpenShiftAsync(AppDbContext ctx, int userId, decimal openingCash = 0m)
    {
        var existing = await ctx.Shifts
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);
        if (existing != null) return existing;

        var counterId = await GetDefaultCounterIdAsync(ctx);

        var shift = new Shift
        {
            UserId = userId,
            CounterId = counterId,
            OpeningCash = openingCash,
            Status = ShiftStatus.Open,
            StartedAt = DateTime.UtcNow
        };
        ctx.Shifts.Add(shift);
        await ctx.SaveChangesAsync();
        return shift;
    }
}
