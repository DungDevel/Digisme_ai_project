# Bằng chứng kiểm tra và hoàn tất B2 (Evidence 10)

## HEAD Commit & Git Status
- `git rev-parse --short HEAD`: `1e08fc8`
- `git status --short`: (Sạch sẽ)

---

## 1. Khắc phục độ không ổn định (Test Flakiness)
- **Nguyên nhân cũ:** Test `Recalculate_ReportsDiscrepancyForOldStockInWithoutDebt` dùng `WarehouseId = 1` cứng và mã Code cố định, dẫn đến lỗi khóa ngoại `23503 FK_StockIns_Warehouses_WarehouseId` khi kho số 1 không tồn tại ở một số context test. Test `ConcurrencyTest_ParallelPayments_WithLock` dùng `Assert.True(sw.ElapsedMilliseconds > 0)` phụ thuộc thời gian hệ thống.
- **Cách sửa:** Lấy `WarehouseId` động thông qua `EnsureWarehouseAsync(context)` kết hợp `context.Warehouses.FirstAsync()`, sinh mã Code bằng `Guid.NewGuid()`, và xóa bỏ dòng assert thời gian `ElapsedMilliseconds > 0`.
- **Kết quả chạy toàn bộ test 5 lần liên tiếp (cả 5 lần đều PASS cùng số lượng 191/191 test):**
  - Run 1: `Passed! - Failed: 0, Passed: 191, Total: 191`
  - Run 2: `Passed! - Failed: 0, Passed: 191, Total: 191`
  - Run 3: `Passed! - Failed: 0, Passed: 191, Total: 191`
  - Run 4: `Passed! - Failed: 0, Passed: 191, Total: 191`
  - Run 5: `Passed! - Failed: 0, Passed: 191, Total: 191`

---

## 2. Test Hủy phiếu song song (`ConcurrencyTest_CancelStockIn_VsPayment_NoLostPayment`)
- **Kịch bản 2:** Sử dụng gọi thực tế `CancelStockInAsync` và `PaySupplierDebtAsync` đồng thời thông qua `TaskCompletionSource` (cổng đồng bộ).
- **Kết quả:**
  - Hủy thành công (`StockInStatus.Cancelled`), tồn kho được hoàn trả, dòng nợ bị xóa.
  - Tác vụ trả nợ đồng thời bị từ chối với ngoại lệ `KeyNotFoundException` do công nợ đã bị hủy/xóa.
- **Kết quả chạy riêng 3 lần liên tiếp:** Cả 3 lần đều PASS tuyệt đối.

---

## 3. Khảo sát 2 khối khóa `FOR UPDATE` trong `CancelStockInAsync` (`StockInService.cs`)
- **Khối 1 (dòng 753-759):** Khóa bảng `SupplierDebts` theo `StockInId` ngay trước khi khóa Products và WarehouseStocks, nhằm ngăn chặn tranh chấp với các giao dịch trả nợ/phân bổ công nợ đang chạy đồng thời.
- **Khối 2 (dòng 778-784):** Khóa lại bảng `SupplierDebts` sau khi đã khóa Products và WarehouseStocks (đây là khối lặp lại mang tính phòng thủ trùng lặp trong cùng transaction).
- Thực tế kiểm chứng: Việc duy trì khối khóa bảo vệ tranh chấp chính giúp test đồng thời hoạt động chính xác.

---

## 4. Kiểm chứng dữ liệu Migration (`MigrationTest_IMigrator_VerifyOldDataAndRollback`)
- Bổ sung assert giá trị thực tế của dữ liệu cũ sau khi Migrate Up, Down, và Up lại:
  - `StockIns` (`Code = 'PHNK-OLD'`): `Total = 1000m`.
  - `SupplierDebts` (`SupplierId = 1`): `Amount = 1000m`, `PaidAmount = 0m`.
- Xác thực toàn vẹn dữ liệu qua từng bước nâng/hạ migration thành công.

---

## 5. Kiểm tra DriftCheck
- Chạy `dotnet ef migrations add DriftCheck` trên bản clone tạm:
  ```csharp
  protected override void Up(MigrationBuilder migrationBuilder) { }
  protected override void Down(MigrationBuilder migrationBuilder) { }
  ```
- Phương thức `Up` và `Down` hoàn toàn rỗng, chứng minh không có model drift. Đã xóa clone tạm.

---

## Tóm tắt cuối
- Đã khắc phục triệt để flakiness bằng cách dùng ID kho động và sinh code qua Guid.
- Toàn bộ 191/191 test chạy pass 5 lần liên tiếp.
- Test hủy song song Kịch bản 2 đã gọi service thật qua cổng đồng bộ.
- Migration test đã assert giá trị dữ liệu cũ chi tiết.
- Không có model drift phát sinh.
