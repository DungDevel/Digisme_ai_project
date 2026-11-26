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

public class UsersControllerTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public UsersControllerTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    private string GenerateToken(UserRole role, bool isOwner = false, int userId = 999)
    {
        var ticketService = _factory.Services.GetRequiredService<ITicketService>();
        var user = new User
        {
            Id = userId,
            Username = "testuser_" + Guid.NewGuid().ToString("N").Substring(0, 4),
            FullName = "Test User",
            Role = role,
            IsOwner = isOwner
        };
        return ticketService.IssueAccessToken(user);
    }

    [Fact]
    public async Task Anonymous_Post_Users_Returns_Unauthorized()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/users", new
        {
            username = "u1",
            fullName = "U1",
            email = "u1@test.com",
            password = "Password123",
            role = UserRole.Cashier
        });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Cashier_Post_Users_Returns_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Cashier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.PostAsJsonAsync("/api/v1/users", new
        {
            username = "u2",
            fullName = "U2",
            email = "u2@test.com",
            password = "Password123",
            role = UserRole.Cashier
        });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task NonOwner_Manager_Post_Users_Returns_Forbidden()
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

            var resp = await client.PostAsJsonAsync("/api/v1/users", new
            {
                username = "u3",
                fullName = "U3",
                email = "u3@test.com",
                password = "Password123",
                role = UserRole.Cashier
            });
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
    public async Task Accountant_Post_Users_Returns_Forbidden()
    {
        var client = _factory.CreateClient();
        var token = GenerateToken(UserRole.Accountant);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.PostAsJsonAsync("/api/v1/users", new
        {
            username = "u4",
            fullName = "U4",
            email = "u4@test.com",
            password = "Password123",
            role = UserRole.Cashier
        });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Owner_Creates_Accountant_And_Validates_RoundTrip()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        var idsBefore = await db.Users.Select(u => u.Id).ToListAsync();

        var ownerUser = new User
        {
            Username = "owner_test_" + Guid.NewGuid().ToString("N").Substring(0, 4),
            FullName = "Owner Test",
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

            string accUsername = "acc_created_" + Guid.NewGuid().ToString("N").Substring(0, 4);
            string accEmail = accUsername + "@test.com";

            var reqBody = new CreateUserRequest(accUsername, "Accountant Created", accEmail, "Password123", UserRole.Accountant);
            var resp = await client.PostAsJsonAsync("/api/v1/users", reqBody);

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var jsonStr = await resp.Content.ReadAsStringAsync();
            Assert.DoesNotContain("password", jsonStr, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("hash", jsonStr, StringComparison.OrdinalIgnoreCase);

            var createdUserRes = await resp.Content.ReadFromJsonAsync<UserResponse>();
            Assert.NotNull(createdUserRes);
            Assert.Equal(UserRole.Accountant, createdUserRes.Role);

            // Verify new user can login via /api/v1/auth/login and has Role Accountant
            var loginResp = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = accUsername, password = "Password123" });
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
            var authRes = await loginResp.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(authRes);
            Assert.Equal(UserRole.Accountant, authRes.Role);

            // New user calls ManagerOnly endpoint -> 403 Forbidden
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authRes.AccessToken);
            var bankResp = await client.PostAsJsonAsync("/api/v1/bank-transactions/1/match", new { orderId = 1, note = "test" });
            Assert.Equal(HttpStatusCode.Forbidden, bankResp.StatusCode);
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
    public async Task Owner_Creates_Cashier_And_Manager()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        var idsBefore = await db.Users.Select(u => u.Id).ToListAsync();

        var ownerUser = new User
        {
            Username = "owner_cm_" + Guid.NewGuid().ToString("N").Substring(0, 4),
            FullName = "Owner CM",
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

            // Create Cashier
            var resp1 = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequest("cashier_new", "Cashier New", "c_new@test.com", "Password123", UserRole.Cashier));
            Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
            var cashierRes = await resp1.Content.ReadFromJsonAsync<UserResponse>();
            Assert.NotNull(cashierRes);
            Assert.False(cashierRes.IsOwner);

            // Create Manager
            var resp2 = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequest("manager_new", "Manager New", "m_new@test.com", "Password123", UserRole.Manager));
            Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);
            var managerRes = await resp2.Content.ReadFromJsonAsync<UserResponse>();
            Assert.NotNull(managerRes);
            Assert.False(managerRes.IsOwner);

            // Query DB using returned IDs
            var dbCashier = await db.Users.FindAsync(cashierRes.Id);
            Assert.NotNull(dbCashier);
            Assert.False(dbCashier.IsOwner);

            var dbManager = await db.Users.FindAsync(managerRes.Id);
            Assert.NotNull(dbManager);
            Assert.False(dbManager.IsOwner);

            // Log in with the newly created Manager, call POST /api/v1/users -> expect 403 Forbidden
            var loginResp = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "manager_new", password = "Password123" });
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
            var authRes = await loginResp.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(authRes);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authRes.AccessToken);
            var forbiddenResp = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequest("another_user", "Another", "another@test.com", "Password123", UserRole.Cashier));
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenResp.StatusCode);
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
    public async Task Owner_Validation_Errors()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        var idsBefore = await db.Users.Select(u => u.Id).ToListAsync();

        var ownerUser = new User
        {
            Username = "owner_val_" + Guid.NewGuid().ToString("N").Substring(0, 4),
            FullName = "Owner Val",
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

            // Invalid Role (e.g. 99)
            var resp1 = await client.PostAsJsonAsync("/api/v1/users", new { username = "u_inv", fullName = "Inv", email = "inv@test.com", password = "Password123", role = 99 });
            Assert.Equal(HttpStatusCode.BadRequest, resp1.StatusCode);

            // Short password (7 chars)
            var resp2 = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequest("u_short", "Short", "short@test.com", "1234567", UserRole.Cashier));
            Assert.Equal(HttpStatusCode.BadRequest, resp2.StatusCode);

            // Create first user
            var resp3 = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequest("u_dup", "Dup", "dup@test.com", "Password123", UserRole.Cashier));
            Assert.Equal(HttpStatusCode.Created, resp3.StatusCode);

            // Duplicate username
            var resp4 = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequest("u_dup", "Dup2", "dup2@test.com", "Password123", UserRole.Cashier));
            Assert.Equal(HttpStatusCode.BadRequest, resp4.StatusCode);

            // Duplicate email
            var resp5 = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequest("u_dup2", "Dup2", "dup@test.com", "Password123", UserRole.Cashier));
            Assert.Equal(HttpStatusCode.BadRequest, resp5.StatusCode);

            // Duplicate username with leading space (" " + "u_dup") -> 400
            var resp6 = await client.PostAsJsonAsync("/api/v1/users", new CreateUserRequest(" " + "u_dup", "DupWhitespace", "dup_ws@test.com", "Password123", UserRole.Cashier));
            Assert.Equal(HttpStatusCode.BadRequest, resp6.StatusCode);
        }
        finally
        {
            var usersToRemove = await db.Users.Where(u => !idsBefore.Contains(u.Id)).ToListAsync();
            db.Users.RemoveRange(usersToRemove);
            await db.SaveChangesAsync();
            Assert.Equal(idsBefore.Count, await db.Users.CountAsync());
        }
    }
}
