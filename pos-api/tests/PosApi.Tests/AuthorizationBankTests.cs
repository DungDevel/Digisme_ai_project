using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class AuthorizationBankTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public AuthorizationBankTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    private string GenerateToken(UserRole role)
    {
        var ticketService = _factory.Services.GetRequiredService<ITicketService>();
        var user = new User
        {
            Id = 999,
            Username = role == UserRole.Manager ? "mgr_bank" : "cashier_bank",
            FullName = "Test User",
            Role = role
        };
        return ticketService.IssueAccessToken(user);
    }

    [Fact]
    public async Task Cashier_Forbidden_OnBankTransactions()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Cashier);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/bank-transactions");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_Success_OnBankTransactions()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Manager);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/bank-transactions");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
