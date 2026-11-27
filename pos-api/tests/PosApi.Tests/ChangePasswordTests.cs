using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class ChangePasswordTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public ChangePasswordTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    private string GenerateToken(User user)
    {
        var ticketService = _factory.Services.GetRequiredService<ITicketService>();
        return ticketService.IssueAccessToken(user);
    }

    [Fact]
    public async Task Anonymous_Post_ChangePassword_Returns_Unauthorized()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest("OldPass1!", "NewPass1!"));
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Authenticated_ChangePassword_Success_And_Login_Works()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();

        string uname = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        string email = uname + "@test.com";
        var user = new User
        {
            Username = uname,
            FullName = "Test Change Pwd",
            Email = email,
            PasswordHash = Hashing.Hash("OldPass1!"),
            Role = UserRole.Cashier
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        try
        {
            var client = _factory.CreateClient();
            var token = GenerateToken(user);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest("OldPass1!", "NewPass2!"));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var loginNew = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(uname, "NewPass2!"));
            Assert.Equal(HttpStatusCode.OK, loginNew.StatusCode);

            var loginOld = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(uname, "OldPass1!"));
            Assert.Equal(HttpStatusCode.Unauthorized, loginOld.StatusCode);
        }
        finally
        {
            var dbUser = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (dbUser != null)
            {
                db.Users.Remove(dbUser);
                await db.SaveChangesAsync();
            }
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
        }
    }

    [Fact]
    public async Task ChangePassword_Wrong_CurrentPassword_Fails()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();

        string uname = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        string email = uname + "@test.com";
        var user = new User
        {
            Username = uname,
            FullName = "Test Wrong Current",
            Email = email,
            PasswordHash = Hashing.Hash("OldPass1!"),
            Role = UserRole.Cashier
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        try
        {
            var client = _factory.CreateClient();
            var token = GenerateToken(user);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest("WrongPass1!", "NewPass2!"));
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            var err = await resp.Content.ReadFromJsonAsync<Dictionary<string, object>>();
            Assert.NotNull(err);
            Assert.Contains("Mật khẩu hiện tại không đúng", err["error"]?.ToString());

            var loginOld = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(uname, "OldPass1!"));
            Assert.Equal(HttpStatusCode.OK, loginOld.StatusCode);
        }
        finally
        {
            var dbUser = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (dbUser != null)
            {
                db.Users.Remove(dbUser);
                await db.SaveChangesAsync();
            }
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
        }
    }

    [Fact]
    public async Task ChangePassword_Weak_NewPassword_Fails()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();

        string uname = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        string email = uname + "@test.com";
        var user = new User
        {
            Username = uname,
            FullName = "Test Weak Pwd",
            Email = email,
            PasswordHash = Hashing.Hash("OldPass1!"),
            Role = UserRole.Cashier
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        try
        {
            var client = _factory.CreateClient();
            var token = GenerateToken(user);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest("OldPass1!", "weak"));
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);

            var loginOld = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(uname, "OldPass1!"));
            Assert.Equal(HttpStatusCode.OK, loginOld.StatusCode);
        }
        finally
        {
            var dbUser = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (dbUser != null)
            {
                db.Users.Remove(dbUser);
                await db.SaveChangesAsync();
            }
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
        }
    }

    [Fact]
    public async Task ChangePassword_Same_As_Current_Fails()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();

        string uname = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        string email = uname + "@test.com";
        var user = new User
        {
            Username = uname,
            FullName = "Test Same Pwd",
            Email = email,
            PasswordHash = Hashing.Hash("OldPass1!"),
            Role = UserRole.Cashier
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        try
        {
            var client = _factory.CreateClient();
            var token = GenerateToken(user);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest("OldPass1!", "OldPass1!"));
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            var dbUser = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (dbUser != null)
            {
                db.Users.Remove(dbUser);
                await db.SaveChangesAsync();
            }
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
        }
    }

    [Fact]
    public async Task ChangePassword_Isolation_Between_Users()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();

        string unameA = "usrA_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        string unameB = "usrB_" + Guid.NewGuid().ToString("N").Substring(0, 6);

        var userA = new User { Username = unameA, FullName = "User A", Email = unameA + "@test.com", PasswordHash = Hashing.Hash("PassA1!"), Role = UserRole.Cashier };
        var userB = new User { Username = unameB, FullName = "User B", Email = unameB + "@test.com", PasswordHash = Hashing.Hash("PassB1!"), Role = UserRole.Cashier };
        db.Users.Add(userA);
        db.Users.Add(userB);
        await db.SaveChangesAsync();

        try
        {
            var client = _factory.CreateClient();
            var tokenA = GenerateToken(userA);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

            var resp = await client.PostAsJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest("PassA1!", "NewPassA2!"));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var loginB = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(unameB, "PassB1!"));
            Assert.Equal(HttpStatusCode.OK, loginB.StatusCode);
        }
        finally
        {
            var dbUsers = await db.Users.Where(u => u.Username == unameA || u.Username == unameB).ToListAsync();
            db.Users.RemoveRange(dbUsers);
            await db.SaveChangesAsync();
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
        }
    }

    [Fact]
    public async Task Owner_Reset_User_Password_Success()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();

        string uname = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        var targetUser = new User { Username = uname, FullName = "Target", Email = uname + "@test.com", PasswordHash = Hashing.Hash("OldPass1!"), Role = UserRole.Cashier };
        db.Users.Add(targetUser);

        var ownerUser = new User { Username = "owner_res_" + Guid.NewGuid().ToString("N").Substring(0, 4), FullName = "Owner", Email = Guid.NewGuid().ToString("N").Substring(0, 6) + "@test.com", PasswordHash = Hashing.Hash("Pass1!"), Role = UserRole.Manager, IsOwner = true };
        db.Users.Add(ownerUser);
        await db.SaveChangesAsync();

        try
        {
            var client = _factory.CreateClient();
            var ticketService = _factory.Services.GetRequiredService<ITicketService>();
            var token = ticketService.IssueAccessToken(ownerUser);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync($"/api/v1/users/{targetUser.Id}/reset-password", new ResetUserPasswordRequest("NewAdminPass1!"));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var loginNew = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(uname, "NewAdminPass1!"));
            Assert.Equal(HttpStatusCode.OK, loginNew.StatusCode);
        }
        finally
        {
            var usersToRemove = await db.Users.Where(u => u.Id == targetUser.Id || u.Id == ownerUser.Id).ToListAsync();
            db.Users.RemoveRange(usersToRemove);
            await db.SaveChangesAsync();
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
        }
    }

    [Fact]
    public async Task NonOwner_Reset_User_Password_Forbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();

        string uname = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        var targetUser = new User { Username = uname, FullName = "Target", Email = uname + "@test.com", PasswordHash = Hashing.Hash("OldPass1!"), Role = UserRole.Cashier };
        db.Users.Add(targetUser);

        var cashierUser = new User { Username = "cashier_res_" + Guid.NewGuid().ToString("N").Substring(0, 4), FullName = "Cashier", Email = Guid.NewGuid().ToString("N").Substring(0, 6) + "@test.com", PasswordHash = Hashing.Hash("Pass1!"), Role = UserRole.Cashier, IsOwner = false };
        db.Users.Add(cashierUser);
        await db.SaveChangesAsync();

        try
        {
            var client = _factory.CreateClient();
            var ticketService = _factory.Services.GetRequiredService<ITicketService>();
            var token = ticketService.IssueAccessToken(cashierUser);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync($"/api/v1/users/{targetUser.Id}/reset-password", new ResetUserPasswordRequest("NewAdminPass1!"));
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            var usersToRemove = await db.Users.Where(u => u.Id == targetUser.Id || u.Id == cashierUser.Id).ToListAsync();
            db.Users.RemoveRange(usersToRemove);
            await db.SaveChangesAsync();
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
        }
    }

    [Fact]
    public async Task Owner_Reset_NonExistent_User_NotFound()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();

        var ownerUser = new User { Username = "owner_res_" + Guid.NewGuid().ToString("N").Substring(0, 4), FullName = "Owner", Email = Guid.NewGuid().ToString("N").Substring(0, 6) + "@test.com", PasswordHash = Hashing.Hash("Pass1!"), Role = UserRole.Manager, IsOwner = true };
        db.Users.Add(ownerUser);
        await db.SaveChangesAsync();

        try
        {
            var client = _factory.CreateClient();
            var ticketService = _factory.Services.GetRequiredService<ITicketService>();
            var token = ticketService.IssueAccessToken(ownerUser);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync("/api/v1/users/999999/reset-password", new ResetUserPasswordRequest("NewAdminPass1!"));
            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
        finally
        {
            var dbUsers = await db.Users.Where(u => u.Id == ownerUser.Id).ToListAsync();
            db.Users.RemoveRange(dbUsers);
            await db.SaveChangesAsync();
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
        }
    }

    [Fact]
    public async Task Owner_Reset_Weak_Password_Fails()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();

        string uname = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        var targetUser = new User { Username = uname, FullName = "Target", Email = uname + "@test.com", PasswordHash = Hashing.Hash("OldPass1!"), Role = UserRole.Cashier };
        db.Users.Add(targetUser);

        var ownerUser = new User { Username = "owner_res_" + Guid.NewGuid().ToString("N").Substring(0, 4), FullName = "Owner", Email = Guid.NewGuid().ToString("N").Substring(0, 6) + "@test.com", PasswordHash = Hashing.Hash("Pass1!"), Role = UserRole.Manager, IsOwner = true };
        db.Users.Add(ownerUser);
        await db.SaveChangesAsync();

        try
        {
            var client = _factory.CreateClient();
            var ticketService = _factory.Services.GetRequiredService<ITicketService>();
            var token = ticketService.IssueAccessToken(ownerUser);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync($"/api/v1/users/{targetUser.Id}/reset-password", new ResetUserPasswordRequest("weak"));
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            var usersToRemove = await db.Users.Where(u => u.Id == targetUser.Id || u.Id == ownerUser.Id).ToListAsync();
            db.Users.RemoveRange(usersToRemove);
            await db.SaveChangesAsync();
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
        }
    }
}
