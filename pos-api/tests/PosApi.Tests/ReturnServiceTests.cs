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

    private async Task<(int userId, Product product)> SeedUserAndProduct(AppDbContext context, decimal stock = 100m, decimal price = 10000m)
    {
        var category = new Category { Name = "Cat " + Guid.NewGuid(), IsActive = true };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "Product " + Guid.NewGuid(),
            Barcode = "PROD-" + Guid.NewGuid().ToString().Substring(0, 8),
            CategoryId = category.Id,
            RetailPrice = price,
            CurrentStock = stock,
            IsActive = true
        };
        context.Products.Add(product);

        var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
        var cashier = new User
        {
            Username = "cashier_" + uniqueId,
            Email = "cashier_" + uniqueId + "@test.com",
            FullName = "Cashier",
            Role = UserRole.Cashier,
            PasswordHash = "x"
        };
        context.Users.Add(cashier);
        await context.SaveChangesAsync();

        return (cashier.Id, product);
    }

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
    public async Task CreateReturnAsync_Idempotency_ReturnsSameResult()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await SeedOrderData(context);
        var loggerMock = new Mock<ILogger<ReturnService>>();
        var service = new ReturnService(context, loggerMock.Object);
        var orderItem = await context.OrderItems.FirstAsync();
        var req = new ReturnRequest(orderItem.OrderId, "Idempotent return", new List<ReturnItemRequest>
        {
            new(orderItem.Id, 1)
        });
        var key = "idem-return-key-1";

        var returnsBefore = await context.Returns.CountAsync();
        var ledgerBefore = await context.StockLedger.CountAsync();

        var res1 = await service.CreateReturnAsync(1, req, key);
        var res2 = await service.CreateReturnAsync(1, req, key);

        Assert.Equal(res1.Code, res2.Code);
        Assert.Equal(res1.TotalReturned, res2.TotalReturned);

        var returnsAfter = await context.Returns.CountAsync();
        var ledgerAfter = await context.StockLedger.CountAsync();

        Assert.Equal(returnsBefore + 1, returnsAfter);
        Assert.Equal(ledgerBefore + 1, ledgerAfter);

        await tx.RollbackAsync();
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
            RetailPrice = 33334m,
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
            Subtotal = 100000m,
            Total = 100000m,
            Status = OrderStatus.Completed,
            Items = new List<OrderItem>
            {
                new OrderItem
                {
                    ProductId = product.Id,
                    Qty = 3,
                    UnitPrice = 33334m,
                    LineDiscount = 2m,
                    Total = 100000m,
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

        Assert.Equal(33333m, res1.TotalReturned);
        Assert.Equal(33333m, res2.TotalReturned);
        Assert.Equal(33334m, res3.TotalReturned);

        var totalRefunded = res1.TotalReturned + res2.TotalReturned + res3.TotalReturned;
        Assert.Equal(100000m, totalRefunded);

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

        await ctx1.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '2000ms'");
        await ctx2.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '2000ms'");

        var service1 = new ReturnService(ctx1, new Mock<ILogger<ReturnService>>().Object);
        var service2 = new ReturnService(ctx2, new Mock<ILogger<ReturnService>>().Object);

        Exception? ex1 = null;
        Exception? ex2 = null;
        ReturnResponse? res1 = null;
        ReturnResponse? res2 = null;

        var task1 = Task.Run(async () => {
            try { res1 = await service1.CreateReturnAsync(1, new ReturnRequest(orderId, "r1", new List<ReturnItemRequest> { new(itemId, qtyToReturn) })); }
            catch (Exception ex) { ex1 = ex; }
        });

        var task2 = Task.Run(async () => {
            try { res2 = await service2.CreateReturnAsync(1, new ReturnRequest(orderId, "r2", new List<ReturnItemRequest> { new(itemId, qtyToReturn) })); }
            catch (Exception ex) { ex2 = ex; }
        });

        await Task.WhenAll(task1, task2);

        try { await tx1.RollbackAsync(); } catch { }
        try { await tx2.RollbackAsync(); } catch { }

        bool IsExpectedFail(Exception? ex) => ex is BusinessRuleException || (ex is InvalidOperationException);

        int successCount = (res1 != null ? 1 : 0) + (res2 != null ? 1 : 0);
        int failCount = (IsExpectedFail(ex1) ? 1 : 0) + (IsExpectedFail(ex2) ? 1 : 0);

        Assert.Equal(1, successCount);
        Assert.Equal(1, failCount);
    }

    [Fact]
    public async Task Return_FullReturn_RevokesAllEarnedPoints()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var customer = new Customer { Name = "Cust", Phone = "09" + Guid.NewGuid().ToString().Substring(0, 8), IsActive = true };
        customer.Loyalty = new LoyaltyAccount { Points = 50, LifetimeSpend = 20000m };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

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
            CustomerId = customer.Id,
            Subtotal = 10000m,
            Total = 10000m,
            Status = OrderStatus.Completed,
            LoyaltyEarned = 10,
            LoyaltyRevoked = 0,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = product.Id, Qty = 1, UnitPrice = 10000m, Total = 10000m, ReturnedQty = 0 }
            }
        };
        customer.Loyalty.Points = 60;
        customer.Loyalty.LifetimeSpend = 30000m;
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = order.Items.First();
        await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "full", new List<ReturnItemRequest> { new(item.Id, 1) }));

        var loyalty = await context.LoyaltyAccounts.FirstAsync(l => l.CustomerId == customer.Id);
        Assert.Equal(50, loyalty.Points);
        Assert.Equal(20000m, loyalty.LifetimeSpend);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Return_PartialReturn_RevokesProportionalPoints()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var customer = new Customer { Name = "Cust", Phone = "09" + Guid.NewGuid().ToString().Substring(0, 8), IsActive = true };
        customer.Loyalty = new LoyaltyAccount { Points = 50, LifetimeSpend = 50000m };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

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
            CustomerId = customer.Id,
            Subtotal = 20000m,
            Total = 20000m,
            Status = OrderStatus.Completed,
            LoyaltyEarned = 20,
            LoyaltyRevoked = 0,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = product.Id, Qty = 2, UnitPrice = 10000m, Total = 20000m, ReturnedQty = 0 }
            }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = order.Items.First();
        await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "partial", new List<ReturnItemRequest> { new(item.Id, 1) }));

        var loyalty = await context.LoyaltyAccounts.FirstAsync(l => l.CustomerId == customer.Id);
        Assert.Equal(40, loyalty.Points); // 50 (ban đầu) - 10 (thu hồi) = 40
        Assert.Equal(40000m, loyalty.LifetimeSpend);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Return_InsufficientPoints_SetsPointsToZeroAndRecordsShortfall()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var customer = new Customer { Name = "Cust", Phone = "09" + Guid.NewGuid().ToString().Substring(0, 8), IsActive = true };
        customer.Loyalty = new LoyaltyAccount { Points = 2, LifetimeSpend = 20000m };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

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
            CustomerId = customer.Id,
            Subtotal = 10000m,
            Total = 10000m,
            Status = OrderStatus.Completed,
            LoyaltyEarned = 10,
            LoyaltyRevoked = 0,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = product.Id, Qty = 1, UnitPrice = 10000m, Total = 10000m, ReturnedQty = 0 }
            }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = order.Items.First();
        var res = await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "full", new List<ReturnItemRequest> { new(item.Id, 1) }));
        Assert.NotNull(res);

        var loyalty = await context.LoyaltyAccounts.FirstAsync(l => l.CustomerId == customer.Id);
        Assert.Equal(0, loyalty.Points);

        var txRecord = await context.PointTransactions.OrderByDescending(t => t.Id).FirstAsync();
        Assert.Equal(PointTransactionType.Revoke, txRecord.Type);
        Assert.Equal(-10, txRecord.Points);
        Assert.Equal(8, txRecord.Shortfall);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Return_OldOrderZeroLoyaltyEarned_DoesNotRevoke()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var customer = new Customer { Name = "Cust", Phone = "09" + Guid.NewGuid().ToString().Substring(0, 8), IsActive = true };
        customer.Loyalty = new LoyaltyAccount { Points = 50, LifetimeSpend = 20000m };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

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
            CustomerId = customer.Id,
            Subtotal = 10000m,
            Total = 10000m,
            Status = OrderStatus.Completed,
            LoyaltyEarned = 0, // Old order
            LoyaltyRevoked = 0,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = product.Id, Qty = 1, UnitPrice = 10000m, Total = 10000m, ReturnedQty = 0 }
            }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = order.Items.First();
        await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "full", new List<ReturnItemRequest> { new(item.Id, 1) }));

        var loyalty = await context.LoyaltyAccounts.FirstAsync(l => l.CustomerId == customer.Id);
        Assert.Equal(50, loyalty.Points); // unchanged
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Return_WalkInCustomer_NoLoyaltyError()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await SeedOrderData(context);
        var order = await context.Orders.FirstAsync();
        order.CustomerId = null;
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = await context.OrderItems.FirstAsync();
        var res = await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "walkin", new List<ReturnItemRequest> { new(item.Id, 1) }));
        Assert.NotNull(res);

        var ptCount = await context.PointTransactions.CountAsync();
        Assert.Equal(0, ptCount);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Return_OrderWithShippingFee_ShippingFeeNotCountedInRevocation()
    {
        // Tính tay: Đơn 2 món x 10.000đ = 20.000đ hàng, ShippingFee = 15.000đ, Total = 35.000đ.
        // LoyaltyEarned = 20 (chỉ tính trên goods 20k). Points ban đầu = 50, LifetimeSpend = 50.000m.
        // Trả 1 món (goodsRefunded = 10.000đ). Revoke = floor(20 * 10k / 20k) = 10.
        // Points sau trả = 50 - 10 = 40. LifetimeSpend = 50k - 10k = 40.000m.
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var customer = new Customer { Name = "Cust", Phone = "09" + Guid.NewGuid().ToString().Substring(0, 8), IsActive = true };
        customer.Loyalty = new LoyaltyAccount { Points = 50, LifetimeSpend = 50000m };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

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
            CustomerId = customer.Id,
            Subtotal = 20000m,
            ShippingFee = 15000m,
            Total = 35000m,
            Status = OrderStatus.Completed,
            LoyaltyEarned = 20,
            LoyaltyRevoked = 0,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = product.Id, Qty = 2, UnitPrice = 10000m, Total = 20000m, ReturnedQty = 0 }
            }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = order.Items.First();
        await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "ship", new List<ReturnItemRequest> { new(item.Id, 1) }));

        var loyalty = await context.LoyaltyAccounts.FirstAsync(l => l.CustomerId == customer.Id);
        Assert.Equal(40, loyalty.Points);
        Assert.Equal(40000m, loyalty.LifetimeSpend);

        var txRecord = await context.PointTransactions.OrderByDescending(t => t.Id).FirstAsync();
        Assert.Equal(PointTransactionType.Revoke, txRecord.Type);
        Assert.Equal(-10, txRecord.Points);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Return_Fractional3Returns_ExactTotalRevocation()
    {
        // Đơn 100.000đ nhận 100 điểm (100.000 / 1.000 = 100), 3 lần trả mỗi lần 1 qty (33.333 / 33.333 / 33.334).
        // Tổng thu hồi đúng 100 điểm, lần cuối nhận phần dư.
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var customer = new Customer { Name = "Cust", Phone = "09" + Guid.NewGuid().ToString().Substring(0, 8), IsActive = true };
        customer.Loyalty = new LoyaltyAccount { Points = 150, LifetimeSpend = 100000m };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var category = new Category { Name = "Cat " + Guid.NewGuid(), IsActive = true };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "P", Barcode = "P-" + Guid.NewGuid().ToString().Substring(0, 8), CategoryId = category.Id, RetailPrice = 33334m, CurrentStock = 50m, IsActive = true };
        context.Products.Add(product);
        var cashier = new User { Username = "u_" + Guid.NewGuid(), Email = "u_" + Guid.NewGuid() + "@test.com", FullName = "U", Role = UserRole.Manager, PasswordHash = "x" };
        context.Users.Add(cashier);
        await context.SaveChangesAsync();

        var order = new Order
        {
            Code = "INV-" + Guid.NewGuid().ToString().Substring(0, 6),
            CashierId = cashier.Id,
            CustomerId = customer.Id,
            Subtotal = 100000m,
            Total = 100000m,
            Status = OrderStatus.Completed,
            LoyaltyEarned = 100,
            LoyaltyRevoked = 0,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = product.Id, Qty = 3, UnitPrice = 33334m, LineDiscount = 2m, Total = 100000m, ReturnedQty = 0 }
            }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = order.Items.First();

        var res1 = await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "r1", new List<ReturnItemRequest> { new(item.Id, 1) }));
        var res2 = await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "r2", new List<ReturnItemRequest> { new(item.Id, 1) }));
        var res3 = await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "r3", new List<ReturnItemRequest> { new(item.Id, 1) }));

        Assert.Equal(33333m, res1.TotalReturned);
        Assert.Equal(33333m, res2.TotalReturned);
        Assert.Equal(33334m, res3.TotalReturned);

        var loyalty = await context.LoyaltyAccounts.FirstAsync(l => l.CustomerId == customer.Id);
        Assert.Equal(50, loyalty.Points); // 150 - 100 = 50
        var reloadedOrder = await context.Orders.FindAsync(order.Id);
        Assert.Equal(100, reloadedOrder!.LoyaltyRevoked);

        var revTxs = await context.PointTransactions
            .Where(t => t.OrderId == order.Id && t.Type == PointTransactionType.Revoke)
            .OrderBy(t => t.Id)
            .ToListAsync();
        Assert.Equal(3, revTxs.Count);
        // Lần 1: floor(100 * (33333 / 100000)) = floor(33.333) = 33
        Assert.Equal(-33, revTxs[0].Points);
        // Lần 2: tổng thu hồi tích lũy floor(100 * (66666 / 100000)) = floor(66.666) = 66, trừ đi 33 đã thu = 33
        Assert.Equal(-33, revTxs[1].Points);
        // Lần 3: trả hết, thu nốt phần còn lại: 100 - 33 - 33 = 34
        Assert.Equal(-34, revTxs[2].Points);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Return_AfterFullyReturned_Throws400_LoyaltyRevokedUnchanged()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await SeedOrderData(context);
        var order = await context.Orders.FirstAsync();
        order.CustomerId = 1;
        order.LoyaltyEarned = 20;
        var cust = new Customer { Id = 1, Name = "C", Phone = "0900000123", IsActive = true };
        cust.Loyalty = new LoyaltyAccount { Points = 20, LifetimeSpend = 20000m };
        context.Customers.Add(cust);
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = await context.OrderItems.FirstAsync();
        await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "full", new List<ReturnItemRequest> { new(item.Id, item.Qty) }));

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateReturnAsync(1, new ReturnRequest(order.Id, "extra", new List<ReturnItemRequest> { new(item.Id, 1) })));

        var reloadedOrder = await context.Orders.FindAsync(order.Id);
        Assert.Equal(20, reloadedOrder!.LoyaltyRevoked);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Return_LoyaltyRevoked_NeverExceedsLoyaltyEarned()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var customer = new Customer { Name = "Cust", Phone = "09" + Guid.NewGuid().ToString().Substring(0, 8), IsActive = true };
        customer.Loyalty = new LoyaltyAccount { Points = 100, LifetimeSpend = 50000m };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

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
            CustomerId = customer.Id,
            Subtotal = 10000m,
            Total = 10000m,
            Status = OrderStatus.Completed,
            LoyaltyEarned = 10,
            LoyaltyRevoked = 0,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = product.Id, Qty = 1, UnitPrice = 10000m, Total = 10000m, ReturnedQty = 0 }
            }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = order.Items.First();
        await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "r1", new List<ReturnItemRequest> { new(item.Id, 1) }));

        var reloadedOrder = await context.Orders.FindAsync(order.Id);
        Assert.True(reloadedOrder!.LoyaltyRevoked <= reloadedOrder.LoyaltyEarned);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Return_PointsNeverNegative()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var customer = new Customer { Name = "Cust", Phone = "09" + Guid.NewGuid().ToString().Substring(0, 8), IsActive = true };
        customer.Loyalty = new LoyaltyAccount { Points = 0, LifetimeSpend = 50000m };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

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
            CustomerId = customer.Id,
            Subtotal = 10000m,
            Total = 10000m,
            Status = OrderStatus.Completed,
            LoyaltyEarned = 10,
            LoyaltyRevoked = 0,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = product.Id, Qty = 1, UnitPrice = 10000m, Total = 10000m, ReturnedQty = 0 }
            }
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = order.Items.First();
        await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "r1", new List<ReturnItemRequest> { new(item.Id, 1) }));

        var loyalty = await context.LoyaltyAccounts.FirstAsync(l => l.CustomerId == customer.Id);
        Assert.True(loyalty.Points >= 0);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Return_LifetimeSpend_DecreasesByRefundedGoods_EvenWhenRevokeIsZero()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var customer = new Customer { Name = "Cust", Phone = "09" + Guid.NewGuid().ToString().Substring(0, 8), IsActive = true };
        customer.Loyalty = new LoyaltyAccount { Points = 10, LifetimeSpend = 5000m };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        var category = new Category { Name = "Cat " + Guid.NewGuid(), IsActive = true };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "P", Barcode = "P-" + Guid.NewGuid().ToString().Substring(0, 8), CategoryId = category.Id, RetailPrice = 500m, CurrentStock = 50m, IsActive = true };
        context.Products.Add(product);
        var cashier = new User { Username = "u_" + Guid.NewGuid(), Email = "u_" + Guid.NewGuid() + "@test.com", FullName = "U", Role = UserRole.Manager, PasswordHash = "x" };
        context.Users.Add(cashier);
        await context.SaveChangesAsync();

        var order = new Order
        {
            Code = "INV-" + Guid.NewGuid().ToString().Substring(0, 6),
            CashierId = cashier.Id,
            CustomerId = customer.Id,
            Subtotal = 500m,
            Total = 500m,
            Status = OrderStatus.Completed,
            LoyaltyEarned = 0,
            LoyaltyRevoked = 0,
            Items = new List<OrderItem>
            {
                new OrderItem { ProductId = product.Id, Qty = 1, UnitPrice = 500m, Total = 500m, ReturnedQty = 0 }
            }
        };
        customer.Loyalty.Points = 5;
        customer.Loyalty.LifetimeSpend = 5000m;
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var service = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var item = order.Items.First();
        await service.CreateReturnAsync(1, new ReturnRequest(order.Id, "small", new List<ReturnItemRequest> { new(item.Id, 1) }));

        var loyalty = await context.LoyaltyAccounts.FirstAsync(l => l.CustomerId == customer.Id);
        Assert.Equal(5, loyalty.Points); // revoke = 0
        Assert.Equal(4500m, loyalty.LifetimeSpend); // decreased by 500đ goods refunded
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task EndToEnd_OrderAndPartialReturns_TotalRevokedEqualsEarned()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 10000m);
        var cust = new Customer { Name = "C", Phone = "09" + Guid.NewGuid().ToString().Substring(0, 8), IsActive = true };
        context.Customers.Add(cust);
        await context.SaveChangesAsync();

        var orderService = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        var orderReq = new CreateOrderRequest(cust.Id, "Cash", 50000m, 0m, new(), new() { new(product.Id, null, 2, 10000m) });
        var orderRes = await orderService.CreateOrderAsync(userId, orderReq, null);

        Assert.Equal(20, orderRes.LoyaltyEarned);

        var returnService = new ReturnService(context, new Mock<ILogger<ReturnService>>().Object);
        var orderItem = (await context.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderRes.Id)).Items.First();

        await returnService.CreateReturnAsync(userId, new ReturnRequest(orderRes.Id, "r1", new List<ReturnItemRequest> { new(orderItem.Id, 1) }));
        await returnService.CreateReturnAsync(userId, new ReturnRequest(orderRes.Id, "r2", new List<ReturnItemRequest> { new(orderItem.Id, 1) }));

        var completedOrder = await context.Orders.FindAsync(orderRes.Id);
        Assert.Equal(completedOrder!.LoyaltyEarned, completedOrder.LoyaltyRevoked);
        await tx.RollbackAsync();
    }

}
