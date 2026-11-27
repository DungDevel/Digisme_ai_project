using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class RequireAuthTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public RequireAuthTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    private string GenerateToken(UserRole role)
    {
        var ticketService = _factory.Services.GetRequiredService<ITicketService>();
        var user = new User
        {
            Id = 999,
            Username = "user_" + role.ToString().ToLower() + "_" + Guid.NewGuid().ToString("N").Substring(0, 4),
            FullName = "Test " + role.ToString(),
            Role = role
        };
        return ticketService.IssueAccessToken(user);
    }

    [Fact]
    public async Task Anonymous_Get_Auth_Me_Returns_Unauthorized()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Cashier_Get_Auth_Me_Returns_Ok_With_Username_And_Role()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Cashier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dict = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(dict);
        Assert.Equal("Cashier", dict["role"]?.ToString());
        Assert.NotNull(dict["username"]);
    }

    [Fact]
    public async Task Anonymous_Get_Products_Returns_Unauthorized()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/products");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Get_Products_By_Barcode_Returns_Unauthorized()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/products/barcode/anycode123");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Accountant)]
    public async Task AllRoles_Get_Products_Returns_Ok(UserRole role)
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/v1/products");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Cashier_Get_Products_By_Barcode_Not_Unauthorized_Or_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Cashier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/api/v1/products/barcode/nonexistent999");
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        // 200 or 404 both accepted
        Assert.True(resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Anonymous_Post_Auth_Login_Still_Ok()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin", "admin123"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
