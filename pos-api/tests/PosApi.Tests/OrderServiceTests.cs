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

        var user = new User
        {
            Username = "cashier_" + Guid.NewGuid().ToString().Substring(0, 8),
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

        var orderCount = await context.Orders.CountAsync();
        Assert.Equal(0, orderCount);

        var ledgerCount = await context.StockLedger.CountAsync();
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
}
