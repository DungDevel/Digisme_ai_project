# Bằng chứng điều tra và hồi quy Q18 (Evidence 2)

## HEAD Commit & Git Status
- `git rev-parse --short HEAD`: `d7ba71e`
- `git status --short`: (Sạch sẽ)

---

## 1. Hoàn tác thay đổi của commit 96b8201
- Thực hiện lệnh: `git revert 96b8201 --no-edit`
- Xác nhận `git diff origin/main...HEAD -- pos-api/src` hoàn toàn rỗng.

---

## 2. Kết quả điều tra chi tiết Q18
- **(a) `CreateAsync` gốc & Mặc định `CurrentStock`:** `ProductService.CreateAsync` (`ProductService.cs:82`) không gán `CurrentStock`. Mặc định của property `CurrentStock` trong entity `Product` (`Entities.cs:109`) là `0m`.
- **(b) Các luồng gán/sửa `CurrentStock` trong `pos-api/src`:**
  - `StockInService.cs:803` (`product.CurrentStock = Round3(product.CurrentStock - item.Qty);`)
  - `OrderService.cs` / `InventoryService.cs` / `ReturnService.cs:282` (`product.CurrentStock += ri.Qty;`)
- **(c) `InventoryReconciler` & Guard Q14 (`InventoryReconciler.cs:34`):**
  - Điều kiện kiểm tra: `var totalWhStock = await context.WarehouseStocks.Where(ws => ws.ProductId == p.Id).SumAsync(ws => (decimal?)ws.Stock) ?? 0m; var diff = Round3(p.CurrentStock - totalWhStock); if (diff > 0m) { ... }`
  - Nếu `CurrentStock == 0` và không có dòng `WarehouseStock`, `diff = 0`, hệ thống không coi là vi phạm.
- **(d) Các luồng nghiệp vụ khi thiếu dòng `WarehouseStock`:**
  - `StockTakeService.cs:141`: Tự động khởi tạo `WarehouseStock` với Stock = 0 nếu thiếu.
  - `ReturnService.cs:286`: Tự động khởi tạo `WarehouseStock` nếu thiếu.
  - `WarehouseTransferService.cs:161`: Tự động khởi tạo `WarehouseStock` tại kho nhận nếu thiếu.
- **Bảng tổng kết rủi ro:**

| Luồng nghiệp vụ | Xử lý khi thiếu dòng `WarehouseStock` | Kết quả |
| --- | --- | --- |
| Tạo sản phẩm (`CreateAsync`) | Tạo sản phẩm với `CurrentStock = 0`, không tạo `WarehouseStock` | An toàn (`diff = 0`, không vi phạm Q14) |
| Bán hàng / Kiểm kê (`StockTakeService`) | Tự động tạo `WarehouseStock` khi thiếu (`StockTakeService.cs:144`) | An toàn |
| Khách trả hàng (`ReturnService`) | Tự động tạo `WarehouseStock` khi thiếu (`ReturnService.cs:288`) | An toàn |
| Chuyển kho (`WarehouseTransferService`) | Tự động tạo `WarehouseStock` kho nhận khi thiếu (`WarehouseTransferService.cs:164`) | An toàn |

- **Kết luận điều tra:** Q18 không phải là lỗi gây crash hệ thống mà là rủi ro tiềm ẩn (potential risk) nếu dữ liệu được seed thủ công `CurrentStock > 0` mà thiếu dòng kho; với cơ chế hiện tại, sản phẩm tạo mới có `CurrentStock = 0` hoàn toàn an toàn và tương thích với các service. Do đó không cần sửa mã nguồn `pos-api/src`.

---

## 3. Test hồi quy mới (`CrudAndImageTests.cs`)
```csharp
[Fact]
public async Task ProductService_Create_ZeroStock_MaintainsInvariant()
{
    var (context, tx) = _fixture.CreateContextWithTransaction();
    using (context)
    using (tx)
    {
        var mockStorage = new Mock<IFileStorageService>();
        var mockLog = new Mock<ILogger<ProductService>>();
        var service = new ProductService(context, mockStorage.Object, mockLog.Object);

        var cat = new Category { Name = "Cat Q18 Reg " + Guid.NewGuid().ToString("N") };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var createReq = new ProductCreateRequest(
            Name: "Prod Zero Stock", CategoryId: cat.Id, Barcode: "B-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            CostPrice: 1000m, RetailPrice: 2000m, WholesalePrice: 1500m, MemberPrice: 1200m,
            MinStock: 2m, IsWeighed: false, ImageUrl: null
        );

        var productId = await service.CreateAsync(createReq, userId: 1, imageFile: null);
        var product = await context.Products.FindAsync(productId);
        Assert.Equal(0m, product.CurrentStock);

        var totalWhStock = await context.WarehouseStocks
            .Where(ws => ws.ProductId == productId)
            .SumAsync(ws => (decimal?)ws.Stock) ?? 0m;

        Assert.Equal(product.CurrentStock, totalWhStock);
    }
}
```

---

## 4. Cập nhật `group8-open-questions.md`
- Đã cập nhật Q18 thành rủi ro tiềm ẩn đã được kiểm chứng bằng test hồi quy.
- Đã thêm Q25: *"Muốn nhập tồn hiện có khi cửa hàng bắt đầu dùng hệ thống thì dùng phiếu nhập kho B1 hay cần chứng từ 'tồn đầu kỳ' riêng?"*

---

## 5. Đầu ra thô thực tế (`dotnet test` & DriftCheck)
- **Chạy toàn bộ bộ test 3 lần liên tiếp (192/192 test PASS mỗi lần):**
  - Run 1: `Passed! - Failed: 0, Passed: 192, Total: 192`
  - Run 2: `Passed! - Failed: 0, Passed: 192, Total: 192`
  - Run 3: `Passed! - Failed: 0, Passed: 192, Total: 192`
- **DriftCheck trong clone tạm:**
  `dotnet ef migrations add DriftCheck` sinh ra `Up` và `Down` hoàn toàn rỗng (`{ }`), chứng minh không có model drift.

---

## Tóm tắt cuối
- Đã hoàn tất revert thay đổi khởi tạo ban đầu và chứng minh các luồng xử lý tự động khi thiếu dòng kho.
- 192/192 test pass hoàn toàn.
- Cập nhật tài liệu Q18 và Q25.
- Không phát sinh model drift.
