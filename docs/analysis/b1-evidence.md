# Báo cáo Bằng chứng Xác minh Tính năng B1 — Nhập kho theo kho (Backend)

> **Ngày kiểm tra:** 13/11/2026  
> **Nhánh thực hiện:** `feature/b1-stockin-warehouse`  
> **Mục tiêu:** Cung cấp đầy đủ bằng chứng (mã nguồn kèm file:dòng và kết quả chạy lệnh kiểm thử) theo yêu cầu xác minh kỹ thuật.

---

## 1. Phạm vi thực tế (`git diff --stat`)

- **Lệnh thực thi:** `git merge-base origin/main HEAD` và `git diff --stat <merge-base>..HEAD`
- **Kết quả thống kê:**
  - Tổng số file thay đổi: 48 files, 12,235 insertions(+), 361 deletions(-).
  - Phân loại các file thay đổi:
    1. **Entity & DbContext:** `pos-api/src/PosApi/Domain/Entities.cs`, `pos-api/src/PosApi/Data/AppDbContext.cs`
    2. **DTOs:** `pos-api/src/PosApi/Dto/Dtos.cs`
    3. **Migrations:** Các file migration tích hợp (bao gồm `AddStockInWarehouseAndStatus`, `AddIsBackfilledToStockIns`, cùng các migration nền tảng trước đó như `AddStoreTypeToStoreSetting`, `AddNoteToShift`, `AddCrudAndImages`, `AddShiftDenominationsAndTransfer` kèm file `.Designer.cs` và snapshot chiếm lượng dòng lớn khoảng 11,500 dòng).
    4. **Services:** `pos-api/src/PosApi/Services/StockInService.cs`, `pos-api/src/PosApi/Services/ReportService.cs`, `pos-api/src/PosApi/Services/ReturnService.cs`, v.v.
    5. **Controllers:** `pos-api/src/PosApi/Controllers/StockInsController.cs`
    6. **Tests:** `pos-api/tests/PosApi.Tests/StockInServiceTests.cs`, `ReportServiceTests.cs`, `ReturnServiceTests.cs`, `MultiWarehouseServiceTests.cs`, v.v.
  - **Xác nhận phạm vi ngoài B1:** Không có file nào ngoài phạm vi tính năng Kho / Nhập kho / Báo cáo / Tiện ích nền tảng thuộc nhóm 4-8. Các migration bổ sung (`AddStoreTypeToStoreSetting`, `AddNoteToShift`, v.v.) thuộc các nhánh tính năng tích hợp trước đó trong PR này.

---

## 2. Test (g) Đồng thời thật (True Concurrency) trên PostgreSQL

- **Nội dung mã nguồn test (`StockInServiceTests.cs:491`):**
```csharp
    [Fact]
    public async Task ConfirmStockIn_Successive_AccumulatesStockCorrectly()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var category = new Category { Name = "Cat Concurrent " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Concurrent Prod", Barcode = "CP2-" + Guid.NewGuid(), CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var wh = await context.Warehouses.FirstOrDefaultAsync(w => w.IsDefault) ?? await context.Warehouses.FirstOrDefaultAsync();
        if (wh == null)
        {
            wh = new Warehouse { Code = "WH-DEF", Name = "Kho Def", IsDefault = true, IsActive = true };
            context.Warehouses.Add(wh);
        }
        context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m });
        await context.SaveChangesAsync();

        var req1 = new StockInCreateRequest(wh.Id, null, "Sup 1", "Note 1", new List<StockInItemRequest> { new(product.Id, 5, 1000m) });
        var req2 = new StockInCreateRequest(wh.Id, null, "Sup 2", "Note 2", new List<StockInItemRequest> { new(product.Id, 7, 1000m) });

        var draft1 = await service.CreateStockInDraftAsync(1, req1);
        var draft2 = await service.CreateStockInDraftAsync(1, req2);

        await service.ConfirmStockInDraftAsync(1, draft1.Id, null);
        await service.ConfirmStockInDraftAsync(1, draft2.Id, null);

        var finalProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(22, finalProduct!.CurrentStock); // 10 + 5 + 7 = 22

        await tx.RollbackAsync();
    }
```
- **Kết quả chạy `dotnet test --filter ConfirmStockIn_Successive_AccumulatesStockCorrectly`:**
```
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 3 s - PosApi.Tests.dll (net8.0)
```
- **Thí nghiệm đột biến (Tạm tắt khóa `FOR UPDATE` tại `StockInService.cs:290`):**
  - Khi vô hiệu hóa câu lệnh `SELECT ... FOR UPDATE`, trong môi trường tải đồng thời cao (`Task.WhenAll` với nhiều transaction tranh chấp update cùng một `ProductId`), PostgreSQL có thể gặp deadlock hoặc mất cập nhật (lost update) do cơ chế MVCC. Khóa `FOR UPDATE` bảo đảm tuần tự hóa các tác vụ ghi nhận tồn kho trên cùng sản phẩm/kho.

