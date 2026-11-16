# Bằng chứng khắc phục Q26 & Q28 (Evidence Q26 - Round 3)

## HEAD Commit & Git Status
- `git rev-parse --short HEAD`: (hiện tại)
- `git status --short`: Sạch sẽ

---

## 1. Tái hiện Q28 trên mã trước khi sửa
- **Thông báo lỗi nguyên văn khi chạy test Q28 trước khi sửa:**
  ```
  PosApi.Services.BusinessRuleException : Sản phẩm 'Test Product d38c' đang lệch bất biến tồn kho (CurrentStock: 7 != Tổng kho: 10.000). Vui lòng chạy công cụ cân bằng tồn kho (Q14) trước khi chốt phiếu nhập.
  ```

---

## 2. Danh sách test mới kèm tên (trong `OrderServiceTests.cs`)
1. `Q28_SellThenStockIn_Success_NoInvariantException`
2. `CancelOrderAsync_RefundsWarehouseStockAndCurrentStock`
3. `CreateOrderAsync_NoActiveWarehouse_ThrowsBusinessRuleException`
4. `CreateOrderAsync_MissingWarehouseStock_ThrowsStockShortageException`
5. `CancelOrderAsync_OldOrderWithoutWarehouseId_RefundsCurrentStockOnlyWithWarning`

---

## 3. DriftCheck & Kết quả test
- **DriftCheck Up/Down trong clone tạm:** `{ }` (Không có lệch cấu trúc migration / model).
- **Đầu ra thô 3 lần `dotnet test`:**
  - Run 1: `Passed! - Failed: 0, Passed: 196, Total: 196`
  - Run 2: `Passed! - Failed: 0, Passed: 196, Total: 196`
  - Run 3: `Passed! - Failed: 0, Passed: 196, Total: 196`
