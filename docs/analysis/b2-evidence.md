# Bằng chứng thực hiện nhiệm vụ B2 — Ghi nợ nhà cung cấp (backend)

## 1. Khảo sát (Bước 0)
- **a. Entity SupplierDebt (`pos-api/src/PosApi/Domain/Entities.cs:184-196`):** Lưu theo từng chứng từ phát sinh (`Amount`, `PaidAmount`, `Status`, `ReferenceType`, `ReferenceId`, `StockInId`, `DueDate`).
- **b. SupplierDebtService & SupplierReturnService:** `SupplierDebtService` quản lý danh sách và thanh toán công nợ NCC. `SupplierReturnService` duyệt phiếu trả hàng NCC nhưng chưa tự động giảm nợ (đã bổ sung tích hợp). Chưa có tính năng tuổi nợ.
- **c. Phiếu chi (`CashVoucher`):** Đã có gắn `PartnerType = PartnerType.Supplier`, `PartnerId = SupplierId`, và `PaymentMethod`.
- **d. Chốt phiếu nhập (`StockInService`):** Đã tích hợp tự động tạo `SupplierDebt` khi `Total - PaidAmount > 0` trong cùng transaction chốt phiếu. Hủy phiếu kiểm tra thanh toán phân bổ và hoàn nợ.
- **e. DebtService (nợ khách):** Mẫu chuẩn về `ApplyPayment` và row-level locking `FOR UPDATE`.
- **f. Q18:** `ProductService` (`CreateAsync`/`UpdateAsync`) không khởi tạo/cập nhật `WarehouseStock` khi có tồn đầu, gây lệch bất biến Q14 (đã báo cáo vào `group8-open-questions.md`).

---

## 2. Kết quả chạy test trên bản sạch (PowerShell)
```
Test run for E:\DigiSME_AI_Project\pos-api\tests\PosApi.Tests\bin\Debug\net8.0\PosApi.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 17.11.1 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:   180, Skipped:     0, Total:   180, Duration: 9 s - PosApi.Tests.dll (net8.0)
```

---

## 3. Git Status & Commit Log
```
389cdd9 feat(pos): implement B2 supplier debt features, aging, recalculate, concurrency and tests
```
`git status --short` sạch sẽ.