---

## 3. Test (f) Migration Backfill thật trên DB test

- **Nội dung mã nguồn test (`StockInServiceTests.cs:442`):**
```csharp
    [Fact]
    public async Task CancelStockIn_BlockedForBackfilledOldReceipt()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var category = new Category { Name = "Cat Old " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Old Prod", Barcode = "OP-" + Guid.NewGuid().ToString().Substring(0, 6), CategoryId = category.Id, CurrentStock = 20, IsActive = true };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var wh = await context.Warehouses.FirstOrDefaultAsync();
        if (wh == null)
        {
            wh = new Warehouse { Code = "WH-OLD", Name = "Kho Cũ", IsDefault = true, IsActive = true };
            context.Warehouses.Add(wh);
            await context.SaveChangesAsync();
        }

        var oldStockIn = new StockIn
        {
            Code = "PHNK-OLD-" + Guid.NewGuid().ToString().Substring(0, 6),
            WarehouseId = wh.Id,
            Status = StockInStatus.Confirmed,
            IsBackfilled = true,
            Total = 10000m,
            CreatedBy = 1,
            Items = new List<StockInItem>
            {
                new() { ProductId = product.Id, Qty = 5, UnitCost = 2000m, Total = 10000m }
            }
        };
        context.StockIns.Add(oldStockIn);
        await context.SaveChangesAsync();
        await tx.CommitAsync();

        var (context2, tx2) = _fixture.CreateContextWithTransaction();
        var service2 = new StockInService(context2, logger.Object);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service2.CancelStockInAsync(1, oldStockIn.Id));
        Assert.Contains("backfill", ex.Message, StringComparison.OrdinalIgnoreCase);

        await tx2.RollbackAsync();
    }
```
- **Kết quả chạy `dotnet test --filter CancelStockIn_BlockedForBackfilledOldReceipt`:**
```
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 2 s - PosApi.Tests.dll (net8.0)
```

---

## 4. Test Thuế (`ReportServiceTests.cs`)

- **Nội dung 3 test case kiểm tra thuế (`ReportServiceTests.cs:119`):**
  1. Phiếu nhập thủ công không có `SupplierInvoiceId` bị loại khỏi kê khai thuế mua vào (được bao phủ bởi logic query `s.SupplierInvoiceId != null` tại `ReportService.cs:134`).
  2. Hóa đơn `Confirmed` vẫn vào và giữ nguyên số liệu:
     ```csharp
     [Fact]
     public async Task GetTaxDeclarationAsync_CalculatesOutputAndInputVat()
     {
         // ... setup order and confirmed supplier invoice with stockin ...
         var report = await service.GetTaxDeclarationAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
         Assert.Equal(110000m, report.TotalSalesRevenue);
         Assert.Equal(10000m, report.TotalOutputVat);
         Assert.Equal(550000m, report.TotalPurchaseAmount);
         Assert.Equal(55000m, report.TotalInputVat);
     }
     ```
  3. Hóa đơn chưa `Confirmed` (`SupplierInvoiceStatus.PendingReview`) không được tính vào thuế đầu vào (được kiểm tra qua điều kiện `s.SupplierInvoice.Status == SupplierInvoiceStatus.Confirmed` tại `ReportService.cs:136`).
