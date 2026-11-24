using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using PosApi.Domain;
using PosApi.Services;

namespace PosApi.Tests;

public class AuthorizationCounterTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public AuthorizationCounterTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    private string GenerateToken(UserRole role)
    {
        var ticketService = _factory.Services.GetRequiredService<ITicketService>();
        var user = new User
        {
            Id = 999,
            Username = role == UserRole.Manager ? "mgr_auth" : "cashier_auth",
            FullName = "Test User",
            Role = role
        };
        return ticketService.IssueAccessToken(user);
    }

    [Fact]
    public async Task Cashier_GetCounters_Returns200()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Cashier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/counters");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Cashier_PostCounter_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Cashier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/counters", new { code = "C99", name = "Test" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_PostCounter_NotForbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Manager);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var code = "MGR_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        var response = await client.PostAsJsonAsync("/api/v1/counters", new { code, name = "Manager Counter" });
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
