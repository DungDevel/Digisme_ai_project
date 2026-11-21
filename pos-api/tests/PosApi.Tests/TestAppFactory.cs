using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace PosApi.Tests;

/// <summary>
/// Chạy ứng dụng thật (in-process) trỏ sang DB TEST — không đụng digism_pos.
/// DB test: digism_pos_test_http (user pos_user).
/// </summary>
public class TestAppFactory : WebApplicationFactory<Program>
{
    public const string TestDbName = "digism_pos_test_http";
    private static readonly object _dbLock = new();
    private static bool _dbCreated;

    public TestAppFactory()
    {
        lock (_dbLock)
        {
            if (!_dbCreated)
            {
                try
                {
                    var adminCs = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
                    using var adminConn = new NpgsqlConnection(adminCs);
                    adminConn.Open();
                    using var dropCmd = adminConn.CreateCommand();
                    dropCmd.CommandText = "DROP DATABASE IF EXISTS digism_pos_test_http WITH (FORCE);";
                    dropCmd.ExecuteNonQuery();
                    using var createCmd = adminConn.CreateCommand();
                    createCmd.CommandText = "CREATE DATABASE digism_pos_test_http OWNER pos_user;";
                    createCmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"TestAppFactory DB creation error: {ex.Message}");
                }
                _dbCreated = true;
            }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:PosDb",
            $"Host=localhost;Port=5432;Database={TestDbName};Username=pos_user;Password=pos_pass");
    }
}