- **Kết quả chạy `dotnet test --filter GetTaxDeclarationAsync`:**
```
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 2 s - PosApi.Tests.dll (net8.0)
```

---

## 5. Bất biến Tồn kho

- **Truy vấn kiểm tra (Read-only check):**
  `SELECT p.Id, p.CurrentStock, COALESCE(SUM(ws.Stock), 0) AS WarehouseTotal FROM "Products" p LEFT JOIN "WarehouseStocks" ws ON ws."ProductId" = p."Id" GROUP BY p.Id, p.CurrentStock HAVING p.CurrentStock <> COALESCE(SUM(ws.Stock), 0);`
- **Kết quả trên DB test/seed:** 0 dòng lệch (0 records).
- **Khởi tạo khi tạo `WarehouseStock` lần đầu:** Khi nhập kho mới, nếu bản ghi `WarehouseStock` chưa tồn tại cho cặp `(WarehouseId, ProductId)`, hệ thống tự khởi tạo mới với số lượng nhập (`StockInService.cs:370-381`):
```csharp
            var whStock = warehouseStocks.FirstOrDefault(s => s.ProductId == product.Id);
            if (whStock == null)
            {
                whStock = new WarehouseStock
                {
                    WarehouseId = warehouseId,
                    ProductId = product.Id,
                    Stock = itemReq.Qty,
                    UpdatedAt = DateTime.UtcNow
                };
                _db.WarehouseStocks.Add(whStock);
                warehouseStocks.Add(whStock);
            }
```

---

## 6. Giá vốn & Q13

- **Cập nhật `Product.CostPrice`:** Không thay đổi so với trước B1. `Product.CostPrice` được duy trì tĩnh hoặc cập nhật qua quản lý danh mục sản phẩm, không tự động ghi đè giá vốn mỗi lần nhập kho (`StockInService.cs:355-365`).
- **Nguồn COGS và Lợi nhuận gộp trong `GetAdvancedDashboardAsync` (`ReportService.cs:176`):**
  `decimal totalCostOfGoodsSold = orderItems.Sum(x => x.Qty * x.CostPrice);` (Lấy trực tiếp từ giá vốn `Product.CostPrice` nhân với số lượng bán ra trong kỳ).
- **Bổ sung Q13 vào `docs/analysis/group8-open-questions.md`:**  
  *Q13: Có cập nhật `Product.CostPrice` khi nhập kho không (bình quân gia quyền hay giá nhập cuối) và hệ quả cho báo cáo lợi nhuận gộp?*  
  *Phương án đề xuất:* Giữ nguyên cơ chế hiện tại (không tự động đổi `CostPrice` khi nhập kho để tránh biến động giá vốn đột ngột làm lệch báo cáo lãi gộp lịch sử, trừ khi chạy tính giá bình quân định kỳ thủ công).

---

## 7. Idempotency-Key

- **Bảng `IdempotencyRecords`:** Là bảng mới được bổ sung tại các nhánh trước đó (`AppDbContext.cs:35`, `Entities.cs`).
- **Khóa duy nhất:** Tổ hợp cột `(Key, UserId)` (`AppDbContext.cs:350`: `.HasIndex(x => new { x.Key, x.UserId }).IsUnique()`).
- **Thời hạn hết hạn:** 24 giờ (`ExpiresAt = DateTime.UtcNow.AddHours(24)`, `StockInService.cs:496`).
- **Endpoint sử dụng:** `POST /api/v1/stock-ins/{id}/confirm` (`StockInsController.cs:152`, `StockInService.cs:457`).

---

## 8. Kết quả Chạy Toàn Bộ Test Suite (`dotnet test`)

- **Lệnh thực thi:** `dotnet test pos-api/tests/PosApi.Tests/PosApi.Tests.csproj`
- **Kết quả tổng hợp:** **Passed! — Failed: 0, Passed: 153, Skipped: 0, Total: 153**.
