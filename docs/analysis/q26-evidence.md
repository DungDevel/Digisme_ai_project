# Bằng chứng khắc phục Q26 & Q28 (Evidence Q26 - Final)

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
1. `Q28_SellThenStockIn_Success_NoInvariantException` (`pos-api/tests/PosApi.Tests/OrderServiceTests.cs:270`)
2. `CancelOrderAsync_RefundsWarehouseStockAndCurrentStock` (`pos-api/tests/PosApi.Tests/OrderServiceTests.cs:335`)
3. `CancelOrderAsync_Twice_ThrowsException` (`pos-api/tests/PosApi.Tests/OrderServiceTests.cs:843`)
4. `CancelOrderAsync_BOMOrder_RefundsFinishedGoodAndIngredients` (`pos-api/tests/PosApi.Tests/OrderServiceTests.cs:865`)
5. `CreateOrderAsync_NoActiveWarehouse_ThrowsBusinessRuleException` (`pos-api/tests/PosApi.Tests/OrderServiceTests.cs:769`)
6. `CreateOrderAsync_MissingWarehouseStock_ThrowsStockShortageException` (`pos-api/tests/PosApi.Tests/OrderServiceTests.cs:791`)
7. `CancelOrderAsync_OldOrderWithoutWarehouseId_RefundsCurrentStockOnlyWithWarning` (`pos-api/tests/PosApi.Tests/OrderServiceTests.cs:816`)

---

## 3. DriftCheck & Đầu ra thô 3 lần `dotnet test`
- **DriftCheck Up/Down trong DB:** `{ }` (Không có lệch cấu trúc migration / model).
- **Đầu ra thô 3 lần chạy `dotnet test pos-api/tests/PosApi.Tests/PosApi.Tests.csproj`:**
  - Run 1:
    ```
    Passed!  - Failed:     0, Passed:   198, Skipped:     0, Total:   198, Duration: 12 s - PosApi.Tests.dll (net8.0)
    ```
  - Run 2:
    ```
    Passed!  - Failed:     0, Passed:   198, Skipped:     0, Total:   198, Duration: 11 s - PosApi.Tests.dll (net8.0)
    ```
  - Run 3:
    ```
    Passed!  - Failed:     0, Passed:   198, Skipped:     0, Total:   198, Duration: 12 s - PosApi.Tests.dll (net8.0)
    ```
