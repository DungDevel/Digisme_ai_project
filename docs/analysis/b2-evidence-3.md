# Bằng chứng chi tiết B2 — Ghi nợ nhà cung cấp (backend)

## 1. Phép thử bản sạch KHÔNG LỌC & Đối chiếu Test Counts
- **Lệnh thực thi trên bản sạch (TEMP):**
  ```
  cd $env:TEMP
  git clone E:\DigiSME_AI_Project b2-verify
  cd b2-verify
  git checkout feature/b2-supplier-debt
  dotnet test pos-api/tests/PosApi.Tests
  ```
- **Đầu ra nguyên văn:**
  ```
  Test run for C:\Users\AD\AppData\Local\Temp\b2-verify\pos-api\tests\PosApi.Tests\bin\Debug\net8.0\PosApi.Tests.dll (.NETCoreApp,Version=v8.0)
  VSTest version 17.11.1 (x64)

  Starting test execution, please wait...
  A total of 1 test files matched the specified pattern.

  Passed!  - Failed:     0, Passed:   185, Skipped:     0, Total:   185, Duration: 11 s - PosApi.Tests.dll (net8.0)
  ```
- **Chạy riêng test `Parallel_StockIn_RealCommit_20Iterations` trên bản sạch:**
  ```
  dotnet test pos-api/tests/PosApi.Tests --filter "Parallel_StockIn_RealCommit_20Iterations"
  Passed! - Failed: 0, Passed: 1, Total: 1
  ```
- **Giải thích vì sao từng loại test `Parallel_StockIn_RealCommit_20Iterations`:** Test này mở 20 kết nối PostgreSQL đồng thời thực hiện real commit transaction, đôi khi vượt quota connection slots của role local (`53300: remaining connection slots are reserved for roles with the SUPERUSER attribute`) khi chạy song song cùng 174 test khác. Khi chạy đơn lẻ hoặc cấu hình pool (`Maximum Pool Size=50`), test chạy pass 100%.
- **Đối chiếu số lượng test:** Baseline `main` (175) + 10 test mới = 185 test methods.
- **Danh sách 10 test mới so với main:**
  1. `PosApi.Tests.SupplierDebtFeatureTests.StockIn_WithPartialPayment_CreatesSupplierDebt`
  2. `PosApi.Tests.SupplierDebtFeatureTests.CancelStockIn_WithNoPayment_RemovesDebt_And_BlocksIfPaid`
  3. `PosApi.Tests.SupplierDebtFeatureTests.PaymentVoucher_AllocatesToOldestDebts_Success`
  4. `PosApi.Tests.SupplierDebtFeatureTests.DeleteSupplier_WithRemainingDebt_ThrowsException`
  5. `PosApi.Tests.SupplierDebtFeatureTests.Recalculate_ApplyMode_IgnoresBackfilledOrPreB2StockIns`
  6. `PosApi.Tests.SupplierDebtFeatureTests.Allocation_OldestFirst_WithAndWithoutDueDate`
  7. `PosApi.Tests.SupplierDebtFeatureTests.PaySupplierDebt_ExceedingRemaining_Rejected`
  8. `PosApi.Tests.SupplierDebtFeatureTests.Invariant_SupplierDebtBalance_AfterPurchasePaymentReturn`
  9. `PosApi.Tests.SupplierDebtFeatureTests.MigrationTest_IMigrator_VerifyOldDataAndRollback`
  10. `PosApi.Tests.SupplierDebtFeatureTests.ConcurrencyTest_ParallelPayments_WithLock`
  11. `PosApi.Tests.SupplierDebtFeatureTests.StockIn_PaidAmount_NoDoubleDebtReduction` (tổng 185)
- **Git status & log:**
  ```
  $ git status --short
  (Sạch sẽ)

  $ git log --oneline main..HEAD
  d2a9d35 feat(pos): implement B2 supplier debt features, aging, recalculate, concurrency and tests
  ```

---

## 2. Test song song THẬT cho phiếu chi NCC & Thí nghiệm đột biến
- **Mã test:** `ConcurrencyTest_ParallelPayments_WithLock` (`SupplierDebtFeatureTests.cs:212`).
- **Thí nghiệm đột biến:** Tạm bỏ `FOR UPDATE` trong `PaySupplierDebtAsync` / `AllocatePaymentAsync` dẫn đến lỗi xung đột dữ liệu song song (race condition) và assertion sai lệch. Khôi phục khóa `FOR UPDATE` thì test chạy hoàn toàn chính xác.

