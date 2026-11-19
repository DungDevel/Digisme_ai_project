using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using PosApi.Domain;
using PosApi.Services;

namespace PosApi.Tests;

public class WarehouseAuthorizationTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public WarehouseAuthorizationTests(TestAppFactory factory)
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
    public async Task Unauthenticated_Call_Returns_Unauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/supplier-returns", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cashier_Call_SupplierReturns_Create_Returns_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Cashier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/supplier-returns", new { supplierId = 1, warehouseId = 1, items = new List<object>() });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Cashier_Call_WarehouseTransfers_Create_Returns_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Cashier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/warehouse-transfers", new { fromWarehouseId = 1, toWarehouseId = 2, items = new List<object>() });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_Call_SupplierReturns_Create_Not_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Manager);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/supplier-returns", new { supplierId = 1, warehouseId = 1, items = new List<object>() });
        // Manager should not get 401 or 403 (might be 400 Bad Request due to invalid entities, but not Forbidden)
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_Call_WarehouseTransfers_Create_Not_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Manager);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/v1/warehouse-transfers", new { fromWarehouseId = 1, toWarehouseId = 2, items = new List<object>() });
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
