using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using PosApi.Controllers;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class StockTakeApprovalTests : IClassFixture<TestAppFactory>
{
    private readonly DatabaseFixture _fixture = new();
    private readonly TestAppFactory _factory;

    public StockTakeApprovalTests(TestAppFactory factory)
    {
        _factory = factory;
    }

    private string GenerateToken(int userId, UserRole role)
    {
        var ticketService = _factory.Services.GetRequiredService<ITicketService>();
        var user = new User
        {
            Id = userId,
            Username = "user" + userId,
            FullName = "User " + userId,
            Role = role
        };
        return ticketService.IssueAccessToken(user);
    }

    [Fact]
    public async Task ConfirmStockTake_AutoApproval_Success_Even_If_Creator()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var wh = new Warehouse { Code = "WH1", Name = "Kho 1", IsDefault = true };
            context.Warehouses.Add(wh);

            var manager = new User { Username = "mgr1_" + Guid.NewGuid().ToString("N").Substring(0, 6), FullName = "Manager 1", Email = Guid.NewGuid().ToString("N") + "@digisme.local", Role = UserRole.Manager, IsOwner = false };
            context.Users.Add(manager);

            var cat = new Category { Name = "Cat" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            var product = new Product { Name = "Prod 1", Barcode = "P1_" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = cat.Id, CostPrice = 1000m, CurrentStock = 10m };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m });
            await context.SaveChangesAsync();

            var stockTakeService = new StockTakeService(context);

            var st = await stockTakeService.CreateStockTakeAsync(manager.Id, new StockTakeCreateRequest(
                wh.Id, "Note", new List<StockTakeItemRequest> { new(product.Id, 10m, 60m, "Thừa 50") }
            ));

            Assert.Equal(StockTakeStatus.Draft, st.Status);

            var confirmed = await stockTakeService.ConfirmStockTakeAsync(manager.Id, st.Id);
            Assert.Equal(StockTakeStatus.Completed, confirmed.Status);
            Assert.Equal("Auto", confirmed.ApprovalLevel);
            Assert.Equal(manager.Id, confirmed.ConfirmedBy);
            Assert.Equal(50000m, confirmed.TotalDeltaValue);
        }
    }

    [Fact]
    public async Task ConfirmStockTake_ManagerLimit_ExactBoundary200k_RejectsCreator_AllowsOtherManager()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var wh = new Warehouse { Code = "WH1", Name = "Kho 1", IsDefault = true };
            context.Warehouses.Add(wh);

            var mgr1 = new User { Username = "mgr1_" + Guid.NewGuid().ToString("N").Substring(0, 6), FullName = "Manager 1", Email = Guid.NewGuid().ToString("N") + "@digisme.local", Role = UserRole.Manager };
            var mgr2 = new User { Username = "mgr2_" + Guid.NewGuid().ToString("N").Substring(0, 6), FullName = "Manager 2", Email = Guid.NewGuid().ToString("N") + "@digisme.local", Role = UserRole.Manager };
            context.Users.AddRange(mgr1, mgr2);

            var cat = new Category { Name = "Cat" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            var product = new Product { Name = "Prod 1", Barcode = "P1_" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = cat.Id, CostPrice = 2000m, CurrentStock = 10m };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m });
            await context.SaveChangesAsync();

            var stockTakeService = new StockTakeService(context);

            // Delta 100 * 2000 = 200,000 (Exact 200k boundary -> Manager level)
            var st = await stockTakeService.CreateStockTakeAsync(mgr1.Id, new StockTakeCreateRequest(
                wh.Id, "Note", new List<StockTakeItemRequest> { new(product.Id, 10m, 110m, "Thừa 100") }
            ));

            await Assert.ThrowsAsync<BusinessRuleException>(() => stockTakeService.ConfirmStockTakeAsync(mgr1.Id, st.Id));

            var draftCheck = await context.StockTakes.FindAsync(st.Id);
            Assert.Equal(StockTakeStatus.Draft, draftCheck!.Status);

            var confirmed = await stockTakeService.ConfirmStockTakeAsync(mgr2.Id, st.Id);
            Assert.Equal(StockTakeStatus.Completed, confirmed.Status);
            Assert.Equal("Manager", confirmed.ApprovalLevel);
            Assert.Equal(mgr2.Id, confirmed.ConfirmedBy);
        }
    }

    [Fact]
    public async Task ConfirmStockTake_OwnerLimit_Over1M_RequiresOwner_RejectsCreator()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var wh = new Warehouse { Code = "WH1", Name = "Kho 1", IsDefault = true };
            context.Warehouses.Add(wh);

            var mgr = new User { Username = "mgr_" + Guid.NewGuid().ToString("N").Substring(0, 6), FullName = "Manager", Email = Guid.NewGuid().ToString("N") + "@digisme.local", Role = UserRole.Manager, IsOwner = false };
            var owner = new User { Username = "owner_" + Guid.NewGuid().ToString("N").Substring(0, 6), FullName = "Owner", Email = Guid.NewGuid().ToString("N") + "@digisme.local", Role = UserRole.Manager, IsOwner = true };
            context.Users.AddRange(mgr, owner);

            var cat = new Category { Name = "Cat" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            var product = new Product { Name = "Prod 1", Barcode = "P1_" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = cat.Id, CostPrice = 10000m, CurrentStock = 10m };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m });
            await context.SaveChangesAsync();

            var stockTakeService = new StockTakeService(context);

            // Delta 101 * 10000 = 1,010,000 (> 1,000,000 -> Owner level)
            var st = await stockTakeService.CreateStockTakeAsync(mgr.Id, new StockTakeCreateRequest(
                wh.Id, "Note", new List<StockTakeItemRequest> { new(product.Id, 10m, 111m, "Thừa 101") }
            ));

            await Assert.ThrowsAsync<BusinessRuleException>(() => stockTakeService.ConfirmStockTakeAsync(mgr.Id, st.Id));

            var confirmed = await stockTakeService.ConfirmStockTakeAsync(owner.Id, st.Id);
            Assert.Equal(StockTakeStatus.Completed, confirmed.Status);
            Assert.Equal("Owner", confirmed.ApprovalLevel);
            Assert.Equal(owner.Id, confirmed.ConfirmedBy);
        }
    }

    [Fact]
    public async Task ConfirmStockTake_Rejected_KeepsDraft_And_StockUnchanged()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var wh = new Warehouse { Code = "WH1", Name = "Kho 1", IsDefault = true };
            context.Warehouses.Add(wh);

            var mgr = new User { Username = "mgr_" + Guid.NewGuid().ToString("N").Substring(0, 6), FullName = "Manager", Email = Guid.NewGuid().ToString("N") + "@digisme.local", Role = UserRole.Manager };
            context.Users.Add(mgr);

            var cat = new Category { Name = "Cat" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            var product = new Product { Name = "Prod 1", Barcode = "P1_" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = cat.Id, CostPrice = 10000m, CurrentStock = 10m };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            var stockRecord = new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m };
            context.WarehouseStocks.Add(stockRecord);
            await context.SaveChangesAsync();

            var stockTakeService = new StockTakeService(context);

            var st = await stockTakeService.CreateStockTakeAsync(mgr.Id, new StockTakeCreateRequest(
                wh.Id, "Note", new List<StockTakeItemRequest> { new(product.Id, 10m, 60m, "Thừa 50") }
            ));

            decimal stockBefore = stockRecord.Stock;

            await Assert.ThrowsAsync<BusinessRuleException>(() => stockTakeService.ConfirmStockTakeAsync(mgr.Id, st.Id));

            var stReloaded = await context.StockTakes.FindAsync(st.Id);
            Assert.Equal(StockTakeStatus.Draft, stReloaded!.Status);

            var stockAfter = (await context.WarehouseStocks.FirstAsync(s => s.WarehouseId == wh.Id && s.ProductId == product.Id)).Stock;
            Assert.Equal(stockBefore, stockAfter);
        }
    }

    [Fact]
    public async Task ConfirmStockTake_ChangingThresholds_ChangesClassification()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var wh = new Warehouse { Code = "WH1", Name = "Kho 1", IsDefault = true };
            context.Warehouses.Add(wh);

            var mgr1 = new User { Username = "mgr1_" + Guid.NewGuid().ToString("N").Substring(0, 6), FullName = "Manager 1", Email = Guid.NewGuid().ToString("N") + "@digisme.local", Role = UserRole.Manager };
            var mgr2 = new User { Username = "mgr2_" + Guid.NewGuid().ToString("N").Substring(0, 6), FullName = "Manager 2", Email = Guid.NewGuid().ToString("N") + "@digisme.local", Role = UserRole.Manager };
            context.Users.AddRange(mgr1, mgr2);

            var setting = await context.StoreSettings.FirstOrDefaultAsync(s => s.Id == 1);
            if (setting == null)
            {
                setting = new StoreSetting { Id = 1 };
                context.StoreSettings.Add(setting);
            }
            setting.StockTakeAutoLimit = 50000m;
            setting.StockTakeManagerLimit = 500000m;
            await context.SaveChangesAsync();

            var cat = new Category { Name = "Cat" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            var product = new Product { Name = "Prod 1", Barcode = "P1_" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = cat.Id, CostPrice = 1000m, CurrentStock = 10m };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m });
            await context.SaveChangesAsync();

            var stockTakeService = new StockTakeService(context);

            var st = await stockTakeService.CreateStockTakeAsync(mgr1.Id, new StockTakeCreateRequest(
                wh.Id, "Note", new List<StockTakeItemRequest> { new(product.Id, 10m, 85m, "Thừa 75") }
            ));

            await Assert.ThrowsAsync<BusinessRuleException>(() => stockTakeService.ConfirmStockTakeAsync(mgr1.Id, st.Id));

            var confirmed = await stockTakeService.ConfirmStockTakeAsync(mgr2.Id, st.Id);
            Assert.Equal(StockTakeStatus.Completed, confirmed.Status);
            Assert.Equal("Manager", confirmed.ApprovalLevel);
        }
    }

    [Fact]
    public async Task ConfirmStockTake_ManagerLimit_ExactBoundary1M()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var wh = new Warehouse { Code = "WH1", Name = "Kho 1", IsDefault = true };
            context.Warehouses.Add(wh);

            var mgr1 = new User { Username = "mgr1_" + Guid.NewGuid().ToString("N").Substring(0, 6), FullName = "Manager 1", Email = Guid.NewGuid().ToString("N") + "@digisme.local", Role = UserRole.Manager, IsOwner = false };
            var mgr2 = new User { Username = "mgr2_" + Guid.NewGuid().ToString("N").Substring(0, 6), FullName = "Manager 2", Email = Guid.NewGuid().ToString("N") + "@digisme.local", Role = UserRole.Manager, IsOwner = false };
            context.Users.AddRange(mgr1, mgr2);

            var cat = new Category { Name = "Cat" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            var product = new Product { Name = "Prod 1", Barcode = "P1_" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = cat.Id, CostPrice = 10000m, CurrentStock = 10m };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m });
            await context.SaveChangesAsync();

            var stockTakeService = new StockTakeService(context);

            // Delta 100 * 10000 = 1,000,000 (Exact 1M boundary -> Manager level)
            var st = await stockTakeService.CreateStockTakeAsync(mgr1.Id, new StockTakeCreateRequest(
                wh.Id, "Note", new List<StockTakeItemRequest> { new(product.Id, 10m, 110m, "Thừa 100") }
            ));

            var confirmed = await stockTakeService.ConfirmStockTakeAsync(mgr2.Id, st.Id);
            Assert.Equal(StockTakeStatus.Completed, confirmed.Status);
            Assert.Equal("Manager", confirmed.ApprovalLevel);
        }
    }

    [Fact]
    public async Task ConfirmStockTake_OwnerLimit_NonExistentUser_RejectsAndKeepsDraft()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var wh = new Warehouse { Code = "WH1", Name = "Kho 1", IsDefault = true };
            context.Warehouses.Add(wh);

            var mgr = new User { Username = "mgr_" + Guid.NewGuid().ToString("N").Substring(0, 6), FullName = "Manager", Email = Guid.NewGuid().ToString("N") + "@digisme.local", Role = UserRole.Manager };
            context.Users.Add(mgr);

            var cat = new Category { Name = "Cat" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            var product = new Product { Name = "Prod 1", Barcode = "P1_" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = cat.Id, CostPrice = 10000m, CurrentStock = 10m };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            var stockRecord = new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m };
            context.WarehouseStocks.Add(stockRecord);
            await context.SaveChangesAsync();

            var stockTakeService = new StockTakeService(context);

            // Delta > 1,000,000
            var st = await stockTakeService.CreateStockTakeAsync(mgr.Id, new StockTakeCreateRequest(
                wh.Id, "Note", new List<StockTakeItemRequest> { new(product.Id, 10m, 111m, "Thừa 111") }
            ));

            decimal stockBefore = stockRecord.Stock;
            int nonExistentUserId = 999999;

            await Assert.ThrowsAsync<BusinessRuleException>(() => stockTakeService.ConfirmStockTakeAsync(nonExistentUserId, st.Id));

            var stReloaded = await context.StockTakes.FindAsync(st.Id);
            Assert.Equal(StockTakeStatus.Draft, stReloaded!.Status);

            var stockAfter = (await context.WarehouseStocks.FirstAsync(s => s.WarehouseId == wh.Id && s.ProductId == product.Id)).Stock;
            Assert.Equal(stockBefore, stockAfter);
        }
    }

    [Fact]
    public async Task GetStockTakeThresholds_ZeroThresholds_ReturnsDefaults()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var setting = await context.StoreSettings.FirstOrDefaultAsync(s => s.Id == 1);
            if (setting == null)
            {
                setting = new StoreSetting { Id = 1 };
                context.StoreSettings.Add(setting);
            }
            setting.StockTakeAutoLimit = 0m;
            setting.StockTakeManagerLimit = 0m;
            await context.SaveChangesAsync();

            var controller = new SettingsController(context);
            var result = await controller.GetStockTakeThresholds();
            var okResult = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<StockTakeThresholdsResponse>(okResult.Value);

            Assert.Equal(200000m, response.AutoLimit);
            Assert.Equal(1000000m, response.ManagerLimit);
        }
    }

    [Fact]
    public async Task Settings_StockTakeThresholds_HttpTests()
    {
        // Ca ghi (PUT hợp lệ của Owner, GET sau PUT) chạy trên transaction rollback trực tiếp qua Controller
        var (contextTx, tx) = _fixture.CreateContextWithTransaction();
        using (contextTx)
        using (tx)
        {
            var controllerTx = new SettingsController(contextTx);
            controllerTx.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
                    {
                        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "999")
                    }, "TestAuth"))
                }
            };

            var uTx = await contextTx.Users.FindAsync(999);
            if (uTx == null)
            {
                contextTx.Users.Add(new User { Id = 999, Username = "owner999", FullName = "Owner 999", Email = "owner999@digisme.local", Role = UserRole.Manager, IsOwner = true });
                await contextTx.SaveChangesAsync();
            }
            else
            {
                uTx.IsOwner = true;
                await contextTx.SaveChangesAsync();
            }

            var putValidResult = await controllerTx.UpdateStockTakeThresholds(new StockTakeThresholdsRequest(50000m, 500000m));
            Assert.IsType<OkObjectResult>(putValidResult);

            var getResult = await controllerTx.GetStockTakeThresholds();
            var okResult = Assert.IsType<OkObjectResult>(getResult);
            var response = Assert.IsType<StockTakeThresholdsResponse>(okResult.Value);
            Assert.Equal(50000m, response.AutoLimit);
            Assert.Equal(500000m, response.ManagerLimit);
        }

        // Ca HTTP không ghi / kiểm tra HTTP permissions với try/finally bảo vệ DB dùng chung
        decimal? origAuto = null;
        decimal? origMan = null;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var setting = await db.StoreSettings.FirstOrDefaultAsync(s => s.Id == 1);
            if (setting != null)
            {
                origAuto = setting.StockTakeAutoLimit;
                origMan = setting.StockTakeManagerLimit;
                setting.StockTakeAutoLimit = 200000m;
                setting.StockTakeManagerLimit = 1000000m;
                await db.SaveChangesAsync();
            }
            var u = await db.Users.FindAsync(999);
            if (u == null)
            {
                db.Users.Add(new User { Id = 999, Username = "owner999", FullName = "Owner 999", Email = "owner999@digisme.local", Role = UserRole.Manager, IsOwner = true });
                await db.SaveChangesAsync();
            }
            else
            {
                u.IsOwner = true;
                await db.SaveChangesAsync();
            }
        }

        try
        {
            var client1 = _factory.CreateClient();
            client1.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateToken(777, UserRole.Cashier));
            var getRes = await client1.GetAsync("/api/v1/settings/stock-take-thresholds");
            Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
            var thresholds = await getRes.Content.ReadFromJsonAsync<StockTakeThresholdsResponse>();
            Assert.NotNull(thresholds);
            Assert.Equal(200000m, thresholds!.AutoLimit);
            Assert.Equal(1000000m, thresholds.ManagerLimit);

            var client2 = _factory.CreateClient();
            var cashierToken = GenerateToken(888, UserRole.Cashier);
            client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
            var putNonOwner = await client2.PutAsJsonAsync("/api/v1/settings/stock-take-thresholds", new StockTakeThresholdsRequest(50000m, 500000m));
            Assert.True(putNonOwner.StatusCode == HttpStatusCode.BadRequest || putNonOwner.StatusCode == HttpStatusCode.Forbidden || putNonOwner.StatusCode == HttpStatusCode.Unauthorized);

            var client3 = _factory.CreateClient();
            var ownerToken = GenerateToken(999, UserRole.Manager);
            client3.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
            var putInvalid = await client3.PutAsJsonAsync("/api/v1/settings/stock-take-thresholds", new StockTakeThresholdsRequest(1000000m, 500000m));
            Assert.Equal(HttpStatusCode.BadRequest, putInvalid.StatusCode);
        }
        finally
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var setting = await db.StoreSettings.FirstOrDefaultAsync(s => s.Id == 1);
                if (setting != null)
                {
                    setting.StockTakeAutoLimit = origAuto ?? 0m;
                    setting.StockTakeManagerLimit = origMan ?? 0m;
                    await db.SaveChangesAsync();
                }
                var u = await db.Users.FindAsync(999);
                if (u != null && u.Username == "owner999")
                {
                    db.Users.Remove(u);
                    await db.SaveChangesAsync();
                }
            }
        }
    }
}
