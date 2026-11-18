using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class InventoryReconcilerTests
{
    private readonly DatabaseFixture _fixture = new();

    private async Task EnsureDefaultWarehouseAsync(AppDbContext context)
    {
        if (!await context.Warehouses.AnyAsync())
        {
            context.Warehouses.Add(new Warehouse { Code = "WH-DEFAULT", Name = "Kho mặc định", IsDefault = true, IsActive = true });
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ComputeDiffs_DryRun_DoesNotModifyDatabase()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await using var _ = tx;

        await EnsureDefaultWarehouseAsync(context);
        var cat = new Category { Name = "Cat " + Guid.NewGuid() };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        // Product 1: CurrentStock = 10, WhStock = 5 (diff > 0)
        var p1 = new Product { Name = "P1", Barcode = "BC1-" + Guid.NewGuid(), CategoryId = cat.Id, CurrentStock = 10m, IsActive = true };
        // Product 2: CurrentStock = 2, WhStock = 5 (diff < 0)
        var p2 = new Product { Name = "P2", Barcode = "BC2-" + Guid.NewGuid(), CategoryId = cat.Id, CurrentStock = 2m, IsActive = true };
        context.Products.AddRange(p1, p2);
        await context.SaveChangesAsync();

        var defaultWh = await context.Warehouses.FirstAsync(w => w.IsDefault);
        context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = defaultWh.Id, ProductId = p1.Id, Stock = 5m });
        context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = defaultWh.Id, ProductId = p2.Id, Stock = 5m });
        await context.SaveChangesAsync();

        var wsCountBefore = await context.WarehouseStocks.CountAsync();
        var slCountBefore = await context.StockLedger.CountAsync();
        var plCountBefore = await context.PosLogs.CountAsync();

        var report = await InventoryReconciler.ComputeDiffsAsync(context);

        var wsCountAfter = await context.WarehouseStocks.CountAsync();
        var slCountAfter = await context.StockLedger.CountAsync();
        var plCountAfter = await context.PosLogs.CountAsync();

        Assert.Equal(wsCountBefore, wsCountAfter);
        Assert.Equal(slCountBefore, slCountAfter);
        Assert.Equal(plCountBefore, plCountAfter);

        var ourDetails = report.Details.Where(d => d.ProductId == p1.Id || d.ProductId == p2.Id).ToList();
        Assert.Equal(2, ourDetails.Count);
        Assert.Contains(ourDetails, d => d.ProductId == p1.Id && d.Diff == 5m && d.ActionType == "WillAddToDefaultWarehouse");
        Assert.Contains(ourDetails, d => d.ProductId == p2.Id && d.Diff == -3m && d.ActionType == "ReportOnly");
    }

    [Fact]
    public async Task ReconcileApply_Surplus_UpdatesWarehouseStockAndLedger_Idempotent()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await using var _ = tx;

        await EnsureDefaultWarehouseAsync(context);
        var cat = new Category { Name = "Cat " + Guid.NewGuid() };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var p = new Product { Name = "P1", Barcode = "BC-" + Guid.NewGuid(), CategoryId = cat.Id, CurrentStock = 10m, IsActive = true };
        context.Products.Add(p);
        await context.SaveChangesAsync();

        var defaultWh = await context.Warehouses.FirstAsync(w => w.IsDefault);
        context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = defaultWh.Id, ProductId = p.Id, Stock = 3m });
        await context.SaveChangesAsync();

        var service = new InventoryService(context);
        var response = await service.ReconcileInventoryAsync(1, new InventoryReconcileRequest(null, false));

        Assert.False(response.DryRun);
        var whStock = await context.WarehouseStocks.FirstOrDefaultAsync(ws => ws.WarehouseId == defaultWh.Id && ws.ProductId == p.Id);
        Assert.NotNull(whStock);
        Assert.Equal(10m, whStock!.Stock);
        Assert.Equal(10m, (await context.Products.FindAsync(p.Id))!.CurrentStock);

        var ledgerEntry = await context.StockLedger.FirstOrDefaultAsync(l => l.ProductId == p.Id && l.RefType == LedgerRefType.Adjustment);
        Assert.NotNull(ledgerEntry);
        Assert.Equal(7m, ledgerEntry!.Qty);

        // Run again -> Idempotent (no surplus remaining for this product)
        var response2 = await service.ReconcileInventoryAsync(1, new InventoryReconcileRequest(null, false));
        var ourDetail2 = response2.Details.FirstOrDefault(d => d.ProductId == p.Id);
        Assert.Null(ourDetail2);
    }

    [Fact]
    public async Task ReconcileApply_Deficit_DoesNotModifyStock()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await using var _ = tx;

        await EnsureDefaultWarehouseAsync(context);
        var cat = new Category { Name = "Cat " + Guid.NewGuid() };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var p = new Product { Name = "P1", Barcode = "BC-" + Guid.NewGuid(), CategoryId = cat.Id, CurrentStock = 2m, IsActive = true };
        context.Products.Add(p);
        await context.SaveChangesAsync();

        var defaultWh = await context.Warehouses.FirstAsync(w => w.IsDefault);
        context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = defaultWh.Id, ProductId = p.Id, Stock = 5m });
        await context.SaveChangesAsync();

        var service = new InventoryService(context);
        var response = await service.ReconcileInventoryAsync(1, new InventoryReconcileRequest(null, false));

        var ourDetail = response.Details.First(d => d.ProductId == p.Id);
        Assert.Equal("ReportOnly", ourDetail.ActionType);
        var whStock = await context.WarehouseStocks.FirstAsync(ws => ws.WarehouseId == defaultWh.Id && ws.ProductId == p.Id);
        Assert.Equal(5m, whStock.Stock);
        Assert.Equal(2m, (await context.Products.FindAsync(p.Id))!.CurrentStock);
    }

    [Fact]
    public async Task ComputeDiffs_IgnoresInactiveProducts()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await using var _ = tx;

        await EnsureDefaultWarehouseAsync(context);
        var cat = new Category { Name = "Cat " + Guid.NewGuid() };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var p = new Product { Name = "Inactive P", Barcode = "BC-INACT-" + Guid.NewGuid(), CategoryId = cat.Id, CurrentStock = 10m, IsActive = false };
        context.Products.Add(p);
        await context.SaveChangesAsync();

        var report = await InventoryReconciler.ComputeDiffsAsync(context);
        Assert.DoesNotContain(report.Details, d => d.ProductId == p.Id);
    }

    [Fact]
    public async Task ComputeDiffs_NoActiveWarehouse_ThrowsInvalidOperationException()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await using var _ = tx;

        // Deactivate all warehouses
        await context.Warehouses.ExecuteUpdateAsync(w => w.SetProperty(x => x.IsActive, false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => InventoryReconciler.ComputeDiffsAsync(context));
    }

    [Fact]
    public async Task ReconcileInventory_NullBody_DefaultsToDryRun()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await using var _ = tx;

        await EnsureDefaultWarehouseAsync(context);
        var cat = new Category { Name = "Cat " + Guid.NewGuid() };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var p = new Product { Name = "P1", Barcode = "BC-" + Guid.NewGuid(), CategoryId = cat.Id, CurrentStock = 10m, IsActive = true };
        context.Products.Add(p);
        await context.SaveChangesAsync();

        var service = new InventoryService(context);
        var response = await service.ReconcileInventoryAsync(1, null);

        Assert.True(response.DryRun);
        var wsList = await context.WarehouseStocks.Where(ws => ws.ProductId == p.Id).ToListAsync();
        Assert.Empty(wsList);
    }

    [Fact]
    public async Task Reconcile_NonExistentWarehouse_ThrowsKeyNotFoundException_NothingWritten()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await using var _ = tx;

        var service = new InventoryService(context);
        int nonExistentId = 999999;

        var wsBefore = await context.WarehouseStocks.CountAsync();
        var slBefore = await context.StockLedger.CountAsync();
        var plBefore = await context.PosLogs.CountAsync();

        // DryRun = true
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReconcileInventoryAsync(1, new InventoryReconcileRequest(nonExistentId, true)));
        // DryRun = false
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ReconcileInventoryAsync(1, new InventoryReconcileRequest(nonExistentId, false)));

        Assert.Equal(wsBefore, await context.WarehouseStocks.CountAsync());
        Assert.Equal(slBefore, await context.StockLedger.CountAsync());
        Assert.Equal(plBefore, await context.PosLogs.CountAsync());
    }

    [Fact]
    public async Task Reconcile_InactiveWarehouse_ThrowsInvalidOperationException()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await using var _ = tx;

        var inactiveWh = new Warehouse { Code = "WH-INACTIVE", Name = "Kho không hoạt động", IsDefault = false, IsActive = false };
        context.Warehouses.Add(inactiveWh);
        await context.SaveChangesAsync();

        var service = new InventoryService(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReconcileInventoryAsync(1, new InventoryReconcileRequest(inactiveWh.Id, true)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReconcileInventoryAsync(1, new InventoryReconcileRequest(inactiveWh.Id, false)));
    }
}
