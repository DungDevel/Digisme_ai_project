using System.Security.Claims;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class AccessMatrixTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public AccessMatrixTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    private static string FindBaselinePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var csproj = Path.Combine(dir.FullName, "PosApi.Tests.csproj");
            if (File.Exists(csproj))
            {
                return Path.Combine(dir.FullName, "access-matrix.baseline.csv");
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Không tìm thấy thư mục chứa PosApi.Tests.csproj để xác định đường dẫn access-matrix.baseline.csv.");
    }

    [Fact]
    public async Task AccessMatrix_MatchesBaseline()
    {
        using var scope = _factory.Services.CreateScope();
        var endpoints = scope.ServiceProvider.GetRequiredService<EndpointDataSource>();
        var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var routeEndpoints = endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.OfType<ControllerActionDescriptor>().FirstOrDefault() != null)
            .ToList();

        var matrixRows = new List<(string Method, string Route, string Anonymous, string Cashier, string Manager, string Owner, string Accountant, string Note)>();

        foreach (var ep in routeEndpoints.OrderBy(e => e.RoutePattern.RawText).ThenBy(e => e.Metadata.OfType<HttpMethodMetadata>().FirstOrDefault()?.HttpMethods.FirstOrDefault() ?? "GET"))
        {
            var actionDesc = ep.Metadata.OfType<ControllerActionDescriptor>().FirstOrDefault()!;
            var httpMethod = ep.Metadata.OfType<HttpMethodMetadata>().FirstOrDefault()?.HttpMethods.FirstOrDefault() ?? "GET";
            var rawRoute = ep.RoutePattern.RawText ?? "";

            var route = "/" + rawRoute.TrimStart('/');

            var authorizeData = ep.Metadata.OfType<IAuthorizeData>().ToList();
            var allowAnonymous = ep.Metadata.OfType<IAllowAnonymous>().FirstOrDefault() != null;

            async Task<string> CheckAccessAsync(ClaimsPrincipal principal)
            {
                if (allowAnonymous) return "Y";
                if (!authorizeData.Any()) return "Y";

                var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData);
                if (policy == null) return "Y";

                var result = await authService.AuthorizeAsync(principal, policy);
                return result.Succeeded ? "Y" : "N";
            }

            var anonPrincipal = new ClaimsPrincipal(new ClaimsIdentity());
            var cashierPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Cashier") }, "TestAuth"));
            var managerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "2"), new Claim(ClaimTypes.Role, "Manager") }, "TestAuth"));
            var ownerPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "3"), new Claim(ClaimTypes.Role, "Manager") }, "TestAuth"));
            var accountantPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "4"), new Claim(ClaimTypes.Role, "Accountant") }, "TestAuth"));

            var canAnon = await CheckAccessAsync(anonPrincipal);
            var canCashier = await CheckAccessAsync(cashierPrincipal);
            var canManager = await CheckAccessAsync(managerPrincipal);
            var canOwner = await CheckAccessAsync(ownerPrincipal);
            var canAccountant = await CheckAccessAsync(accountantPrincipal);

            string note = "";
            if (route.Contains("stock-take-thresholds") && httpMethod == "PUT")
            {
                note = "owner-check-in-service (SettingsController.cs:98)";
            }

            matrixRows.Add((httpMethod, route, canAnon, canCashier, canManager, canOwner, canAccountant, note));
        }

        Assert.Equal(144, matrixRows.Count);

        var baselinePath = FindBaselinePath();
        bool updateBaseline = Environment.GetEnvironmentVariable("UPDATE_ACCESS_BASELINE") == "1";

        if (updateBaseline)
        {
            var lines = new List<string> { "Method,Route,Anonymous,Cashier,Manager,Owner,Accountant,Note" };
            foreach (var r in matrixRows)
            {
                lines.Add($"{r.Method},{r.Route},{r.Anonymous},{r.Cashier},{r.Manager},{r.Owner},{r.Accountant},{r.Note}");
            }
            File.WriteAllLines(baselinePath, lines);
        }
        else if (!File.Exists(baselinePath))
        {
            Assert.Fail("access-matrix.baseline.csv không tồn tại. Chạy với UPDATE_ACCESS_BASELINE=1 để tạo, rồi commit file này.");
        }

        var rawLines = File.ReadAllLines(baselinePath);
        Assert.NotEmpty(rawLines);
        Assert.Equal("Method,Route,Anonymous,Cashier,Manager,Owner,Accountant,Note", rawLines[0].Trim());

        var seenPairs = new HashSet<(string, string)>();
        for (int lineIdx = 1; lineIdx < rawLines.Length; lineIdx++)
        {
            var line = rawLines[lineIdx];
            var parts = line.Split(',');
            if (parts.Length < 7)
            {
                Assert.Fail($"Dòng {lineIdx + 1} trong baseline CSV không đủ cột.");
            }
            var method = parts[0];
            var route = parts[1];
            if (!seenPairs.Add((method, route)))
            {
                Assert.Fail($"Dòng {lineIdx + 1} trùng lặp (Method, Route): ({method}, {route}).");
            }

            for (int col = 2; col <= 6; col++)
            {
                var val = parts[col];
                if (val != "Y" && val != "N")
                {
                    Assert.Fail($"Dòng {lineIdx + 1} cột {col} có giá trị không hợp lệ '{val}' (chỉ chấp nhận Y hoặc N).");
                }
            }
        }

        var baselineLines = rawLines.Skip(1).ToList();
        Assert.Equal(matrixRows.Count, baselineLines.Count);

        for (int i = 0; i < matrixRows.Count; i++)
        {
            var r = matrixRows[i];
            var parts = baselineLines[i].Split(',');
            Assert.Equal(r.Method, parts[0]);
            Assert.Equal(r.Route, parts[1]);
            Assert.Equal(r.Anonymous, parts[2]);
            Assert.Equal(r.Cashier, parts[3]);
            Assert.Equal(r.Manager, parts[4]);
            Assert.Equal(r.Owner, parts[5]);
            Assert.Equal(r.Accountant, parts[6]);
        }
    }

    [Fact]
    public async Task AccessMatrix_OwnerColumn_IsDocumented()
    {
        // Vì Owner là cờ đọc từ DB trong service (không phải policy), cột Owner luôn bằng Manager trong ma trận hiện tại.
        // Test này khẳng định đúng điều này để nếu sau này Owner được đưa vào policy, test đỏ để người sửa cập nhật cách mô phỏng.
        var baselinePath = FindBaselinePath();
        var rawLines = File.ReadAllLines(baselinePath).Skip(1).ToArray();

        for (int i = 0; i < rawLines.Length; i++)
        {
            var parts = rawLines[i].Split(',');
            var managerVal = parts[4];
            var ownerVal = parts[5];
            Assert.True(managerVal == ownerVal, $"Dòng {i + 2} ({parts[0]} {parts[1]}): Owner ({ownerVal}) khác Manager ({managerVal}). Owner hiện được mô phỏng bằng Manager.");
        }
    }

    [Fact]
    public async Task AccessMatrix_CashierManagerAnonymous_Unchanged()
    {
        var client = _factory.CreateClient();

        var healthResp = await client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);

        var ticketService = _factory.Services.GetRequiredService<ITicketService>();
        var cashierToken = ticketService.IssueAccessToken(new User { Id = 1, Username = "c", Role = UserRole.Cashier });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        var response = await client.PostAsJsonAsync("/api/v1/supplier-debts/99999/pay", new { amount = 1000m });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AccessMatrix_Policies_AccountantNotInLegacyPolicies()
    {
        using var scope = _factory.Services.CreateScope();
        var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var anyRolePolicy = await policyProvider.GetPolicyAsync("AnyRole");
        var managerOnlyPolicy = await policyProvider.GetPolicyAsync("ManagerOnly");
        var managerOrAccountantPolicy = await policyProvider.GetPolicyAsync("ManagerOrAccountant");
        var allRolesPolicy = await policyProvider.GetPolicyAsync("AllRoles");

        var cashier = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Cashier") }, "Test"));
        var manager = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Manager") }, "Test"));
        var accountant = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Accountant") }, "Test"));

        Assert.False((await authService.AuthorizeAsync(accountant, anyRolePolicy!)).Succeeded);
        Assert.False((await authService.AuthorizeAsync(accountant, managerOnlyPolicy!)).Succeeded);

        Assert.True((await authService.AuthorizeAsync(accountant, managerOrAccountantPolicy!)).Succeeded);
        Assert.True((await authService.AuthorizeAsync(accountant, allRolesPolicy!)).Succeeded);

        Assert.False((await authService.AuthorizeAsync(cashier, managerOrAccountantPolicy!)).Succeeded);
        Assert.True((await authService.AuthorizeAsync(cashier, allRolesPolicy!)).Succeeded);

        Assert.True((await authService.AuthorizeAsync(manager, anyRolePolicy!)).Succeeded);
        Assert.True((await authService.AuthorizeAsync(manager, managerOnlyPolicy!)).Succeeded);
        Assert.True((await authService.AuthorizeAsync(manager, managerOrAccountantPolicy!)).Succeeded);
        Assert.True((await authService.AuthorizeAsync(manager, allRolesPolicy!)).Succeeded);
    }

    [Fact]
    public async Task Accountant_Cannot_Be_Registered()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Data.AppDbContext>();
        var idsBefore = await db.Users.Select(u => u.Id).ToListAsync();

        var ownerUser = new User
        {
            Username = "owner_reg_" + Guid.NewGuid().ToString("N").Substring(0, 4),
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

            string regUsername = "reg_test_" + Guid.NewGuid().ToString("N").Substring(0, 4);
            var resp = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(regUsername, "Reg Test", regUsername + "@test.com", "Password123"));

            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
            var authRes = await resp.Content.ReadFromJsonAsync<AuthResponse>();
            Assert.NotNull(authRes);
            Assert.Equal(UserRole.Cashier, authRes.Role);
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
    public async Task Accountant_Token_RoundTrip()
    {
        var accUser = new User
        {
            Username = "accountant_user",
            FullName = "Accountant",
            Email = "acc_user@test.com",
            PasswordHash = Hashing.Hash("Password123"),
            Role = UserRole.Accountant
        };
        var ticketService = _factory.Services.GetRequiredService<ITicketService>();
        var token = ticketService.IssueAccessToken(accUser);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var meResp = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResp.StatusCode);

        var bankResp = await client.PostAsJsonAsync("/api/v1/bank-transactions/1/match", new { orderId = 1, note = "test" });
        Assert.Equal(HttpStatusCode.Forbidden, bankResp.StatusCode);
    }
}
