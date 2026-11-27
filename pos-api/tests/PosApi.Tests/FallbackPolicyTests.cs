using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using PosApi.Dto;
using Xunit;

namespace PosApi.Tests;

public class FallbackPolicyTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public FallbackPolicyTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FallbackPolicy_Is_Configured_With_RequireAuthenticatedUser()
    {
        using var scope = _factory.Services.CreateScope();
        var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var fallbackPolicy = await policyProvider.GetFallbackPolicyAsync();

        Assert.NotNull(fallbackPolicy);
        Assert.Contains(fallbackPolicy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task Anonymous_Post_Auth_Login_With_Wrong_Password_Returns_401_With_Specific_Error()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("nonexistent_" + Guid.NewGuid().ToString("N").Substring(0, 4), "WrongPassword123"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        Assert.NotNull(json);
        var errStr = json["error"]?.ToString();
        Assert.NotNull(errStr);
        Assert.Contains("Sai tên đăng nhập hoặc mật khẩu", errStr);
    }

    [Fact]
    public async Task Anonymous_Get_Health_Returns_200()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Get_Health_Ready_Not_Unauthorized_Or_Forbidden()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/health/ready");
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.True(resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Anonymous_Post_Auth_Forgot_Password_Not_Unauthorized_Or_Forbidden()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("nonexistent@test.com"));
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.True(resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Anonymous_Post_Auth_Reset_Password_Not_Unauthorized_Or_Forbidden()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest("nonexistent@test.com", "123456", "NewPassword123"));
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        Assert.True(resp.StatusCode == HttpStatusCode.OK || resp.StatusCode == HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Anonymous_Get_Missing_Permission_Endpoint_Returns_Unauthorized()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/api/v1/orders");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
