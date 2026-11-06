using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class OrderServiceTests
{
    private readonly DatabaseFixture _fixture = new();

    private async Task<(int userId, Product product)> SeedUserAndProduct(AppDbContext context, decimal initialStock = 100m, decimal retailPrice = 10000m)
    {
        var category = new Category { Name = "Test Category " + Guid.NewGuid(), IsActive = true };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "Test Product " + Guid.NewGuid().ToString().Substring(0, 4),
            Barcode = "TEST-" + Guid.NewGuid().ToString().Substring(0, 8),
            CategoryId = category.Id,
            RetailPrice = retailPrice,
            CurrentStock = initialStock,
            IsActive = true,
            IsWeighed = false,
            MinStock = 5
        };
        context.Products.Add(product);

        var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
        var user = new User
        {
            Username = "cashier_" + uniqueId,
            Email = "cashier_" + uniqueId + "@test.com",
            FullName = "Test Cashier",
            Role = UserRole.Cashier,
            PasswordHash = "hash"
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        return (user.Id, product);
    }

    [Fact]
    public async Task CreateOrderAsync_Success_ReducesStockCorrectly()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m);

        var req = new CreateOrderRequest(
            CustomerId: null,
            PaymentMethod: "Cash",
            PaidAmount: 200000m,
            Discount: 0m,
            Surcharges: new List<OrderSurchargeRequest>(),
            Items: new List<OrderItemRequest>
            {
                new(ProductId: product.Id, UnitId: null, Qty: 20, UnitPrice: 10000m)
            }
        );

        var loggerMock = new Mock<ILogger<OrderService>>();
        var service = new OrderService(context, loggerMock.Object);

        var result = await service.CreateOrderAsync(userId, req, null);

        Assert.NotNull(result);
        Assert.True(result.Id > 0);

        var updatedProduct = await context.Products.FindAsync(product.Id);
        Assert.NotNull(updatedProduct);
        Assert.Equal(80m, updatedProduct.CurrentStock);

        var ledgerEntry = await context.StockLedger.FirstOrDefaultAsync(l => l.ProductId == product.Id);
        Assert.NotNull(ledgerEntry);
        Assert.Equal(product.Id, ledgerEntry.ProductId);
        Assert.Equal(-20m, ledgerEntry.Qty);
        Assert.Equal(80m, ledgerEntry.Balance);

        var order = await context.Orders.FindAsync(result.Id);
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Completed, order.Status);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateOrderAsync_InsufficientStock_ThrowsException()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 10m);
        var ordersBefore = await context.Orders.CountAsync();

        var req = new CreateOrderRequest(
            CustomerId: null,
            PaymentMethod: "Cash",
            PaidAmount: 200000m,
            Discount: 0m,
            Surcharges: new List<OrderSurchargeRequest>(),
            Items: new List<OrderItemRequest>
            {
                new(ProductId: product.Id, UnitId: null, Qty: 20, UnitPrice: 10000m)
            }
        );

        var loggerMock = new Mock<ILogger<OrderService>>();
        var service = new OrderService(context, loggerMock.Object);

        var exception = await Assert.ThrowsAsync<StockShortageException>(
            () => service.CreateOrderAsync(userId, req, null)
        );

        Assert.Contains("Không đủ tồn kho", exception.Message);

        var updatedProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(10m, updatedProduct!.CurrentStock);

        var ordersAfter = await context.Orders.CountAsync();
        Assert.Equal(ordersBefore, ordersAfter);

        var ledgerCount = await context.StockLedger.CountAsync(l => l.ProductId == product.Id);
        Assert.Equal(0, ledgerCount);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateOrderAsync_Idempotency_Key_DoesNotDoubleStockDeduction()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m);

        var req = new CreateOrderRequest(
            CustomerId: null,
            PaymentMethod: "Cash",
            PaidAmount: 100000m,
            Discount: 0m,
            Surcharges: new List<OrderSurchargeRequest>(),
            Items: new List<OrderItemRequest>
            {
                new(ProductId: product.Id, UnitId: null, Qty: 10, UnitPrice: 10000m)
            }
        );

        var loggerMock = new Mock<ILogger<OrderService>>();
        var service = new OrderService(context, loggerMock.Object);
        var idempotencyKey = "test-idempotency-key-001";

        var result1 = await service.CreateOrderAsync(userId, req, idempotencyKey);
        var result2 = await service.CreateOrderAsync(userId, req, idempotencyKey);

        Assert.Equal(result1.Id, result2.Id);

        var updatedProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(90m, updatedProduct!.CurrentStock);

        var ledgerCount = await context.StockLedger.CountAsync(l => l.ProductId == product.Id);
        Assert.Equal(1, ledgerCount);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateOrderAsync_LedgerBalance_CorrectlyRecordsPostTransactionBalance()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 500m);

        var req = new CreateOrderRequest(
            CustomerId: null,
            PaymentMethod: "Cash",
            PaidAmount: 600000m,
            Discount: 0m,
            Surcharges: new List<OrderSurchargeRequest>(),
            Items: new List<OrderItemRequest>
            {
                new(ProductId: product.Id, UnitId: null, Qty: 50, UnitPrice: 10000m)
            }
        );

        var loggerMock = new Mock<ILogger<OrderService>>();
        var service = new OrderService(context, loggerMock.Object);

        var result = await service.CreateOrderAsync(userId, req, null);

        var ledgerEntry = await context.StockLedger.FirstOrDefaultAsync(l => l.ProductId == product.Id);
        Assert.NotNull(ledgerEntry);
        Assert.Equal(450m, ledgerEntry!.Balance);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateOrderAsync_NewFields_ReturnDefaultValues()
    {
        // chưa triển khai, xem giai đoạn B
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m);

        var req = new CreateOrderRequest(
            CustomerId: null,
            PaymentMethod: "Cash",
            PaidAmount: 50000m,
            Discount: 0m,
            Surcharges: new List<OrderSurchargeRequest>(),
            Items: new List<OrderItemRequest>
            {
                new(ProductId: product.Id, UnitId: null, Qty: 1, UnitPrice: 10000m)
            }
        );

        var loggerMock = new Mock<ILogger<OrderService>>();
        var service = new OrderService(context, loggerMock.Object);

        var result = await service.CreateOrderAsync(userId, req, null);

        Assert.Equal(0m, result.VipDiscount);
        Assert.Equal(0, result.PointsRedeemed);
        Assert.Equal(0m, result.PointsReduction);
        Assert.Equal(0m, result.DebtCollected);
        Assert.NotNull(result.DebtPayments);
        Assert.Empty(result.DebtPayments);
        Assert.Equal("instore", result.DeliveryMethod);
        Assert.Equal(0m, result.ShippingFee);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateOrderAsync_VipCustomer_Applies5PercentDiscountAndSumsVat()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 20000m);

        var cust = new Customer
        {
            Name = "VIP Customer",
            Phone = "090" + Guid.NewGuid().ToString().Substring(0, 7),
            IsVip = true,
            IsActive = true
        };
        context.Customers.Add(cust);
        await context.SaveChangesAsync();

        var req = new CreateOrderRequest(
            CustomerId: cust.Id,
            PaymentMethod: "Cash",
            PaidAmount: 500000m,
            Discount: 0m,
            Surcharges: new List<OrderSurchargeRequest>(),
            Items: new List<OrderItemRequest>
            {
                new(ProductId: product.Id, UnitId: null, Qty: 5, UnitPrice: 20000m)
            }
        );

        var loggerMock = new Mock<ILogger<OrderService>>();
        var service = new OrderService(context, loggerMock.Object);

        var result = await service.CreateOrderAsync(userId, req, null);
        var order = await context.Orders.Include(o => o.Items).FirstAsync(o => o.Id == result.Id);

        Assert.Equal(100000m, order.Subtotal);
        Assert.Equal(5000m, order.Discount);

        var sumItemVats = order.Items.Sum(i => i.Vat);
        Assert.Equal(order.Vat, sumItemVats);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateOrderAsync_ShopDelivery_AddsShippingFee()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 10000m);
        var req = new CreateOrderRequest(null, "Cash", 200000m, 0m, new(), new() { new(product.Id, null, 1, 10000m) }, DeliveryMethod: "shopdelivery", ShippingFee: 15000m);
        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        var res = await service.CreateOrderAsync(userId, req, null);
        var order = await context.Orders.FindAsync(res.Id);
        Assert.Equal("shopdelivery", order!.DeliveryMethod);
        Assert.Equal(15000m, order.ShippingFee);
        Assert.Equal(25000m, order.Total);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateOrderAsync_InvalidDeliveryMethod_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 10000m);
        var req = new CreateOrderRequest(null, "Cash", 200000m, 0m, new(), new() { new(product.Id, null, 1, 10000m) }, DeliveryMethod: "invalid", ShippingFee: 0m);
        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateOrderAsync(userId, req, null));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CreateOrderAsync_NegativeShippingFee_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 10000m);
        var req = new CreateOrderRequest(null, "Cash", 200000m, 0m, new(), new() { new(product.Id, null, 1, 10000m) }, DeliveryMethod: "shopdelivery", ShippingFee: -5000m);
        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateOrderAsync(userId, req, null));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task InStore_ForcesShippingFeeToZero()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 10000m);
        var req = new CreateOrderRequest(null, "Cash", 200000m, 0m, new(), new() { new(product.Id, null, 1, 10000m) }, DeliveryMethod: "instore", ShippingFee: 30000m);
        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        var res = await service.CreateOrderAsync(userId, req, null);
        var order = await context.Orders.FindAsync(res.Id);
        Assert.Equal("instore", order!.DeliveryMethod);
        Assert.Equal(0m, order.ShippingFee);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task SameIdempotencyKey_ChangedShippingFee_Returns409()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 10000m);
        var req1 = new CreateOrderRequest(null, "Cash", 200000m, 0m, new(), new() { new(product.Id, null, 1, 10000m) }, DeliveryMethod: "shopdelivery", ShippingFee: 10000m);
        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        await service.CreateOrderAsync(userId, req1, "key-123");

        var req2 = new CreateOrderRequest(null, "Cash", 200000m, 0m, new(), new() { new(product.Id, null, 1, 10000m) }, DeliveryMethod: "shopdelivery", ShippingFee: 20000m);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.CreateOrderAsync(userId, req2, "key-123"));
        Assert.Equal("Idempotency-Key đã được dùng cho nội dung khác", ex.Message);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task SameIdempotencyKey_ChangedDeliveryMethod_Returns409()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 10000m);
        var req1 = new CreateOrderRequest(null, "Cash", 200000m, 0m, new(), new() { new(product.Id, null, 1, 10000m) }, DeliveryMethod: "shopdelivery", ShippingFee: 10000m);
        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        await service.CreateOrderAsync(userId, req1, "key-456");

        var req2 = new CreateOrderRequest(null, "Cash", 200000m, 0m, new(), new() { new(product.Id, null, 1, 10000m) }, DeliveryMethod: "carrier", ShippingFee: 10000m);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.CreateOrderAsync(userId, req2, "key-456"));
        Assert.Equal("Idempotency-Key đã được dùng cho nội dung khác", ex.Message);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task IdempotencyRecord_NullRequestHash_StillReturnsCachedResult()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 10000m);
        context.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = "legacy-key",
            UserId = userId,
            Endpoint = "POST /api/v1/orders",
            RequestHash = null,
            ResultJson = "{\"id\":999,\"code\":\"INV-TEST\"}",
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        });
        await context.SaveChangesAsync();

        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        var req = new CreateOrderRequest(null, "Cash", 200000m, 0m, new(), new() { new(product.Id, null, 1, 10000m) });
        var res = await service.CreateOrderAsync(userId, req, "legacy-key");
        Assert.Equal(999, res.Id);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ShippingFee_NotIncludedInLoyaltyPointsAndLifetimeSpend()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 10000m);
        var cust = new Customer { Name = "C", Phone = "09" + Guid.NewGuid().ToString().Substring(0, 8), IsActive = true };
        context.Customers.Add(cust);
        await context.SaveChangesAsync();

        var req = new CreateOrderRequest(cust.Id, "Cash", 200000m, 0m, new(), new() { new(product.Id, null, 1, 10000m) }, DeliveryMethod: "shopdelivery", ShippingFee: 20000m);
        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        await service.CreateOrderAsync(userId, req, null);

        var loyalty = await context.LoyaltyAccounts.FirstOrDefaultAsync(l => l.CustomerId == cust.Id);
        Assert.NotNull(loyalty);
        Assert.Equal(10, loyalty.Points);
        Assert.Equal(10000m, loyalty.LifetimeSpend);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task LegacyEmptyDeliveryMethod_ReadsAsInstore()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 10000m);
        var order = new Order
        {
            Code = "INV-LEGACY",
            CashierId = userId,
            Subtotal = 10000m,
            Total = 10000m,
            DeliveryMethod = "",
            Status = OrderStatus.Completed
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var fetched = await context.Orders.FirstAsync(o => o.Code == "INV-LEGACY");
        var dm = string.IsNullOrWhiteSpace(fetched.DeliveryMethod) ? "instore" : fetched.DeliveryMethod;
        Assert.Equal("instore", dm);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Vip_WithManualDiscount_SumsBoth()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 100000m);
        var cust = new Customer { Name = "VIP", Phone = "0900000000", IsVip = true, IsActive = true };
        context.Customers.Add(cust);
        await context.SaveChangesAsync();

        var req = new CreateOrderRequest(cust.Id, "Cash", 500000m, 10000m, new(), new() { new(product.Id, null, 1, 100000m) });
        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        var res = await service.CreateOrderAsync(userId, req, null);
        var order = await context.Orders.FindAsync(res.Id);
        Assert.Equal(15000m, order!.Discount);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ManualDiscountEqualsSubtotal_CappedAtSubtotal()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 100000m);
        var cust = new Customer { Name = "VIP", Phone = "0900000000", IsVip = true, IsActive = true };
        context.Customers.Add(cust);
        await context.SaveChangesAsync();

        var req = new CreateOrderRequest(cust.Id, "Cash", 500000m, 100000m, new(), new() { new(product.Id, null, 1, 100000m) });
        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        var res = await service.CreateOrderAsync(userId, req, null);
        var order = await context.Orders.FindAsync(res.Id);
        Assert.Equal(100000m, order!.Discount);
        Assert.Equal(0m, order.Total);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task NonVip_NoVipDiscount()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 100000m);
        var cust = new Customer { Name = "Regular", Phone = "0901111111", IsVip = false, IsActive = true };
        context.Customers.Add(cust);
        await context.SaveChangesAsync();

        var req = new CreateOrderRequest(cust.Id, "Cash", 500000m, 0m, new(), new() { new(product.Id, null, 1, 100000m) });
        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        var res = await service.CreateOrderAsync(userId, req, null);
        var order = await context.Orders.FindAsync(res.Id);
        Assert.Equal(0m, order!.Discount);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task VipPercentConfigChanged_ChangesDiscount()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 100000m);
        var cust = new Customer { Name = "VIP", Phone = "0902222222", IsVip = true, IsActive = true };
        context.Customers.Add(cust);
        await context.SaveChangesAsync();

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "VipDiscountPercent", "10" } })
            .Build();

        var req = new CreateOrderRequest(cust.Id, "Cash", 500000m, 0m, new(), new() { new(product.Id, null, 1, 100000m) });
        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object, null, config);
        var res = await service.CreateOrderAsync(userId, req, null);
        var order = await context.Orders.FindAsync(res.Id);
        Assert.Equal(10000m, order!.Discount);
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task OrderWithSurcharge_SumOrderItemVat_EqualsOrderVat()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var (userId, product) = await SeedUserAndProduct(context, 100m, 10000m);
        var surcharge = new Surcharge { Name = "Phụ phí", Type = SurchargeType.Percent, Value = 10m, IsActive = true, ApplyMode = SurchargeApply.Auto };
        context.Surcharges.Add(surcharge);
        await context.SaveChangesAsync();

        var req = new CreateOrderRequest(null, "Cash", 200000m, 0m, new List<OrderSurchargeRequest> { new(surcharge.Id, 10m) }, new() { new(product.Id, null, 2, 10000m) });
        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        var res = await service.CreateOrderAsync(userId, req, null);
        var order = await context.Orders.Include(o => o.Items).FirstAsync(o => o.Id == res.Id);
        Assert.Equal(order.Vat, order.Items.Sum(i => i.Vat));
        await tx.RollbackAsync();
    }

    [Fact]
    public async Task SumLineDiscount_EqualsOrderDiscount()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var category = new Category { Name = "Cat " + Guid.NewGuid(), IsActive = true };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var p1 = new Product { Name = "P1", Barcode = "P1-" + Guid.NewGuid().ToString().Substring(0, 6), CategoryId = category.Id, RetailPrice = 12345.67m, CurrentStock = 10m, IsActive = true };
        var p2 = new Product { Name = "P2", Barcode = "P2-" + Guid.NewGuid().ToString().Substring(0, 6), CategoryId = category.Id, RetailPrice = 23456.78m, CurrentStock = 10m, IsActive = true };
        context.Products.AddRange(p1, p2);

        var cust = new Customer { Name = "VIP Cust", Phone = "09" + Guid.NewGuid().ToString().Substring(0, 8), IsVip = true, IsActive = true };
        context.Customers.Add(cust);
        var uniqueId = Guid.NewGuid().ToString().Substring(0, 8);
        var cashier = new User { Username = "u_" + uniqueId, Email = "u_" + uniqueId + "@test.com", FullName = "U", Role = UserRole.Manager, PasswordHash = "x" };
        context.Users.Add(cashier);
        await context.SaveChangesAsync();

        var req = new CreateOrderRequest(
            CustomerId: cust.Id,
            PaymentMethod: "Cash",
            PaidAmount: 500000m,
            Discount: 1000m,
            Surcharges: new(),
            Items: new List<OrderItemRequest>
            {
                new(p1.Id, null, 2, 12345.67m),
                new(p2.Id, null, 1, 23456.78m)
            }
        );

        var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);
        var res = await service.CreateOrderAsync(cashier.Id, req, null);
        var order = await context.Orders.Include(o => o.Items).FirstAsync(o => o.Id == res.Id);

        Assert.Equal(order.Discount, order.Items.Sum(i => i.LineDiscount));
        await tx.RollbackAsync();
    }
}
