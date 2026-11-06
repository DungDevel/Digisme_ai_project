using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class ReturnServiceTests
{
    private readonly DatabaseFixture _fixture = new();

    private async Task SeedOrderData(AppDbContext context)
    {
        var category = new Category { Name = "Test Cat " + Guid.NewGuid(), IsActive = true };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "Test Product",
            Barcode = "RET-" + Guid.NewGuid().ToString().Substring(0, 8),
            CategoryId = category.Id,
            RetailPrice = 10000m,
            CurrentStock = 50m,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
        var cashier = new User { Username = "admin_" + uniqueId, Email = "admin_" + uniqueId + "@test.com", FullName = "Admin", Role = UserRole.Manager, PasswordHash = "x" };
        context.Users.Add(cashier);
        await context.SaveChangesAsync();

        var order = new Order
        {
            Code = "INV-20261010-" + Guid.NewGuid().ToString().Substring(0, 6),
            CashierId = cashier.Id,
            Subtotal = 20000m,
            Total = 20000m,
            Status = OrderStatus.Completed,
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    ProductId = product.Id,
                    Qty = 2,
                    UnitPrice = 10000m,
                    Total = 20000m,
                    ReturnedQty = 0
                }
            }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task CreateReturnAsync_Success_IncreasesStockAndRecordsReturn()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await SeedOrderData(context);
        var loggerMock = new Mock<ILogger<ReturnService>>();
        var service = new ReturnService(context, loggerMock.Object);
        var orderItem = await context.OrderItems.FirstAsync();
        var req = new ReturnRequest(orderItem.OrderId, "Khách trả hàng", new List<ReturnItemRequest>
        {
            new(orderItem.Id, 1)
        });

        var result = await service.CreateReturnAsync(1, req);

        Assert.NotNull(result);
        Assert.Contains("PHTRA-", result.Code);
        Assert.Equal(10000m, result.TotalReturned);

        var product = await context.Products.FindAsync(orderItem.ProductId);
        Assert.Equal(51m, product!.CurrentStock);

        var ledger = await context.StockLedger.OrderByDescending(l => l.Id).FirstOrDefaultAsync(l => l.RefType == LedgerRefType.Return);
        Assert.NotNull(ledger);
        Assert.Equal(1m, ledger!.Qty);
        Assert.Equal(LedgerRefType.Return, ledger.RefType);

        var updatedItem = await context.OrderItems.FindAsync(orderItem.Id);
        Assert.Equal(1m, updatedItem!.ReturnedQty);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateReturnAsync_ExceedsOriginalQty_ThrowsException()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await SeedOrderData(context);
        var loggerMock = new Mock<ILogger<ReturnService>>();
        var service = new ReturnService(context, loggerMock.Object);
        var orderItem = await context.OrderItems.FirstAsync();
        var req = new ReturnRequest(orderItem.OrderId, "Trả quá", new List<ReturnItemRequest>
        {
            new(orderItem.Id, 3)
        });

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateReturnAsync(1, req));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateReturnAsync_MultipleReturns_AccumulatesReturnedQty()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await SeedOrderData(context);
        var loggerMock = new Mock<ILogger<ReturnService>>();
        var service = new ReturnService(context, loggerMock.Object);
        var orderItem = await context.OrderItems.FirstAsync();

        var req1 = new ReturnRequest(orderItem.OrderId, "Lần 1", new List<ReturnItemRequest> { new(orderItem.Id, 1) });
        await service.CreateReturnAsync(1, req1);

        var req2 = new ReturnRequest(orderItem.OrderId, "Lần 2", new List<ReturnItemRequest> { new(orderItem.Id, 1) });
        await service.CreateReturnAsync(1, req2);

        var updatedItem = await context.OrderItems.FindAsync(orderItem.Id);
        Assert.Equal(2m, updatedItem!.ReturnedQty);

        var product = await context.Products.FindAsync(orderItem.ProductId);
        Assert.Equal(52m, product!.CurrentStock);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateReturnAsync_InvalidOrderItem_ThrowsException()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await SeedOrderData(context);
        var loggerMock = new Mock<ILogger<ReturnService>>();
        var service = new ReturnService(context, loggerMock.Object);
        var orderItem = await context.OrderItems.FirstAsync();
        var req = new ReturnRequest(orderItem.OrderId, "Sai item", new List<ReturnItemRequest> { new(9999, 1) });

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateReturnAsync(1, req));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateReturnAsync_FullReturn_RefundsShippingFee()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var category = new Category { Name = "Cat " + Guid.NewGuid(), IsActive = true };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "P", Barcode = "P-" + Guid.NewGuid().ToString().Substring(0, 8), CategoryId = category.Id, RetailPrice = 10000m, CurrentStock = 50m, IsActive = true };
        context.Products.Add(product);
        var cashier = new User { Username = "u_" + Guid.NewGuid(), Email = "u_" + Guid.NewGuid() + "@test.com", FullName = "U", Role = UserRole.Manager, PasswordHash = "x" };
        context.Users.Add(cashier);
        await context.SaveChangesAsync();

        var order = new Order
        {
            Code = "INV-" + Guid.NewGuid().ToString().Substring(0, 6),
            CashierId = cashier.Id,
            Subtotal = 20000m,
            Total = 35000m,
            ShippingFee = 15000m,
            Status = OrderStatus.Completed,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = product.Id, Qty = 2, UnitPrice = 10000m, Total = 20000m, ReturnedQty = 0 }
            }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var orderItem = order.Items.First();
        var req = new ReturnRequest(order.Id, "Trả toàn bộ kèm ship", new List<ReturnItemRequest>
        {
            new(orderItem.Id, orderItem.Qty)
        });

        var result = await service.CreateReturnAsync(1, req);
        Assert.Equal(35000m, result.TotalReturned);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateReturnAsync_FractionalRemainderAndMultipleReturns_ExactRefund()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var category = new Category { Name = "Cat " + Guid.NewGuid(), IsActive = true };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "P",
            Barcode = "P-" + Guid.NewGuid().ToString().Substring(0, 8),
            CategoryId = category.Id,
            RetailPrice = 33333.33m,
            CurrentStock = 10m,
            IsActive = true
        };
        context.Products.Add(product);
        var cashier = new User { Username = "u_" + Guid.NewGuid(), Email = "u_" + Guid.NewGuid() + "@test.com", FullName = "U", Role = UserRole.Manager, PasswordHash = "x" };
        context.Users.Add(cashier);
        await context.SaveChangesAsync();

        var order = new Order
        {
            Code = "INV-" + Guid.NewGuid().ToString().Substring(0, 6),
            CashierId = cashier.Id,
            Subtotal = 99999.99m,
            Total = 99999.99m,
            Status = OrderStatus.Completed,
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    ProductId = product.Id,
                    Qty = 3,
                    UnitPrice = 33333.33m,
                    Total = 99999.99m,
                    ReturnedQty = 0
                }
            }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var loggerMock = new Mock<ILogger<ReturnService>>();
        var service = new ReturnService(context, loggerMock.Object);
        var orderItem = order.Items.First();

        var res1 = await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "1", new List<ReturnItemRequest> { new(orderItem.Id, 1) }));
        var res2 = await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "2", new List<ReturnItemRequest> { new(orderItem.Id, 1) }));
        var res3 = await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "3", new List<ReturnItemRequest> { new(orderItem.Id, 1) }));

        var totalRefunded = res1.TotalReturned + res2.TotalReturned + res3.TotalReturned;
        Assert.Equal(99999.99m, totalRefunded);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateReturnAsync_OldOrderWithManualDiscount_LegacyOverRefundBehavior()
    {
        // Hành vi cũ giữ có chủ đích: đơn cũ có discount thủ công nhưng OrderItem không lưu LineDiscount, ReturnService hoàn theo UnitPrice*Qty
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await SeedOrderData(context);
        var order = await context.Orders.FirstAsync();
        order.Discount = 5000m;
        await context.SaveChangesAsync();

        var loggerMock = new Mock<ILogger<ReturnService>>();
        var service = new ReturnService(context, loggerMock.Object);
        var orderItem = await context.OrderItems.FirstAsync();

        var res = await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "Old order return", new List<ReturnItemRequest> { new(orderItem.Id, 2) }));
        Assert.Equal(20000m, res.TotalReturned);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task PartialReturn_RefundsProportionalToLineDiscount()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var category = new Category { Name = "Cat " + Guid.NewGuid(), IsActive = true };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "P", Barcode = "P-" + Guid.NewGuid().ToString().Substring(0, 8), CategoryId = category.Id, RetailPrice = 10000m, CurrentStock = 10m, IsActive = true };
        context.Products.Add(product);
        var cashier = new User { Username = "u_" + Guid.NewGuid(), Email = "u_" + Guid.NewGuid() + "@test.com", FullName = "U", Role = UserRole.Manager, PasswordHash = "x" };
        context.Users.Add(cashier);
        await context.SaveChangesAsync();

        var order = new Order
        {
            Code = "INV-" + Guid.NewGuid().ToString().Substring(0, 6),
            CashierId = cashier.Id,
            Subtotal = 10000m,
            Discount = 1000m,
            Total = 9000m,
            Status = OrderStatus.Completed,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = product.Id, Qty = 2, UnitPrice = 5000m, LineDiscount = 1000m, Total = 10000m, ReturnedQty = 0 }
            }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = order.Items.First();
        var res = await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "partial", new List<ReturnItemRequest> { new(item.Id, 1) }));
        Assert.Equal(4500m, res.TotalReturned);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ReturnAfterFullyReturned_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await SeedOrderData(context);
        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = await context.OrderItems.FirstAsync();
        await service.CreateReturnAsync(1, new ReturnRequest(item.OrderId, "full", new List<ReturnItemRequest> { new(item.Id, item.Qty) }));

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateReturnAsync(1, new ReturnRequest(item.OrderId, "extra", new List<ReturnItemRequest> { new(item.Id, 1) })));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task TotalRefunded_NeverExceedsTotalCollected()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await SeedOrderData(context);
        var order = await context.Orders.FirstAsync();
        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = await context.OrderItems.FirstAsync();
        var res = await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "full", new List<ReturnItemRequest> { new(item.Id, item.Qty) }));
        Assert.True(res.TotalReturned <= order.Total);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task SurchargeGT0_MultipleReturns_SurchargeRefundedOnce()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await SeedOrderData(context);
        var order = await context.Orders.FirstAsync();
        order.SurchargeTotal = 5000m;
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = await context.OrderItems.FirstAsync();
        var res1 = await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "1", new List<ReturnItemRequest> { new(item.Id, 1) }));
        var res2 = await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "2", new List<ReturnItemRequest> { new(item.Id, 1) }));
        var total = res1.TotalReturned + res2.TotalReturned;
        Assert.Equal(25000m, total);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ConcurrentReturnsSameLine_OnlyOneSucceeds()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await SeedOrderData(context);
        var item = await context.OrderItems.FirstAsync();
        var orderId = item.OrderId;
        var itemId = item.Id;
        var qtyToReturn = item.Qty;
        await tx.CommitAsync();

        var (ctx1, tx1) = _fixture.CreateContextWithTransaction();
        var (ctx2, tx2) = _fixture.CreateContextWithTransaction();

        await ctx1.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '50ms'");
        await ctx2.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '50ms'");

        var service1 = new ReturnService(ctx1, new Mock<ILogger<ReturnService>>().Object);
        var service2 = new ReturnService(ctx2, new Mock<ILogger<ReturnService>>().Object);

        ReturnResponse? res1 = null;
        ReturnResponse? res2 = null;

        var task1 = Task.Run(async () => {
            try { res1 = await service1.CreateReturnAsync(1, new ReturnRequest(orderId, "r1", new List<ReturnItemRequest> { new(itemId, qtyToReturn) })); }
            catch { }
        });

        var task2 = Task.Run(async () => {
            try { res2 = await service2.CreateReturnAsync(1, new ReturnRequest(orderId, "r2", new List<ReturnItemRequest> { new(itemId, qtyToReturn) })); }
            catch { }
        });

        await Task.WhenAll(task1, task2);

        try { await tx1.RollbackAsync(); } catch { }
        try { await tx2.RollbackAsync(); } catch { }

        int successCount = (res1 != null ? 1 : 0) + (res2 != null ? 1 : 0);
        Assert.Equal(1, successCount);
    }
}
