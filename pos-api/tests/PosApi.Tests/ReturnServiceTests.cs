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
            Barcode = "RET001",
            CategoryId = category.Id,
            RetailPrice = 10000m,
            CurrentStock = 50m,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var cashier = new User { Username = "admin" + Guid.NewGuid(), FullName = "Admin", Role = UserRole.Manager, PasswordHash = "x" };
        context.Users.Add(cashier);
        await context.SaveChangesAsync();

        var order = new Order
        {
            Code = "INV-20261010-0001",
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

        var ledger = await context.StockLedger.FirstOrDefaultAsync();
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
}
