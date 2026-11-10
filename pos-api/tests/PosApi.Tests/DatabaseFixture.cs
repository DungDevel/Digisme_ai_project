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
    private static readonly object _migrationLock = new();
    private static bool _migrated;
    private readonly object _lock = new();
    private readonly List<NpgsqlConnection> _connections = new();

    public DatabaseFixture()
    {
        _connectionString = "Host=localhost;Port=5432;Database=digism_pos_test_adr;Username=pos_user;Password=pos_pass";
        lock (_migrationLock)
        {
            try
            {
                var adminCs = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
                using var adminConn = new NpgsqlConnection(adminCs);
                adminConn.Open();
                using var cmd = adminConn.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = 'digism_pos_test_adr';";
                var exists = cmd.ExecuteScalar();
                if (exists == null)
                {
                    using var createCmd = adminConn.CreateCommand();
                    createCmd.CommandText = "CREATE DATABASE digism_pos_test_adr OWNER pos_user;";
                    createCmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DB creation error: {ex.Message}");
            }

            try
            {
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql(_connectionString).Options;
                using var context = new AppDbContext(options);
                context.Database.Migrate();

                context.Database.ExecuteSqlRaw(
                    "ALTER TABLE \"Shifts\" ADD COLUMN IF NOT EXISTS \"TransferSales\" numeric(18,4) NOT NULL DEFAULT 0;" +
                    "ALTER TABLE \"Shifts\" ADD COLUMN IF NOT EXISTS \"CountedTransfer\" numeric(18,4) NULL;" +
                    "ALTER TABLE \"Shifts\" ADD COLUMN IF NOT EXISTS \"TransferDiff\" numeric(18,4) NULL;" +
                    "ALTER TABLE \"Shifts\" ADD COLUMN IF NOT EXISTS \"DenominationsJson\" text NULL;"
                );

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
            catch (Exception ex)
            {
                Console.WriteLine($"MIGRATE ERROR: {ex.Message} --- {ex.InnerException?.Message}");
                throw;
            }
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
            _connections.Add(connection);

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
                ""SupplierReturnItems"", ""SupplierReturns"", ""StockTakeItems"", ""StockTakes"",
                ""WarehouseTransferItems"", ""WarehouseTransfers"", ""WarehouseStocks"", ""Warehouses"",
                ""CashVouchers"", ""CashLedger"", ""SupplierDebts"",
                ""ProductRecipes"", ""Rooms"", ""Tables"", ""Areas"", ""CustomerGroups"",
                ""Suppliers"", ""ReturnItems"", ""Returns"", ""OrderItems"", ""Orders"",
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
        lock (_lock)
        {
            foreach (var conn in _connections)
            {
                try
                {
                    if (conn.State == System.Data.ConnectionState.Open)
                        conn.Close();
                    conn.Dispose();
                }
                catch {}
            }
            _connections.Clear();
        }
    }
}