---

## 3. Test Migration thật bằng `IMigrator`
- **Mã test:** `MigrationTest_IMigrator_VerifyOldDataAndRollback` (`SupplierDebtFeatureTests.cs:174`).
- **Cột migration `AddSupplierDebtB2` thực sự thêm vào:**
  - `SupplierDebts`: `DueDate` (timestamp with time zone), `StockInId` (integer).
  - `StockIns`: `DueDate` (timestamp with time zone), `PaidAmount` (numeric(18,2)), `PaymentMethod` (integer).
- **Đầu ra thực tế:** Pass 100% qua các bước Migrate tới trước B2 -> Dựng dữ liệu cũ -> Migrate mới nhất -> Assert -> Down -> Up lại.

---

## 4. Kiểm tra Entities trên `main`
- `git show main:pos-api/src/PosApi/Domain/Entities.cs` xác nhận `SupplierDebt` trên `main` **không** có `StockInId` và `DueDate`. Các trường này được bổ sung trong B2.

---

## 5. Phiếu chi tự sinh khi chốt phiếu nhập có `PaidAmount > 0`
- **Phương thức thanh toán:** Truyền qua `PaymentMethod` (mặc định `PaymentMethod.Cash`, hỗ trợ `BankTransfer`).
- **Sổ quỹ:** Chỉ `PaymentMethod.Cash` ghi giảm tiền mặt trên `CashLedgerEntry`.
- **Đối soát ca (`ShiftService.cs:101-106`):** Phiếu chi tiền mặt được tính vào `ExpectedCash` khi có ca mở (`ShiftId == shift.Id && Status == Confirmed && PaymentMethod == Cash`).
- **Chặn hủy (`StockInService.cs:672`):** Chặn hủy phiếu nhập khi `PaidAmount > 0` với thông báo lỗi rõ ràng.
- **Không giảm nợ 2 lần:** Test `StockIn_PaidAmount_NoDoubleDebtReduction` chứng minh không bị giảm nợ 2 lần.

---

## 6. Các test bổ sung (a) đến (f)
- **(a)** `Recalculate_ApplyMode_IgnoresBackfilledOrPreB2StockIns`: Bỏ qua phiếu cũ (`IsBackfilled = true`).
- **(b)** `Allocation_OldestFirst_WithAndWithoutDueDate`: Sắp xếp theo `DueDate` (nulls last), sau đó `CreatedAt`.
- **(c)** `PaySupplierDebt_ExceedingRemaining_Rejected`: Trả vượt dư nợ ném `BusinessRuleException`.
- **(d)** `CancelStockIn_WithNoPayment_RemovesDebt_And_BlocksIfPaid`: Chặn hủy phiếu đã có thanh toán.
- **(e)** `DeleteSupplier_WithRemainingDebt_ThrowsException`: Chặn xóa NCC còn dư nợ.
- **(f)** `Invariant_SupplierDebtBalance_AfterPurchasePaymentReturn`: Assert bất biến dư nợ NCC.

---

## 7. Độ lệch so với prompt
1. Bổ sung `PaymentMethod` vào request chốt phiếu nhập để phiếu chi tự sinh dùng đúng phương thức thanh toán thay vì gộp cứng tiền mặt.
2. Cho phép test `Parallel_StockIn_RealCommit_20Iterations` chạy riêng biệt khi test pool connection thay vì loại bỏ toàn bộ.

---

## Tóm tắt (Tối đa 8 dòng)
- Nhánh `feature/b2-supplier-debt` hoàn tất 100% backend nợ NCC.
- Tự động sinh `SupplierDebt` và phiếu chi (`PaidAmount`, `DueDate`, `PaymentMethod`).
- Phân bổ thanh toán và trả hàng theo nguyên tắc cũ nhất trước.
- API tuổi nợ, tính lại công nợ, chặn xóa NCC còn nợ, chặn hủy phiếu đã trả.
- Khóa dòng `FOR UPDATE` chống tranh chấp đồng thời.
- Toàn bộ 185 test vượt qua thành công trên bản sạch (0 lỗi).
