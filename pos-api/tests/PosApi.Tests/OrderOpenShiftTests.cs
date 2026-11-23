using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class OrderOpenShiftTests
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

    private async Task<(int userId, Product product)> SeedUserAndProduct(AppDbContext context, decimal initialStock = 100m, decimal retailPrice = 10000m)
    {
        await EnsureWarehouseAsync(context);
        var wh = await context.Warehouses.FirstAsync(w => w.IsDefault && w.IsActive);
        var category = new Category { Name = "Test Cat " + Guid.NewGuid(), IsActive = true };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "Test Product " + Guid.NewGuid().ToString().Substring(0, 4),
            Barcode = "TEST-" + Guid.NewGuid().ToString().Substring(0, 8),
            CategoryId = category.Id,
            RetailPrice = retailPrice,
            CurrentStock = initialStock,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        if (initialStock > 0)
        {
            var existingStock = await context.WarehouseStocks
                .FirstOrDefaultAsync(ws => ws.WarehouseId == wh.Id && ws.ProductId == product.Id);
            if (existingStock == null)
            {
                context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = initialStock });
                await context.SaveChangesAsync();
            }
            else
            {
                existingStock.Stock = initialStock;
                await context.SaveChangesAsync();
            }
        }

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
    public async Task NoOpenShift_Throws400_WritesNothing()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var (userId, product) = await SeedUserAndProduct(context, 100m);
            var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);

            var req = new CreateOrderRequest(
                CustomerId: null,
                PaymentMethod: "Cash",
                PaidAmount: 200000m,
                Discount: 0m,
                Surcharges: new(),
                Items: new List<OrderItemRequest> { new(product.Id, null, 1m, 10000m) }
            );

            var idempotencyKey = "IDEMP-NO-SHIFT-" + Guid.NewGuid();

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.CreateOrderAsync(userId, req, idempotencyKey)
            );
            Assert.Contains("Chưa mở ca làm việc", ex.Message);

            // Assert nothing written
            Assert.Equal(0, await context.Orders.CountAsync(o => o.CashierId == userId));
            var prodAfter = await context.Products.FindAsync(product.Id);
            Assert.Equal(100m, prodAfter!.CurrentStock);
            Assert.Equal(0, await context.IdempotencyRecords.CountAsync(r => r.Key == idempotencyKey));

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task WithOpenShift_AssignsShiftId()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var (userId, product) = await SeedUserAndProduct(context, 100m);
            var shift = await ShiftTestHelper.EnsureOpenShiftAsync(context, userId);
            var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);

            var req = new CreateOrderRequest(
                CustomerId: null,
                PaymentMethod: "Cash",
                PaidAmount: 200000m,
                Discount: 0m,
                Surcharges: new(),
                Items: new List<OrderItemRequest> { new(product.Id, null, 1m, 10000m) }
            );

            var res = await service.CreateOrderAsync(userId, req, null);
            Assert.NotNull(res);

            var order = await context.Orders.FindAsync(res.Id);
            Assert.NotNull(order);
            Assert.Equal(shift.Id, order.ShiftId);

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task ClosedShift_Throws()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var (userId, product) = await SeedUserAndProduct(context, 100m);
            var shift = await ShiftTestHelper.EnsureOpenShiftAsync(context, userId);

            // Close the shift
            shift.Status = ShiftStatus.Closed;
            shift.EndedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();

            var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);

            var req = new CreateOrderRequest(
                CustomerId: null,
                PaymentMethod: "Cash",
                PaidAmount: 200000m,
                Discount: 0m,
                Surcharges: new(),
                Items: new List<OrderItemRequest> { new(product.Id, null, 1m, 10000m) }
            );

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.CreateOrderAsync(userId, req, null)
            );
            Assert.Contains("Chưa mở ca làm việc", ex.Message);

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task OtherUsersOpenShift_DoesNotCount()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var (userIdA, product) = await SeedUserAndProduct(context, 100m);
            var (userIdB, _) = await SeedUserAndProduct(context, 100m);

            // User A opens shift, User B does not
            await ShiftTestHelper.EnsureOpenShiftAsync(context, userIdA);

            var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);

            var req = new CreateOrderRequest(
                CustomerId: null,
                PaymentMethod: "Cash",
                PaidAmount: 200000m,
                Discount: 0m,
                Surcharges: new(),
                Items: new List<OrderItemRequest> { new(product.Id, null, 1m, 10000m) }
            );

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.CreateOrderAsync(userIdB, req, null)
            );
            Assert.Contains("Chưa mở ca làm việc", ex.Message);

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task IdempotentReplay_AfterShiftClosed_ReturnsOriginal()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var (userId, product) = await SeedUserAndProduct(context, 100m);
            var shift = await ShiftTestHelper.EnsureOpenShiftAsync(context, userId);
            var service = new OrderService(context, new Mock<ILogger<OrderService>>().Object);

            var req = new CreateOrderRequest(
                CustomerId: null,
                PaymentMethod: "Cash",
                PaidAmount: 200000m,
                Discount: 0m,
                Surcharges: new(),
                Items: new List<OrderItemRequest> { new(product.Id, null, 1m, 10000m) }
            );

            var idempotencyKey = "IDEMP-REPLAY-" + Guid.NewGuid();

            // First call with open shift
            var res1 = await service.CreateOrderAsync(userId, req, idempotencyKey);
            Assert.NotNull(res1);

            // Now close the shift
            shift.Status = ShiftStatus.Closed;
            shift.EndedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();

            // Second call with same idempotency key after shift closed -> should return original without shift rule block
            var res2 = await service.CreateOrderAsync(userId, req, idempotencyKey);
            Assert.NotNull(res2);
            Assert.Equal(res1.Id, res2.Id);
            Assert.Equal(res1.Code, res2.Code);

            Assert.Equal(1, await context.Orders.CountAsync(o => o.CashierId == userId));

            await tx.RollbackAsync();
        }
    }
}
