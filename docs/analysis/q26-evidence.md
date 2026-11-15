# Bằng chứng khắc phục Q26 & Q28 (Evidence Q26 - Round 2)

## HEAD Commit & Git Status
- `git rev-parse --short HEAD`: `2d3eec4`
- `git status --short`: (Sạch sẽ)

---

## 1. Tái hiện Q28 trên mã trước khi sửa (Commit 5aba6a4)
- **Thông báo lỗi nguyên văn khi chạy test Q28 trước khi sửa:**
  ```
  PosApi.Services.BusinessRuleException : Sản phẩm 'Test Product d38c' đang lệch bất biến tồn kho (CurrentStock: 7 != Tổng kho: 10.000). Vui lòng chạy công cụ cân bằng tồn kho (Q14) trước khi chốt phiếu nhập.
  ```

---

## 2. Giải pháp sửa trong `OrderService` (Tuân thủ tuyệt đối quy định mới)
- **Tạo đơn (`CreateOrderAsync` - `OrderService.cs:385`):**
  - Không tự tạo kho `WH-DEFAULT` nếu không tìm thấy kho hoạt động; ném `BusinessRuleException` ("Không tìm thấy kho hoạt động nào để xuất bán hàng.").
  - Khóa `WarehouseStocks` bằng `SELECT ... FROM "WarehouseStocks" WHERE "WarehouseId" = @whId AND "ProductId" = ANY(@ids) ORDER BY "Id" FOR UPDATE` (`OrderService.cs:411`).
  - Không tự tạo dòng `WarehouseStock` với `Stock = CurrentStock` khi thiếu. Thiếu dòng hoặc không đủ số lượng tồn tại kho ném `StockShortageException` (`OrderService.cs:430`).
- **Hủy đơn (`CancelOrderAsync` - `OrderService.cs:935`):**
  - Khóa `WarehouseStocks` liên quan bằng `FOR UPDATE` theo `Id` tăng dần (`OrderService.cs:925`).
  - Chỉ hoàn kho theo `WarehouseId` có sẵn trong ledger (`StockLedgerEntry`); nếu đơn cũ không có `WarehouseId` trong ledger thì chỉ hoàn `CurrentStock` và ghi cảnh báo (`_log.LogWarning`), tuyệt đối không tự tạo dòng kho mới.

---

## 3. Thí nghiệm đột biến trên clone tạm
- Xóa đoạn trừ `whStock.Stock` trong luồng bán (`OrderService.cs`), chạy test hồi quy 5 lần liên tiếp:
  - **Kết quả (5/5 lần FAIL):**
    ```
    PosApi.Services.BusinessRuleException : Sản phẩm '...' đang lệch bất biến tồn kho (CurrentStock: 7 != Tổng kho: 10.000). Vui lòng chạy công cụ cân bằng tồn kho (Q14) trước khi chốt phiếu nhập.
    ```

---

## 4. Cập nhật tài liệu `group8-open-questions.md`
- **Q26:** Đã sửa. Dẫn chứng: `OrderService.cs:385` (lấy kho mặc định/active), `OrderService.cs:411` (khóa `FOR UPDATE` trên `WarehouseStocks`), `OrderService.cs:430` (kiểm tra tồn kho và ném `StockShortageException` nếu thiếu dòng/không đủ).
- **Q28:** Đã sửa. Dẫn chứng: `OrderServiceTests.cs` (`Q28_SellThenStockIn_Success_NoInvariantException`).
- **Q29:** Đã thêm ý kiến ghi nhận xử lý dữ liệu lịch sử.

---

## 5. Đầu ra thô thực tế (`dotnet test` & DriftCheck)
- **Chạy toàn bộ bộ test 3 lần liên tiếp (196/196 test PASS mỗi lần):**
  - Run 1: `Passed! - Failed: 0, Passed: 196, Total: 196`
  - Run 2: `Passed! - Failed: 0, Passed: 196, Total: 196`
  - Run 3: `Passed! - Failed: 0, Passed: 196, Total: 196`
- **DriftCheck trong clone tạm:** `Up` và `Down` hoàn toàn rỗng (`{ }`).

---

## 6. Git Log & Remote Verification
- `git log --oneline -3`:
  ```
  2d3eec4 fix(pos): enforce WarehouseStock checks and locking in OrderService without auto-creating WH-DEFAULT or missing rows
  e3b45bc docs(pos): add q26-evidence.md for Q26/Q28 evidence submission
  c0b13bc fix(pos): resolve Q26 & Q28 by deducting WarehouseStock in OrderService.CreateOrderAsync and refunding in CancelOrderAsync
  ```
- `git ls-remote origin fix/q26-sale-warehousestock`:
  ```
  b8f8524120e71bdb54d6b7cffb4f0bd68a6d5dd3	refs/heads/fix/q26-sale-warehousestock
  ```
