using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class MultiWarehouseServiceTests
{
    private readonly DatabaseFixture _fixture = new();

    [Fact]
    public async Task WarehouseCrud_And_DefaultManagement_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new WarehouseService(context);

            var wh1 = await service.CreateWarehouseAsync(1, new WarehouseCreateRequest("WH1", "Kho chính", "Hà Nội", true));
            Assert.True(wh1.IsDefault);
            Assert.Equal("WH1", wh1.Code);

            var wh2 = await service.CreateWarehouseAsync(1, new WarehouseCreateRequest("WH2", "Kho phụ", "Hồ Chí Minh", false));
            Assert.False(wh2.IsDefault);

            // Set wh2 as default
            var updated = await service.UpdateWarehouseAsync(1, wh2.Id, new WarehouseUpdateRequest("Kho phụ VIP", "Hồ Chí Minh", true, true));
            Assert.True(updated.IsDefault);

            // Verify wh1 is no longer default
            var wh1Reloaded = await service.GetWarehouseByIdAsync(wh1.Id);
            Assert.False(wh1Reloaded!.IsDefault);
        }
    }

    [Fact]
    public async Task WarehouseTransfer_CreateAndConfirm_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var whService = new WarehouseService(context);
            var transferService = new WarehouseTransferService(context);
            var stockService = new WarehouseStockService(context);

            var wh1 = await whService.CreateWarehouseAsync(1, new WarehouseCreateRequest("WH1", "Kho 1", null, true));
            var wh2 = await whService.CreateWarehouseAsync(1, new WarehouseCreateRequest("WH2", "Kho 2", null, false));

            var cat = new Category { Name = "Đồ uống" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            var product = new Product
            {
                Barcode = "SP01",
                Name = "Coca Cola",
                CategoryId = cat.Id,
                RetailPrice = 10000m,
                CostPrice = 5000m,
                CurrentStock = 100m
            };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            // Setup initial stock in wh1
            context.WarehouseStocks.Add(new WarehouseStock
            {
                WarehouseId = wh1.Id,
                ProductId = product.Id,
                Stock = 50m,
                UpdatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            // Create transfer draft
            var transfer = await transferService.CreateTransferAsync(1, new WarehouseTransferCreateRequest(
                wh1.Id,
                wh2.Id,
                "Chuyển hàng bổ sung",
                new List<WarehouseTransferItemRequest>
                {
                    new(product.Id, 20m, 5000m, "20 lon")
                }
            ));

            Assert.Equal(TransferStatus.Draft, transfer.Status);
            Assert.Single(transfer.Items);

            // Confirm transfer
            var confirmed = await transferService.ConfirmTransferAsync(1, transfer.Id);
            Assert.Equal(TransferStatus.Completed, confirmed.Status);

            // Verify stocks
            var stocks = await stockService.GetWarehouseStocksAsync(null, null, null, 1, 10);
            var wh1Stock = stocks.Items.First(s => s.WarehouseId == wh1.Id && s.ProductId == product.Id);
            var wh2Stock = stocks.Items.First(s => s.WarehouseId == wh2.Id && s.ProductId == product.Id);

            Assert.Equal(30m, wh1Stock.Stock); // 50 - 20
            Assert.Equal(20m, wh2Stock.Stock); // 0 + 20

            // Verify ledger entries created
            var ledgerCount = await context.StockLedger.CountAsync();
            Assert.Equal(2, ledgerCount);
        }
    }

    [Fact]
    public async Task StockTake_CreateAndConfirm_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var whService = new WarehouseService(context);
            var stockTakeService = new StockTakeService(context);
            var stockService = new WarehouseStockService(context);

            var wh = await whService.CreateWarehouseAsync(1, new WarehouseCreateRequest("WH1", "Kho chính", null, true));

            var cat = new Category { Name = "Đồ ăn" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            var product = new Product
            {
                Barcode = "SP02",
                Name = "Bánh mỳ",
                CategoryId = cat.Id,
                RetailPrice = 15000m,
                CostPrice = 8000m,
                CurrentStock = 10m
            };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.WarehouseStocks.Add(new WarehouseStock
            {
                WarehouseId = wh.Id,
                ProductId = product.Id,
                Stock = 10m,
                UpdatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            // System says 10, actual counted is 8 (variance -2)
            var stockTake = await stockTakeService.CreateStockTakeAsync(1, new StockTakeCreateRequest(
                wh.Id,
                "Kiểm kê tháng 11",
                new List<StockTakeItemRequest>
                {
                    new(product.Id, 10m, 8m, "Hao hụt 2 cái")
                }
            ));

            Assert.Equal(StockTakeStatus.Draft, stockTake.Status);
            Assert.Equal(-2m, stockTake.Items.First().DeltaQty);

            var confirmed = await stockTakeService.ConfirmStockTakeAsync(1, stockTake.Id);
            Assert.Equal(StockTakeStatus.Completed, confirmed.Status);

            var stocks = await stockService.GetWarehouseStocksAsync(wh.Id, product.Id, null, 1, 10);
            Assert.Single(stocks.Items);
            Assert.Equal(8m, stocks.Items.First().Stock);
        }
    }

    [Fact]
    public async Task SupplierReturn_CreateAndConfirm_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var whService = new WarehouseService(context);
            var supplierService = new SupplierService(context);
            var returnService = new SupplierReturnService(context);
            var stockService = new WarehouseStockService(context);

            var wh = await whService.CreateWarehouseAsync(1, new WarehouseCreateRequest("WH1", "Kho chính", null, true));
            var supplier = await supplierService.CreateSupplierAsync(1, new SupplierCreateRequest("NCC01", "Công ty TNHH Thực Phẩm Sạch", "0901234567", "Hà Nội", "0123456789", null));

            var cat = new Category { Name = "Thực phẩm" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            var product = new Product
            {
                Barcode = "SP03",
                Name = "Thịt bò",
                CategoryId = cat.Id,
                RetailPrice = 200000m,
                CostPrice = 150000m,
                CurrentStock = 50m
            };
            context.Products.Add(product);
            await context.SaveChangesAsync();

            context.WarehouseStocks.Add(new WarehouseStock
            {
                WarehouseId = wh.Id,
                ProductId = product.Id,
                Stock = 30m,
                UpdatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();

            var supplierReturn = await returnService.CreateSupplierReturnAsync(1, new SupplierReturnCreateRequest(
                supplier.Id,
                wh.Id,
                "Trả hàng lỗi",
                new List<SupplierReturnItemRequest>
                {
                    new(product.Id, 5m, 150000m, "5kg thịt hỏng")
                }
            ));

            Assert.Equal(SupplierReturnStatus.Draft, supplierReturn.Status);
            Assert.Equal(750000m, supplierReturn.Total);

            var confirmed = await returnService.ConfirmSupplierReturnAsync(1, supplierReturn.Id);
            Assert.Equal(SupplierReturnStatus.Completed, confirmed.Status);

            var stocks = await stockService.GetWarehouseStocksAsync(wh.Id, product.Id, null, 1, 10);
            Assert.Equal(25m, stocks.Items.First().Stock); // 30 - 5
        }
    }
}
