using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class SupplierReturnServiceTests
{
    private readonly DatabaseFixture _fixture = new();

    private async Task EnsureWarehouseAsync(AppDbContext context)
    {
        if (!await context.Warehouses.AnyAsync())
        {
            context.Warehouses.Add(new Warehouse { Code = "WH-DEFAULT", Name = "Kho mặc định", IsDefault = true, IsActive = true });
            await context.SaveChangesAsync();
        }
    }

    private async Task<int> SeedUser(AppDbContext context)
    {
        var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
        var user = new User
        {
            Username = "user_" + uniqueId,
            Email = "user_" + uniqueId + "@test.com",
            FullName = "Test User",
            Role = UserRole.Manager,
            PasswordHash = "x"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> CreateSupplierAsync(AppDbContext context)
    {
        var supplier = new Supplier
        {
            Name = "Supplier " + Guid.NewGuid().ToString().Substring(0, 6),
            Phone = "0900000000",
            IsActive = true
        };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();
        return supplier.Id;
    }

    [Fact]
    public async Task ConfirmSupplierReturn_UpdatesWarehouseStockAndCurrentStock_MaintainsQ14()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            await EnsureWarehouseAsync(context);
            var wh = await context.Warehouses.FirstAsync(w => w.IsDefault);
            var userId = await SeedUser(context);
            var supplierId = await CreateSupplierAsync(context);

            var category = new Category { Name = "Cat " + Guid.NewGuid(), IsActive = true };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var product = new Product
            {
                Name = "Test Product",
                Barcode = "SR-" + Guid.NewGuid().ToString().Substring(0, 6),
                CategoryId = category.Id,
                RetailPrice = 10000m,
                CurrentStock = 10m,
                IsActive = true
            };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.WarehouseStocks.Add(new WarehouseStock
            {
                WarehouseId = wh.Id,
                ProductId = product.Id,
                Stock = 10m
            });
            await context.SaveChangesAsync();

            var service = new SupplierReturnService(context);
            var createReq = new SupplierReturnCreateRequest(
                supplierId,
                wh.Id,
                "Trả hàng test",
                new List<SupplierReturnItemRequest>
                {
                    new(product.Id, 3m, 5000m, null)
                }
            );

            var created = await service.CreateSupplierReturnAsync(userId, createReq);
            Assert.Equal(SupplierReturnStatus.Draft, created.Status);

            var confirmed = await service.ConfirmSupplierReturnAsync(userId, created.Id);
            Assert.Equal(SupplierReturnStatus.Completed, confirmed.Status);

            var updatedProduct = await context.Products.FindAsync(product.Id);
            var updatedWhStock = await context.WarehouseStocks
                .FirstOrDefaultAsync(s => s.WarehouseId == wh.Id && s.ProductId == product.Id);

            Assert.Equal(7m, updatedProduct!.CurrentStock);
            Assert.Equal(7m, updatedWhStock!.Stock);
        }
    }

    [Fact]
    public async Task ConfirmSupplierReturn_MultipleProducts_ReducesAllCurrentStocks()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            await EnsureWarehouseAsync(context);
            var wh = await context.Warehouses.FirstAsync(w => w.IsDefault);
            var userId = await SeedUser(context);
            var supplierId = await CreateSupplierAsync(context);

            var category = new Category { Name = "Cat " + Guid.NewGuid(), IsActive = true };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var p1 = new Product { Name = "P1", Barcode = "P1-" + Guid.NewGuid().ToString().Substring(0, 6), CategoryId = category.Id, RetailPrice = 1000m, CurrentStock = 20m, IsActive = true };
            var p2 = new Product { Name = "P2", Barcode = "P2-" + Guid.NewGuid().ToString().Substring(0, 6), CategoryId = category.Id, RetailPrice = 1000m, CurrentStock = 15m, IsActive = true };
            context.Products.AddRange(p1, p2);
            await context.SaveChangesAsync();

            context.WarehouseStocks.AddRange(
                new WarehouseStock { WarehouseId = wh.Id, ProductId = p1.Id, Stock = 20m },
                new WarehouseStock { WarehouseId = wh.Id, ProductId = p2.Id, Stock = 15m }
            );
            await context.SaveChangesAsync();

            var service = new SupplierReturnService(context);
            var createReq = new SupplierReturnCreateRequest(
                supplierId,
                wh.Id,
                "Multi return",
                new List<SupplierReturnItemRequest>
                {
                    new(p1.Id, 5m, 100m, null),
                    new(p2.Id, 4m, 100m, null)
                }
            );

            var created = await service.CreateSupplierReturnAsync(userId, createReq);
            await service.ConfirmSupplierReturnAsync(userId, created.Id);

            Assert.Equal(15m, (await context.Products.FindAsync(p1.Id))!.CurrentStock);
            Assert.Equal(11m, (await context.Products.FindAsync(p2.Id))!.CurrentStock);
        }
    }

    [Fact]
    public async Task ConfirmSupplierReturn_InsufficientStock_ThrowsBusinessRuleException_NoStockChange()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            await EnsureWarehouseAsync(context);
            var wh = await context.Warehouses.FirstAsync(w => w.IsDefault);
            var userId = await SeedUser(context);
            var supplierId = await CreateSupplierAsync(context);

            var category = new Category { Name = "Cat " + Guid.NewGuid(), IsActive = true };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var product = new Product { Name = "Test", Barcode = "T-" + Guid.NewGuid().ToString().Substring(0, 6), CategoryId = category.Id, RetailPrice = 1000m, CurrentStock = 5m, IsActive = true };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 5m });
            await context.SaveChangesAsync();

            var service = new SupplierReturnService(context);
            var createReq = new SupplierReturnCreateRequest(
                supplierId,
                wh.Id,
                "Over return",
                new List<SupplierReturnItemRequest>
                {
                    new(product.Id, 10m, 100m, null)
                }
            );

            var created = await service.CreateSupplierReturnAsync(userId, createReq);

            await Assert.ThrowsAsync<BusinessRuleException>(() => service.ConfirmSupplierReturnAsync(userId, created.Id));

            var dbProd = await context.Products.FindAsync(product.Id);
            var dbWhStock = await context.WarehouseStocks.FirstAsync(s => s.WarehouseId == wh.Id && s.ProductId == product.Id);
            var dbSr = await context.SupplierReturns.FindAsync(created.Id);

            Assert.Equal(5m, dbProd!.CurrentStock);
            Assert.Equal(5m, dbWhStock.Stock);
            Assert.Equal(SupplierReturnStatus.Draft, dbSr!.Status);
        }
    }

    [Fact]
    public async Task ConfirmSupplierReturn_ConfirmTwice_ThrowsBusinessRuleException()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            await EnsureWarehouseAsync(context);
            var wh = await context.Warehouses.FirstAsync(w => w.IsDefault);
            var userId = await SeedUser(context);
            var supplierId = await CreateSupplierAsync(context);

            var category = new Category { Name = "Cat " + Guid.NewGuid(), IsActive = true };
            context.Categories.Add(category);
            await context.SaveChangesAsync();

            var product = new Product { Name = "Test", Barcode = "T2-" + Guid.NewGuid().ToString().Substring(0, 6), CategoryId = category.Id, RetailPrice = 1000m, CurrentStock = 10m, IsActive = true };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m });
            await context.SaveChangesAsync();

            var service = new SupplierReturnService(context);
            var createReq = new SupplierReturnCreateRequest(
                supplierId,
                wh.Id,
                "Double confirm",
                new List<SupplierReturnItemRequest>
                {
                    new(product.Id, 2m, 100m, null)
                }
            );

            var created = await service.CreateSupplierReturnAsync(userId, createReq);
            await service.ConfirmSupplierReturnAsync(userId, created.Id);

            await Assert.ThrowsAsync<BusinessRuleException>(() => service.ConfirmSupplierReturnAsync(userId, created.Id));
            Assert.Equal(8m, (await context.Products.FindAsync(product.Id))!.CurrentStock);
        }
    }
}
