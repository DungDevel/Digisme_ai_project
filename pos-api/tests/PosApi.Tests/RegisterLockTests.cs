using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class RegisterLockTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public RegisterLockTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    private string GenerateToken(UserRole role, bool isOwner = false)
    {
        var ticketService = _factory.Services.GetRequiredService<ITicketService>();
        var user = new User
        {
            Id = 999,
            Username = "testuser_" + Guid.NewGuid().ToString("N").Substring(0, 4),
            FullName = "Test User",
            Role = role,
            IsOwner = isOwner
        };
        return ticketService.IssueAccessToken(user);
    }

    [Fact]
    public async Task Anonymous_Post_Auth_Register_Returns_Unauthorized()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("u1", "U1", "u1@test.com", "Password123"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Cashier_Post_Auth_Register_Returns_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Cashier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("u2", "U2", "u2@test.com", "Password123"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task NonOwner_Manager_Post_Auth_Register_Returns_Forbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        var idsBefore = await db.Users.Select(u => u.Id).ToListAsync();

        var nonOwnerMgr = new User
        {
            Username = "nonowner_mgr_" + Guid.NewGuid().ToString("N").Substring(0, 4),
            FullName = "Non Owner Mgr",
            Email = Guid.NewGuid().ToString("N").Substring(0, 6) + "@test.com",
            PasswordHash = Hashing.Hash("Password123"),
            Role = UserRole.Manager,
            IsOwner = false
        };
        db.Users.Add(nonOwnerMgr);
        await db.SaveChangesAsync();

        try
        {
            var client = _factory.CreateClient();
            var ticketService = _factory.Services.GetRequiredService<ITicketService>();
            var token = ticketService.IssueAccessToken(nonOwnerMgr);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("u3", "U3", "u3@test.com", "Password123"));
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            var usersToRemove = await db.Users.Where(u => !idsBefore.Contains(u.Id)).ToListAsync();
            db.Users.RemoveRange(usersToRemove);
            await db.SaveChangesAsync();
            Assert.Equal(idsBefore.Count, await db.Users.CountAsync());
        }
    }

    [Fact]
    public async Task Accountant_Post_Auth_Register_Returns_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Accountant);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("u4", "U4", "u4@test.com", "Password123"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Owner_Post_Auth_Register_Creates_Cashier()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        var idsBefore = await db.Users.Select(u => u.Id).ToListAsync();

        var ownerUser = new User
        {
            Username = "owner_reg_test_" + Guid.NewGuid().ToString("N").Substring(0, 4),
            FullName = "Owner Reg",
            Email = Guid.NewGuid().ToString("N").Substring(0, 6) + "@test.com",
            PasswordHash = Hashing.Hash("Password123"),
            Role = UserRole.Manager,
            IsOwner = true
        };
        db.Users.Add(ownerUser);
        await db.SaveChangesAsync();

        try
        {
            var client = _factory.CreateClient();
            var ticketService = _factory.Services.GetRequiredService<ITicketService>();
            var token = ticketService.IssueAccessToken(ownerUser);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            string regUsername = "reg_new_" + Guid.NewGuid().ToString("N").Substring(0, 4);
            var resp = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(regUsername, "New Reg", regUsername + "@test.com", "Password123"));

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var authRes = await resp.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(authRes);
            Assert.Equal(UserRole.Cashier, authRes.Role);

            // Check in DB
            var dbUser = await db.Users.FirstOrDefaultAsync(u => u.Username == regUsername);
            Assert.NotNull(dbUser);
            Assert.Equal(UserRole.Cashier, dbUser.Role);
            Assert.False(dbUser.IsOwner);
        }
        finally
        {
            var usersToRemove = await db.Users.Where(u => !idsBefore.Contains(u.Id)).ToListAsync();
            db.Users.RemoveRange(usersToRemove);
            await db.SaveChangesAsync();
            Assert.Equal(idsBefore.Count, await db.Users.CountAsync());
        }
    }

    [Fact]
    public async Task Anonymous_Post_Auth_Login_Not_Locked()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("admin", "admin123"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var authRes = await resp.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(authRes);
        Assert.Equal("admin", authRes.Username);
    }
}
