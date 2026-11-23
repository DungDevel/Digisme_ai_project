using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;

namespace PosApi.Tests;

public static class ShiftTestHelper
{
    public static async Task<Shift> EnsureOpenShiftAsync(AppDbContext ctx, int userId, decimal openingCash = 0m)
    {
        var existing = await ctx.Shifts
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Status == ShiftStatus.Open);
        if (existing != null) return existing;

        var shift = new Shift
        {
            UserId = userId,
            OpeningCash = openingCash,
            Status = ShiftStatus.Open,
            StartedAt = DateTime.UtcNow
        };
        ctx.Shifts.Add(shift);
        await ctx.SaveChangesAsync();
        return shift;
    }
}
