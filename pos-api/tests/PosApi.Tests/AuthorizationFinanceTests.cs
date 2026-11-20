using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using PosApi.Domain;
using PosApi.Services;

namespace PosApi.Tests;

public class AuthorizationFinanceTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public AuthorizationFinanceTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    private string GenerateToken(UserRole role)
    {
        var ticketService = _factory.Services.GetRequiredService<ITicketService>();
        var user = new User
        {
            Id = 999,
            Username = role == UserRole.Manager ? "testmgr" : "testcashier",
            FullName = "Test User",
            Role = role
        };
        return ticketService.IssueAccessToken(user);
    }

    [Fact]
    public async Task Unauthenticated_SupplierDebts_Pay_Returns_Unauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/supplier-debts/99999/pay", new { amount = 1000m });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cashier_SupplierDebts_Pay_Returns_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Cashier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/supplier-debts/99999/pay", new { amount = 1000m });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_SupplierDebts_Pay_Not_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Manager);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/supplier-debts/99999/pay", new { amount = 1000m });
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_StockTakes_Create_Returns_Unauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/stock-takes", new { warehouseId = 99999, items = new List<object>() });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cashier_StockTakes_Create_Returns_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Cashier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/stock-takes", new { warehouseId = 99999, items = new List<object>() });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_StockTakes_Create_Not_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Manager);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/stock-takes", new { warehouseId = 99999, items = new List<object>() });
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_CashVouchers_Create_Returns_Unauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/cash-vouchers", new { type = 0, partnerType = 0, partnerId = 99999, amount = 0m });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cashier_CashVouchers_Create_Returns_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Cashier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/cash-vouchers", new { type = 0, partnerType = 0, partnerId = 99999, amount = 0m });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_CashVouchers_Create_Not_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Manager);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/cash-vouchers", new { type = 0, partnerType = 0, partnerId = 99999, amount = 0m });
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
