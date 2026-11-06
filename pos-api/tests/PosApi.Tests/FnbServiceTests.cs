using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class FnbServiceTests
{
    private readonly DatabaseFixture _fixture = new();

    [Fact]
    public async Task CreateArea_And_Table_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var areaService = new AreaService(context);
            var tableService = new TableService(context);

            var area = await areaService.CreateAreaAsync(1, new AreaCreateRequest("A1", "Tầng 1", "Khu vực sảnh chính", 1));
            Assert.NotNull(area);
            Assert.Equal("A1", area.Code);
            Assert.Equal("Tầng 1", area.Name);

            var table = await tableService.CreateTableAsync(1, new TableCreateRequest("T1", "Bàn 01", area.Id, 4));
            Assert.NotNull(table);
            Assert.Equal("T1", table.Code);
            Assert.Equal(area.Id, table.AreaId);
            Assert.Equal(TableStatus.Available, table.Status);
        }
    }

    [Fact]
    public async Task TableTransfer_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var areaService = new AreaService(context);
            var tableService = new TableService(context);

            var area = await areaService.CreateAreaAsync(1, new AreaCreateRequest("A1", "Tầng 1", null, 1));
            var t1 = await tableService.CreateTableAsync(1, new TableCreateRequest("T1", "Bàn 1", area.Id, 4));
            var t2 = await tableService.CreateTableAsync(1, new TableCreateRequest("T2", "Bàn 2", area.Id, 4));

            // Tạo user và order mẫu để thỏa mãn FK CurrentOrderId
            var user = new User { Username = "cashier1", FullName = "Thu Ngân", Role = UserRole.Cashier };
            context.Users.Add(user);
            await context.SaveChangesAsync();

            var order = new Order
            {
                Code = "INV-TEST-001",
                CashierId = user.Id,
                Status = OrderStatus.Pending,
                Subtotal = 10000m,
                Total = 10000m
            };
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            // Đổi t1 sang trạng thái có đơn hàng
            await tableService.UpdateTableStatusAsync(1, t1.Id, TableStatus.Occupied);
            var dbTable = await context.Tables.FirstAsync(x => x.Id == t1.Id);
            dbTable.CurrentOrderId = order.Id;
            await context.SaveChangesAsync();

            // Chuyển bàn từ t1 sang t2
            var success = await tableService.TransferTableAsync(1, t1.Id, t2.Id);
            Assert.True(success);

            var t1Updated = await tableService.GetTableByIdAsync(t1.Id);
            var t2Updated = await tableService.GetTableByIdAsync(t2.Id);

            Assert.Equal(TableStatus.Available, t1Updated!.Status);
            Assert.Null(t1Updated.CurrentOrderId);

            Assert.Equal(TableStatus.Occupied, t2Updated!.Status);
            Assert.Equal(order.Id, t2Updated.CurrentOrderId);
        }
    }

    [Fact]
    public async Task RoomCheckInAndCheckOut_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var roomService = new RoomService(context);

            var room = await roomService.CreateRoomAsync(1, new RoomCreateRequest("VIP1", "Phòng VIP 1", 120000m)); // 120k/h
            Assert.Equal(RoomStatus.Available, room.Status);

            var checkedIn = await roomService.CheckInRoomAsync(1, room.Id, new RoomCheckInRequest(null));
            Assert.Equal(RoomStatus.Occupied, checkedIn.Status);
            Assert.NotNull(checkedIn.StartTime);

            // Giả lập thời gian check-in cách đây 30 phút
            var dbRoom = await context.Rooms.FirstAsync(x => x.Id == room.Id);
            dbRoom.StartTime = DateTime.UtcNow.AddMinutes(-30);
            await context.SaveChangesAsync();

            var checkout = await roomService.CheckOutRoomAsync(1, room.Id);
            Assert.NotNull(checkout);
            Assert.Equal(room.Id, checkout.RoomId);
            // 30 phút với 120k/h = 60.000đ
            Assert.Equal(60000m, checkout.Amount);

            var roomAfter = await roomService.GetRoomByIdAsync(room.Id);
            Assert.Equal(RoomStatus.Available, roomAfter!.Status);
        }
    }

    [Fact]
    public async Task ProductRecipeUpsert_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var cat = new Category { Name = "Đồ uống" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            var finished = new Product { Name = "Cà phê sữa đá", CategoryId = cat.Id, Barcode = "CF01", RetailPrice = 30000m };
            var ing1 = new Product { Name = "Cà phê hạt", CategoryId = cat.Id, Barcode = "ING01", CostPrice = 100000m };
            var ing2 = new Product { Name = "Sữa đặc", CategoryId = cat.Id, Barcode = "ING02", CostPrice = 50000m };
            context.Products.AddRange(finished, ing1, ing2);
            await context.SaveChangesAsync();

            var recipeService = new ProductRecipeService(context);
            var req = new ProductRecipeUpsertRequest(new List<ProductRecipeItemRequest>
            {
                new(ing1.Id, 0.020m, null), // 20g cà phê
                new(ing2.Id, 0.030m, null)  // 30g sữa
            });

            var recipes = (await recipeService.UpsertRecipesAsync(1, finished.Id, req)).OrderBy(x => x.IngredientName).ToList();
            Assert.NotNull(recipes);
            Assert.Equal(2, recipes.Count);
            Assert.Equal("Cà phê hạt", recipes[0].IngredientName);
            Assert.Equal(0.020m, recipes[0].Qty);
            Assert.Equal("Sữa đặc", recipes[1].IngredientName);
            Assert.Equal(0.030m, recipes[1].Qty);
        }
    }
}
