# Bằng chứng khắc phục Q26 & Q28 (Evidence Q26 - Final)

## HEAD Commit & Git Status
- `git rev-parse --short HEAD`: `ebad942`
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

## 3. DriftCheck & Git Log / Remote Verification
- **DriftCheck Up/Down trong clone tạm (No-op):**
  ```csharp
  protected override void Up(MigrationBuilder migrationBuilder)
  {
  }

  protected override void Down(MigrationBuilder migrationBuilder)
  {
  }
  ```
- **`git log --oneline -4`:**
  ```
  ebad942 fix(pos): add Order table FOR UPDATE lock and duplicate cancellation check in CancelOrderAsync, plus new unit tests and Q29 update
  d7d3e25 refactor(pos): restore SupplierDebtFeatureTests.cs to 5aba6a4
  6d96181 fix(pos): wrap CancelOrderAsync in execution strategy transaction and lock WarehouseStocks FOR UPDATE
  1ac1711 docs(pos): update q26-evidence.md with final refined test results and verification
  ```
- **`git ls-remote origin fix/q26-sale-warehousestock`:**
  ```
  ebad94299951c2b98e8bd3ee2f9e3bc196350405	refs/heads/fix/q26-sale-warehousestock
  ```
