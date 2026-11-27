using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class ResetCodeExposureTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public ResetCodeExposureTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Default_ExposeResetCode_False_Forgot_Password_Does_Not_Return_Code()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();
        var codeCountBefore = await db.PasswordResetCodes.CountAsync();

        string uniqueEmail = "test_reset_" + Guid.NewGuid().ToString("N") + "@test.com";
        string uniqueUser = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 6);

        var testUser = new User
        {
            Username = uniqueUser,
            FullName = "Reset User",
            Email = uniqueEmail,
            PasswordHash = Hashing.Hash("Password123"),
            Role = UserRole.Cashier
        };
        db.Users.Add(testUser);
        await db.SaveChangesAsync();

        try
        {
            var client = _factory.CreateClient();
            var resp = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(uniqueEmail));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var fr = await resp.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
            Assert.NotNull(fr);
            Assert.NotNull(fr.Message);
            Assert.False(Regex.IsMatch(fr.Message, @"\d{6}"), $"Message should not expose 6-digit code: {fr.Message}");
        }
        finally
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == uniqueEmail);
            if (user != null)
            {
                var codes = await db.PasswordResetCodes.Where(c => c.UserId == user.Id).ToListAsync();
                db.PasswordResetCodes.RemoveRange(codes);
                db.Users.Remove(user);
                await db.SaveChangesAsync();
            }
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
            Assert.Equal(codeCountBefore, await db.PasswordResetCodes.CountAsync());
        }
    }

    [Fact]
    public async Task Default_ExposeResetCode_False_Forgot_Password_Does_Not_Create_Reset_Code_In_Db()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();
        var codeCountBefore = await db.PasswordResetCodes.CountAsync();

        string uniqueEmail = "test_nocode_" + Guid.NewGuid().ToString("N") + "@test.com";
        string uniqueUser = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 6);

        var testUser = new User
        {
            Username = uniqueUser,
            FullName = "No Code User",
            Email = uniqueEmail,
            PasswordHash = Hashing.Hash("Password123"),
            Role = UserRole.Cashier
        };
        db.Users.Add(testUser);
        await db.SaveChangesAsync();

        try
        {
            int codesBeforeForgot = await db.PasswordResetCodes.CountAsync(c => c.UserId == testUser.Id);
            Assert.Equal(0, codesBeforeForgot);

            var client = _factory.CreateClient();
            var resp = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(uniqueEmail));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            int codesAfterForgot = await db.PasswordResetCodes.CountAsync(c => c.UserId == testUser.Id);
            Assert.Equal(0, codesAfterForgot);
        }
        finally
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == uniqueEmail);
            if (user != null)
            {
                var codes = await db.PasswordResetCodes.Where(c => c.UserId == user.Id).ToListAsync();
                db.PasswordResetCodes.RemoveRange(codes);
                db.Users.Remove(user);
                await db.SaveChangesAsync();
            }
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
            Assert.Equal(codeCountBefore, await db.PasswordResetCodes.CountAsync());
        }
    }

    [Fact]
    public async Task Default_ExposeResetCode_False_Forgot_Password_Message_Identical_For_Existing_And_NonExisting()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();
        var codeCountBefore = await db.PasswordResetCodes.CountAsync();

        string uniqueEmail = "test_reset_exist_" + Guid.NewGuid().ToString("N") + "@test.com";
        string nonExistingEmail = "nonexistent_" + Guid.NewGuid().ToString("N") + "@test.com";

        var testUser = new User
        {
            Username = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 6),
            FullName = "Exist User",
            Email = uniqueEmail,
            PasswordHash = Hashing.Hash("Password123"),
            Role = UserRole.Cashier
        };
        db.Users.Add(testUser);
        await db.SaveChangesAsync();

        try
        {
            var client = _factory.CreateClient();
            var respExist = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(uniqueEmail));
            var respNonExist = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(nonExistingEmail));

            Assert.Equal(HttpStatusCode.OK, respExist.StatusCode);
            Assert.Equal(HttpStatusCode.OK, respNonExist.StatusCode);

            var frExist = await respExist.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
            var frNonExist = await respNonExist.Content.ReadFromJsonAsync<ForgotPasswordResponse>();

            Assert.NotNull(frExist);
            Assert.NotNull(frNonExist);
            Assert.Equal(frExist.Message, frNonExist.Message);
        }
        finally
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == uniqueEmail);
            if (user != null)
            {
                var codes = await db.PasswordResetCodes.Where(c => c.UserId == user.Id).ToListAsync();
                db.PasswordResetCodes.RemoveRange(codes);
                db.Users.Remove(user);
                await db.SaveChangesAsync();
            }
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
            Assert.Equal(codeCountBefore, await db.PasswordResetCodes.CountAsync());
        }
    }

    [Fact]
    public async Task Default_ExposeResetCode_False_Reset_Password_NonExisting_Email_Returns_Same_Error()
    {
        var client = _factory.CreateClient();
        var respNonExist = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest("nonexistent_" + Guid.NewGuid() + "@test.com", "123456", "NewPassword123"));
        Assert.Equal(HttpStatusCode.BadRequest, respNonExist.StatusCode);
        var errNonExist = await respNonExist.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        Assert.NotNull(errNonExist);
        var msg = errNonExist["error"]?.ToString();
        Assert.Equal("Mã không hợp lệ hoặc đã hết hạn", msg);
    }

    [Fact]
    public async Task ExposeResetCode_True_Forgot_Password_Returns_Code_And_Allows_Reset_And_Login()
    {
        var exposedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:ExposeResetCode", "true");
        });

        using var scope = exposedFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();
        var codeCountBefore = await db.PasswordResetCodes.CountAsync();

        string uniqueEmail = "test_exp_" + Guid.NewGuid().ToString("N") + "@test.com";
        string uniqueUser = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 6);

        var testUser = new User
        {
            Username = uniqueUser,
            FullName = "Expose User",
            Email = uniqueEmail,
            PasswordHash = Hashing.Hash("Password123"),
            Role = UserRole.Cashier
        };
        db.Users.Add(testUser);
        await db.SaveChangesAsync();

        try
        {
            var client = exposedFactory.CreateClient();
            var resp = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(uniqueEmail));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var fr = await resp.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
            Assert.NotNull(fr);
            Assert.NotNull(fr.Message);
            Assert.Matches(@"^\d{6}$", fr.Message);
            string code = fr.Message;

            var resetResp = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(uniqueEmail, code, "NewPassword123!"));
            Assert.Equal(HttpStatusCode.OK, resetResp.StatusCode);

            var loginResp = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(uniqueUser, "NewPassword123!"));
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        }
        finally
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == uniqueEmail);
            if (user != null)
            {
                var codes = await db.PasswordResetCodes.Where(c => c.UserId == user.Id).ToListAsync();
                db.PasswordResetCodes.RemoveRange(codes);
                db.Users.Remove(user);
                await db.SaveChangesAsync();
            }
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
            Assert.Equal(codeCountBefore, await db.PasswordResetCodes.CountAsync());
        }
    }

    [Fact]
    public async Task ExposeResetCode_True_Wrong_Code_Fails_And_Old_Password_Still_Works()
    {
        var exposedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:ExposeResetCode", "true");
        });

        using var scope = exposedFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userCountBefore = await db.Users.CountAsync();
        var codeCountBefore = await db.PasswordResetCodes.CountAsync();

        string uniqueEmail = "test_wrong_" + Guid.NewGuid().ToString("N") + "@test.com";
        string uniqueUser = "usr_" + Guid.NewGuid().ToString("N").Substring(0, 6);

        var testUser = new User
        {
            Username = uniqueUser,
            FullName = "Wrong Code User",
            Email = uniqueEmail,
            PasswordHash = Hashing.Hash("Password123"),
            Role = UserRole.Cashier
        };
        db.Users.Add(testUser);
        await db.SaveChangesAsync();

        try
        {
            var client = exposedFactory.CreateClient();
            var resp = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest(uniqueEmail));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

            var fr = await resp.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
            Assert.NotNull(fr);
            string realCode = fr.Message;
            char lastChar = realCode[5];
            char wrongLastChar = lastChar == '0' ? '1' : (char)(lastChar - 1);
            string wrongCode = realCode.Substring(0, 5) + wrongLastChar;

            var resetResp = await client.PostAsJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(uniqueEmail, wrongCode, "NewPassword123!"));
            Assert.Equal(HttpStatusCode.BadRequest, resetResp.StatusCode);

            var loginResp = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(uniqueUser, "Password123"));
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
        }
        finally
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == uniqueEmail);
            if (user != null)
            {
                var codes = await db.PasswordResetCodes.Where(c => c.UserId == user.Id).ToListAsync();
                db.PasswordResetCodes.RemoveRange(codes);
                db.Users.Remove(user);
                await db.SaveChangesAsync();
            }
            Assert.Equal(userCountBefore, await db.Users.CountAsync());
            Assert.Equal(codeCountBefore, await db.PasswordResetCodes.CountAsync());
        }
    }
}
