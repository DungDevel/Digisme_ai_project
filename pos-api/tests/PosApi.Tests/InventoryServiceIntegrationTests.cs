using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;
using Npgsql;

namespace PosApi.Tests;

public class InventoryServiceIntegrationTests
{
    private readonly DatabaseFixture _fixture = new();

    private async Task<Product> SeedProduct(AppDbContext context, decimal initialStock = 100m, decimal minStock = 10m)
    {
        var category = new Category { Name = "Test Cat " + Guid.NewGuid(), IsActive = true };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "Test Product",
            Barcode = "TEST001",
            CategoryId = category.Id,
            RetailPrice = 1000m,
            CurrentStock = initialStock,
            MinStock = minStock,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product;
    }

    [Fact]
    public async Task AdjustStock_Increase_UpdatesStockAndLedger()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var service = new InventoryService(context);
        var product = await SeedProduct(context, 100m);
        var req = new InventoryAdjustRequest(20m, "Adding stock");

        var result = await service.AdjustStockAsync(1, product.Id, req);

        Assert.True(result);
        var updatedProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(120m, updatedProduct!.CurrentStock);

        var ledger = await context.StockLedger.FirstOrDefaultAsync(l => l.ProductId == product.Id && l.RefType == LedgerRefType.Adjustment);
        Assert.NotNull(ledger);
        Assert.Equal(20m, ledger!.Qty);
        Assert.Equal(120m, ledger.Balance);
        Assert.Equal("Adding stock", ledger.Note);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task AdjustStock_Decrease_UpdatesStockAndLedger()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var service = new InventoryService(context);
        var product = await SeedProduct(context, 100m);
        var req = new InventoryAdjustRequest(-30m, "Removing stock");

        var result = await service.AdjustStockAsync(1, product.Id, req);

        Assert.True(result);
        var updatedProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(70m, updatedProduct!.CurrentStock);

        var ledger = await context.StockLedger.FirstOrDefaultAsync(l => l.ProductId == product.Id && l.RefType == LedgerRefType.Adjustment);
        Assert.NotNull(ledger);
        Assert.Equal(-30m, ledger!.Qty);
        Assert.Equal(70m, ledger.Balance);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task AdjustStock_NegativeResult_ThrowsBusinessRuleException_AndRollsBack()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var service = new InventoryService(context);
        var product = await SeedProduct(context, 10m);
        var req = new InventoryAdjustRequest(-20m, "Too much removal");

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AdjustStockAsync(1, product.Id, req));
        Assert.Contains("tồn kho âm", ex.Message);

        var updatedProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(10m, updatedProduct!.CurrentStock);

        var ledger = await context.StockLedger.FirstOrDefaultAsync(l => l.ProductId == product.Id && l.RefType == LedgerRefType.Adjustment);
        Assert.Null(ledger);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task AdjustStock_InvalidProduct_ThrowsKeyNotFoundException()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var service = new InventoryService(context);
        var req = new InventoryAdjustRequest(10m, "Reason");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.AdjustStockAsync(1, 99999, req));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task AdjustStock_ZeroAmount_ThrowsBusinessRuleException()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var service = new InventoryService(context);
        var product = await SeedProduct(context);
        var req = new InventoryAdjustRequest(0m, "Reason");
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AdjustStockAsync(1, product.Id, req));
        Assert.Contains("không được bằng 0", ex.Message);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task AdjustStock_EmptyReason_ThrowsBusinessRuleException()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var service = new InventoryService(context);
        var product = await SeedProduct(context);
        var req = new InventoryAdjustRequest(10m, "");
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.AdjustStockAsync(1, product.Id, req));
        Assert.Contains("lý do điều chỉnh kho", ex.Message);
        await tx.RollbackAsync();
    }
}
