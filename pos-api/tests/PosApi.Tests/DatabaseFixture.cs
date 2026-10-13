using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using Npgsql;

namespace PosApi.Tests;

/// <summary>
/// Fixture để tạo database options cho test với PostgreSQL thực tế.
/// Sử dụng database đã tồn tại và xóa data giữa các test.
/// Sử dụng Transaction để rollback sau mỗi test.
/// </summary>
public class DatabaseFixture : IDisposable
{
    private readonly string _connectionString;
    private readonly object _lock = new();

    public DatabaseFixture()
    {
        _connectionString = "Host=localhost;Port=5432;Database=digism_pos_test;Username=pos_user;Password=pos_pass";
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_connectionString).Options;
            using var context = new AppDbContext(options);
            context.Database.Migrate();

            if (!context.StoreSettings.Any())
            {
                context.StoreSettings.Add(new StoreSetting
                {
                    StoreName = "Test Store",
                    Address = "Test Address",
                    VatRatePercent = 10m
                });
                context.SaveChanges();
            }
        }
        catch
        {
            // Already migrated
        }
    }

    public string ConnectionString => _connectionString;

    /// <summary>
    /// Tạo một context mới với database đã tồn tại.
    /// Sử dụng transaction để test có thể rollback sau khi hoàn thành.
    /// </summary>
    public (AppDbContext context, NpgsqlTransaction transaction) CreateContextWithTransaction()
    {
        lock (_lock)
        {
            var connection = new NpgsqlConnection(_connectionString + ";Command Timeout=120;");
            connection.Open();

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connection).Options;

            var context = new AppDbContext(options);

            try
            {
                if (!context.StoreSettings.Any())
                {
                    context.StoreSettings.Add(new StoreSetting
                    {
                        StoreName = "Test Store",
                        Address = "Test Address",
                        VatRatePercent = 10m
                    });
                    context.SaveChanges();
                }
            }
            catch
            {
                // Ignore if table doesn't exist yet
            }

            // Sau đó tạo transaction cho các thao tác test
            var transaction = connection.BeginTransaction();
            context.Database.UseTransaction(transaction);

            return (context, transaction);
        }
    }

    /// <summary>
    /// Xóa tất cả data trong các bảng bằng cách dùng TRUNCATE CASCADE.
    /// Không chạy trong transaction của test để tránh làm hỏng transaction nếu có lỗi.
    /// </summary>
    public void ClearData()
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            TRUNCATE TABLE
                ""ReturnItems"", ""Returns"", ""OrderItems"", ""Orders"",
                ""StockLedger"", ""PosLogs"", ""Debts"", ""LoyaltyAccounts"",
                ""Customers"", ""StockInItems"", ""StockIns"", ""SupplierInvoices"",
                ""Payments"", ""Shifts"", ""IdempotencyRecords"", ""invoice_day_counter"",
                ""ProductUnits"", ""Units"", ""Products"", ""Categories"",
                ""StoreSettings"", ""PasswordResetCodes"", ""Users""
            RESTART IDENTITY CASCADE;";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        // Không cần làm gì vì connection được quản lý trong CreateContextWithTransaction
    }
}
