using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace PosApi.Tests;

/// <summary>
/// Chạy ứng dụng thật (in-process) trỏ sang DB TEST — không đụng digism_pos.
/// DB test: digism_pos_test (user pos_user). Fixture TestDbFixture reset + migrate + seed.
/// </summary>
public class TestAppFactory : WebApplicationFactory<Program>
{
    public const string TestDbName = "digism_pos_test";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:PosDb",
            $"Host=localhost;Port=5432;Database={TestDbName};Username=pos_user;Password=pos_pass");
    }
}
