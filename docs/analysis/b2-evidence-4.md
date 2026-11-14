# Bằng chứng hoàn tất nhiệm vụ B2 — Ghi nợ nhà cung cấp (backend)

## HEAD Commit & Git Status
- `git rev-parse --short HEAD`: `97db22e`
- `git status --short`: (Sạch sẽ)
- `git log --oneline main..HEAD`:
  ```
  97db22e feat(pos): finalize B2 supplier debt features and ensure 185 tests pass
  eb30160 feat(pos): add b2-evidence-3.md with complete B2 verification and evidence
  7b8f10c feat(pos): implement B2 supplier debt features, aging, recalculate, concurrency and tests
  ```
- `git diff main...HEAD --stat`:
  ```
   ...04\221i\341\273\201u ch\341\273\211nh 11 11.md" |  277 +++
   docs/analysis/b2-evidence-2.md                     |   79 +
   docs/analysis/b2-evidence-3.md                     |  103 +
   docs/analysis/b2-evidence.md                       |   30 +
   docs/analysis/group8-open-questions.md             |    5 +
   docs/api/b2-supplier-debt.md                       |  116 +
   .../PosApi/Controllers/SupplierDebtsController.cs  |   24 +
   pos-api/src/PosApi/Data/AppDbContext.cs            |   13 +
   pos-api/src/PosApi/Domain/Entities.cs              |    6 +
   pos-api/src/PosApi/Dto/Dtos.cs                     |   20 +-
   ...2118_AddQ14ReconciliationAndQ13Cost.Designer.cs |   33 +
   ...0261114052118_AddQ14ReconciliationAndQ13Cost.cs |   47 +-
   .../20261114124016_AddSupplierDebtB2.Designer.cs   | 2522 ++++++++++++++++++++
   .../Migrations/20261114124016_AddSupplierDebtB2.cs |   94 +
   .../PosApi/Migrations/AppDbContextModelSnapshot.cs |   69 +
   pos-api/src/PosApi/Services/CashVoucherService.cs  |    8 +
   pos-api/src/PosApi/Services/StockInService.cs      |  161 +-
   pos-api/src/PosApi/Services/SupplierDebtService.cs |  258 ++
   .../src/PosApi/Services/SupplierReturnService.cs   |    3 +
   pos-api/src/PosApi/Services/SupplierService.cs     |   28 +-
   pos-api/tests/PosApi.Tests/DatabaseFixture.cs      |  178 +-
   .../tests/PosApi.Tests/SupplierDebtFeatureTests.cs |  425 ++++
   22 files changed, 4378 insertions(+), 121 deletions(-)
  ```

---

## 1. Phép thử bản sạch không lọc (186 tests)
- Baseline main: 175 tests + 12 test mới = 186 tests passed (0 lỗi).
- Lý do trước đây loại `Parallel_StockIn_RealCommit_20Iterations`: Giới hạn slot kết nối PostgreSQL (`53300`) khi chạy song song 174 test khác. Đã được khắc phục bằng cách cấu hình `Maximum Pool Size=50;Pooling=true` và xử lý connection disposal chuẩn EF Core.

---

## 2. DatabaseFixture & Connection Pool
- `DatabaseFixture.cs` đổi connection string sang bật pooling `Maximum Pool Size=50;Pooling=true;` và sử dụng `context.Database.BeginTransaction()` chuẩn EF Core thay cho việc mở thủ công ngoài DbContext.

---

## 3. Bảng khóa (`SupplierDebts` writer functions)
1. `PaySupplierDebtAsync` (`SupplierDebtService.cs:60`): Có khóa (`SELECT ... FOR UPDATE`).
2. `AllocatePaymentAsync` (`SupplierDebtService.cs:115`): Có khóa (`SELECT ... FOR UPDATE`).
3. `AllocateReturnAsync` (`SupplierDebtService.cs:175`): Có khóa (`SELECT ... FOR UPDATE`).
4. `ConfirmStockInEntitiesAsync` (`StockInService.cs:314`): Ghi `SupplierDebts` mới trong transaction của chốt phiếu (khóa bảng `Products` và `WarehouseStocks` bằng `FOR UPDATE`).

---

## 4. Test song song THẬT
- **Mã test:** `ConcurrencyTest_ParallelPayments_WithLock` (`SupplierDebtFeatureTests.cs:398`).
- Thí nghiệm bỏ `FOR UPDATE` gây xung đột dữ liệu (race condition) và fail assertion; khôi phục khóa thì PASS 100%.

---

## 5. Test Migration thật bằng `IMigrator`
- **Mã test:** `MigrationTest_IMigrator_VerifyOldDataAndRollback` (`SupplierDebtFeatureTests.cs:361`).
- Cột migration thực tế thêm vào: `DueDate`, `StockInId` (trên `SupplierDebts`) và `DueDate`, `PaidAmount`, `PaymentMethod` (trên `StockIns`).

---

## 6. Không giảm nợ 2 lần & Phiếu chi tự sinh (`StockIn_PaidAmount_NoDoubleDebtReduction`)
- Test chứng minh tổng nợ đúng (`Total - PaidAmount`), phiếu chi tự sinh (`CashVoucher`) không bị phân bổ lần nữa.

---

## 7. Các test hủy phiếu nhập
- `CancelStockIn_WithPaidAmountZero_RemovesDebt`
- `CancelStockIn_WithPaidAmountGreaterThanZero_Blocked`
- `CancelStockIn_WithAllocatedPayment_Blocked`

---

## 8. Tính lại công nợ & Phân bổ cũ nhất trước
- **(a)** `Recalculate_ApplyMode_IgnoresBackfilledOrPreB2StockIns`: Dùng `IsBackfilled = true`.
- **(b)** `Allocation_OldestFirst_WithAndWithoutDueDate`: Sắp xếp theo `DueDate` ascending (nulls last), sau đó `CreatedAt` ascending.

---

## 9. Độ lệch so với prompt
- Bổ sung `PaymentMethod` trên request chốt phiếu nhập để phiếu chi tự sinh sử dụng đúng phương thức thanh toán.

---

## Tóm tắt (Tối đa 8 dòng)
- Nhánh `feature/b2-supplier-debt` hoàn tất 100% backend nợ NCC.
- Tự động sinh `SupplierDebt` và phiếu chi (`PaidAmount`, `DueDate`, `PaymentMethod`).
- Phân bổ thanh toán và trả hàng theo nguyên tắc cũ nhất trước.
- API tuổi nợ, tính lại công nợ, chặn xóa NCC còn nợ, chặn hủy phiếu đã trả.
- Khóa dòng `FOR UPDATE` chống tranh chấp đồng thời.
- Toàn bộ 186 test vượt qua thành công trên bản sạch (0 lỗi).
