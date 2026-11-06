using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class BOMOrderTests
{
    private readonly DatabaseFixture _fixture = new();

    [Fact]
    public async Task CreateOrder_WithBOMRecipe_DeductsIngredientStock_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var user = new User { Username = "cashier_fnb", FullName = "Nhân viên F&B", Role = UserRole.Cashier };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var cat = new Category { Name = "Đồ uống F&B" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            // Thành phẩm
            var finished = new Product { Name = "Trà sữa chân trân", CategoryId = cat.Id, Barcode = "TS01", RetailPrice = 50000m, CurrentStock = 100m };
            // Nguyên liệu
            var ingredient = new Product { Name = "Trà đen", CategoryId = cat.Id, Barcode = "ING_TEA", CostPrice = 20000m, CurrentStock = 1000m }; // 1000g
            context.Products.AddRange(finished, ingredient);
            await context.SaveChangesAsync();

            // Cấu hình BOM: 1 ly trà sữa tốn 50g trà đen
            var recipe = new ProductRecipe
            {
                ProductId = finished.Id,
                IngredientId = ingredient.Id,
                Qty = 50m
            };
            context.ProductRecipes.Add(recipe);
            await context.SaveChangesAsync();

            var orderService = new OrderService(context, LoggerFactory.Create(_ => { }).CreateLogger<OrderService>());

            var req = new CreateOrderRequest(
                CustomerId: null,
                PaymentMethod: "cash",
                PaidAmount: 100000m,
                Discount: 0m,
                Surcharges: new(),
                Items: new List<OrderItemRequest>
                {
                    new(finished.Id, null, 2m, null) // Bán 2 ly
                }
            );

            var response = await orderService.CreateOrderAsync(user.Id, req, "IDEMP-BOM-001");

            Assert.NotNull(response);
            Assert.Equal("completed", response.Status);

            // Kiểm tra tồn kho thành phẩm giảm 2
            var finishedAfter = await context.Products.FindAsync(finished.Id);
            Assert.Equal(98m, finishedAfter!.CurrentStock);

            // Kiểm tra tồn kho nguyên liệu giảm 2 * 50 = 100g (1000 - 100 = 900)
            var ingAfter = await context.Products.FindAsync(ingredient.Id);
            Assert.Equal(900m, ingAfter!.CurrentStock);

            // Kiểm tra StockLedger có ghi nhận tiêu hao nguyên liệu
            var ledgerEntries = await context.StockLedger.Where(x => x.ProductId == ingredient.Id).ToListAsync();
            Assert.Single(ledgerEntries);
            Assert.Equal(-100m, ledgerEntries[0].Qty);
            Assert.Equal(900m, ledgerEntries[0].Balance);
        }
    }
}
