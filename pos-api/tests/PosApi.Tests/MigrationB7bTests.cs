using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PosApi.Data;
using PosApi.Domain;
using Xunit;

namespace PosApi.Tests;

public class MigrationB7bTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _fixture;

    public MigrationB7bTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migration_B7b_Backfill_And_Rollback_Success()
    {
        using var ctx = _fixture.CreateRealContext();
        var migrator = ctx.Database.GetService<IMigrator>();

        string uname1 = "muser1_" + Guid.NewGuid().ToString("N").Substring(0, 4);
        string uname2 = "muser2_" + Guid.NewGuid().ToString("N").Substring(0, 4);

        try
        {
            // 1. Rollback to previous migration: AddSupplierDebtB2
            await migrator.MigrateAsync("AddSupplierDebtB2");

            // 2. Insert test user and 3 shifts without CounterId
            await ctx.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Users"" (""Username"", ""FullName"", ""Email"", ""PasswordHash"", ""Role"", ""IsActive"", ""CreatedAt"")
                  VALUES ({0}, 'User 1', {1}, 'hash', 0, true, NOW()),
                         ({2}, 'User 2', {3}, 'hash', 0, true, NOW())",
                uname1, uname1 + "@test.com", uname2, uname2 + "@test.com");

            var u1Id = await ctx.Users.Where(u => u.Username == uname1).Select(u => u.Id).FirstAsync();
            var u2Id = await ctx.Users.Where(u => u.Username == uname2).Select(u => u.Id).FirstAsync();

            // Insert 3 shifts (2 open, 1 closed) without CounterId
            await ctx.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""Shifts"" (""UserId"", ""StartedAt"", ""Status"", ""OpeningCash"")
                  VALUES ({0}, NOW() - INTERVAL '2 hour', 0, 1000),
                         ({0}, NOW() - INTERVAL '1 hour', 0, 2000),
                         ({1}, NOW() - INTERVAL '5 hour', 1, 1500)",
                u1Id, u2Id);

            // 3. Migrate UP to latest migration
            await migrator.MigrateAsync();

            // 3.1 Verify Shifts.CounterId is NOT NULL via information_schema.columns
            var conn = ctx.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT is_nullable FROM information_schema.columns WHERE table_name = 'Shifts' AND column_name = 'CounterId'";
                var isNullable = (string?)await cmd.ExecuteScalarAsync();
                Assert.Equal("NO", isNullable);
            }

            // 4. Verify all shifts now have CounterId pointing to Counter-Default and NOT NULL
            var defaultCounterId = await ctx.Counters.Where(c => c.IsDefault).Select(c => c.Id).FirstAsync();
            var shifts = await ctx.Shifts.Where(s => s.UserId == u1Id || s.UserId == u2Id).ToListAsync();
            Assert.Equal(3, shifts.Count);
            foreach (var s in shifts)
            {
                Assert.Equal(defaultCounterId, s.CounterId);
            }

            // 5. Test Down and Up again
            await migrator.MigrateAsync("AddSupplierDebtB2");
            await migrator.MigrateAsync();
        }
        finally
        {
            try
            {
                await migrator.MigrateAsync();
                using var cleanupCtx = _fixture.CreateRealContext();
                await cleanupCtx.Database.ExecuteSqlRawAsync(@"
                    DELETE FROM ""Shifts"" WHERE ""UserId"" IN (SELECT ""Id"" FROM ""Users"" WHERE ""Username"" = {0} OR ""Username"" = {1});
                    DELETE FROM ""Users"" WHERE ""Username"" = {0} OR ""Username"" = {1};
                ", uname1, uname2);
            }
            catch { }
        }
    }
}
