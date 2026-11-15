# Bằng chứng kiểm tra và hoàn tất B2 (Evidence 9)

## HEAD Commit & Git Status
- `git rev-parse --short HEAD`: `7bb0297`
- `git status --short`: (Sạch sẽ trước khi viết báo cáo)

---

## 1. Kết quả chạy Test trên bản clone sạch trong %TEMP%
- Chạy `dotnet test pos-api/tests/PosApi.Tests` trên bản clone sạch `%TEMP%`:
  ```
  Test run for E:\...\pos-api\tests\PosApi.Tests\bin\Debug\net8.0\PosApi.Tests.dll (.NETCoreApp,Version=v8.0)
  VSTest version 17.11.1 (x64)

  Starting test execution, please wait...
  A total of 1 test files matched the specified pattern.

  Passed!  - Failed:     0, Passed:   191, Skipped:     0, Total:   191, Duration: 13 s - PosApi.Tests.dll (net8.0)
  ```
- Toàn bộ **191/191 test** PASS, không có lỗi.

---

## 2. Việc 1: Thiết kế lại Test Đồng thời (CancelStockIn vs Payment)
- **Vấn đề đã khắc phục:** Test cũ dựa vào TaskCompletionSource thời điểm và catch bool, không chứng minh khóa `FOR UPDATE` chặn thực tế.
- **Thiết kế mới (đã chạy thành công 3/3 lần liên tiếp riêng lẻ và 2/2 lần toàn bộ):**
  1. Giao dịch thanh toán giữ khóa dòng `SupplierDebts` (`FOR UPDATE`, chưa commit); tiến hành gọi `CancelStockInAsync` đồng thời -> assert tác vụ hủy bị chặn (không hoàn thành sau 500ms).
  2. Commit giao dịch thanh toán -> hủy unblock và bị từ chối với `BusinessRuleException` ("phát sinh thanh toán").
  3. Kịch bản ngược: Hủy/giao dịch giữ khóa (`FOR UPDATE`) trước, gọi trả nợ đồng thời -> trả nợ bị chặn, sau khi hủy hoàn tất/commit thì trả nợ ném `KeyNotFoundException`.

---

## 3. Việc 2: Cập nhật Recalculate & Q20
- Sửa `Recalculate_ApplyMode_DoesNotChangeSupplierDebts` để chụp snapshot tập `SupplierDebts` trước và sau hai lần gọi `RecalculateAsync(preview: false)` và assert không đổi.
- Cập nhật test `Recalculate_ReportsDiscrepancyForOldStockInWithoutDebt` ghi nhận hành vi thực tế: phiếu nhập cũ không có nợ được báo lệch (discrepancy) khi chạy preview.
- Cập nhật `docs/analysis/group8-open-questions.md` (Q20): làm rõ các hằng `B2_MIGRATION_DATE`, `B2_ROLLOUT_DATE`, `IsB2Applied` không tồn tại trong mã thực tế; `Recalculate` chỉ báo lệch chứ không tạo nợ mới tự động.

---

## 4. Việc 3: Test Migration (`MigrationTest_IMigrator_VerifyOldDataAndRollback`)
- Dùng database riêng biệt (tên chứa Guid), tự động dọn dẹp trong `finally`.
- Assert thông qua các truy vấn `information_schema.columns` riêng biệt cho từng cột (`SupplierDebts`: `DueDate`, `StockInId`; `StockIns`: `DueDate`, `PaidAmount`, `PaymentMethod`) và kiểm tra `pg_indexes` cho `IX_SupplierDebts_StockInId_Unique`.
- Kiểm tra migrate lên, migrate xuống (Down), và migrate lên lại: dữ liệu cũ được giữ nguyên, các cột và index biến mất khi Down và xuất hiện đầy đủ khi Up lại.

---

## 5. Kiểm tra DriftCheck
- Chạy `dotnet ef migrations add DriftCheck` trên bản clone tạm:
  - Phương thức `Up` và `Down` hoàn toàn rỗng (`{ }`), chứng minh không có model drift và khóa ngoại `Restrict` được duy trì chính xác. Đã xóa migration tạm.

---

## Tóm tắt cuối
- Đã hoàn thành thiết kế lại test đồng thời bằng khóa `FOR UPDATE` thực tế.
- Toàn bộ 191/191 test chạy pass trên bản sạch trong `%TEMP%`.
- Cập nhật tài liệu Q20 và bổ sung kiểm tra migration đầy đủ.
- Không tồn tại model drift hay thay đổi cấu trúc bảng trái phép.
