# Báo cáo xác minh chi tiết (Nhóm 8 — B1–B9)

Báo cáo này đối chiếu các điểm yêu cầu kiểm tra với codebase thực tế của dự án DigiSME tại `E:\DigiSME_AI_Project`, kèm bằng chứng đường dẫn file và số dòng (`file:line`).

---

## 1. Xác minh V1, V3, V4 (Doanh số, Kho, Chốt ca)
- **Kết quả chạy test thực tế:** `dotnet test` trả về **146 passed, 0 failed, 0 skipped, Tổng số 146 test methods** (trong xUnit, mỗi test method / theory tương ứng 1 test case).
- **V1 (Doanh số):** Đã trừ tiền trả hàng (`TotalReturned`) trong báo cáo doanh thu (`SalesReport`).
  - *Bằng chứng code:* `pos-api/src/PosApi/Services/ReportService.cs:45-50`
  ```csharp
  var totalReturned = await _context.Returns
      .AsNoTracking()
      .Where(r => r.CreatedAt >= startDate && r.CreatedAt <= endDate)
      .SumAsync(r => (decimal?)r.TotalReturned) ?? 0m;
  totalRevenue = Math.Max(0m, totalRevenue - totalReturned);
  ```
- **V3 (Kho khi trả hàng):** Trả hàng (`ReturnService`) đã cập nhật chính xác tồn kho theo kho (`WarehouseStock`) và ghi sổ kho (`StockLedgerEntry`) đúng kho phát sinh.
  - *Bằng chứng code:* `pos-api/src/PosApi/Services/ReturnService.cs:273-316`
- **V4 (Chốt ca / Mở ca):** `ShiftService` tính toán chính xác tiền mặt dự kiến (`ExpectedCash`), tiền chuyển khoản (`ExpectedTransfer`) và chênh lệch ca.
  - *Bằng chứng code:* `pos-api/src/PosApi/Services/ShiftService.cs:83-110` (`GetShiftSummaryAsync`) và `pos-api/tests/PosApi.Tests/ShiftServiceTests.cs`.

---

## 2. Kết quả chạy `dotnet test`
- **Tổng số test methods / test cases:** **146**.
- **Passed:** 146.
- **Failed:** 0.
- **Skipped:** 0.

---

## 3. Kiểm tra chi tiết Nhóm 1, 2, 6 của Gap Analysis
- **Nhóm 1 (Đơn Bán Hàng chiều sâu):**
  - Chiết khấu VIP: **Đã có** (`OrderService.cs`, áp dụng `Customer.IsVip` hoặc `CustomerGroup.DefaultDiscountRate`).
  - Dùng điểm (Loyalty): **Đã có** (`Entities.cs:135-168`, `PointTransaction`, `OrderService.cs`).
  - Thu nợ cũ: **Đã có** (`DebtsController.cs`, `DebtService.cs`).
  - Hình thức giao (Delivery method & fee): **Đã có** (`Order.DeliveryMethod`, `Order.ShippingFee`, `Entities.cs:235, 240`).
- **Nhóm 2 (ADR 0001 — `Supplier`, `CustomerGroup`, 8 mục NCC/ĐC):**
  - Liệt kê 8 mục ADR 0001 trong code:
    1. CRUD NCC: **Đã có** (`SupplierService.cs`, `SuppliersController.cs`).
    2. CRUD nhóm ĐC/NCC (`CustomerGroup`): **Đã có** (`CustomerGroupService.cs`, `CustomerGroupsController.cs`).
    3. Cờ nhóm "Là NCC" (`IsSupplier`): **Đã có** (`CustomerGroup.IsSupplier`, `Entities.cs:418`).
    4. Bảng giá áp dụng theo ĐC: **Có một phần** (`Product` có giá lẻ/sỉ/thành viên, `CustomerGroup` có chiết khấu mặc định).
    5. `SupplierInvoice` gắn `SupplierId`: **Đã có** (`SupplierInvoice` / `StockIn` gắn `SupplierId`, `Entities.cs:392`).
    6. "Tính lại công nợ": **Chưa có** (chưa có hàm batch recalculate debt).
    7. Màn hình NCC/ĐC list + chi tiết: **Chưa có** (chỉ có backend API, frontend chưa xác minh trong repo backend).
    8. Migration EF (`AddSupplier`, `AddCustomerGroup`): **Đã có** (`Migrations/20261106033915_AddSupplier.cs`, v.v.).
- **Nhóm 6 (Reports Đợt 2):**
  - Kê khai thuế: **Đã có** (`ReportService.GetTaxDeclarationAsync`, `ReportService.cs`).
  - Dashboard nâng cao (COGS, gross profit, slow-moving): **Đã có** (`ReportService.GetAdvancedDashboardAsync`, `ReportService.cs`).

---

## 4. Xác minh B2 (Ghi nợ nhà cung cấp)
- `ConfirmStockInAsync` (`pos-api/src/PosApi/Services/StockInService.cs`) **chưa** tự động tạo hoặc tăng `SupplierDebt`.
- Phiếu chi hoặc trả NCC (`SupplierReturnService.cs`) **chưa** tự động giảm nợ `SupplierDebt`.
- Các file dịch vụ và controller công nợ NCC tồn tại:
  - `pos-api/src/PosApi/Services/SupplierDebtService.cs:1` (`class SupplierDebtService`)
  - `pos-api/src/PosApi/Controllers/SupplierDebtsController.cs:1` (`class SupplierDebtsController`)

---

## 5. Xác minh B1 (Nhập kho)
- Khi chưa có `WarehouseId` trong `StockIn`, hệ thống hiện cộng thẳng vào `Products.CurrentStock` tổng (`StockInService.cs`).
- Trạng thái `StockIn` hiện dựa trên `SupplierInvoiceStatus` (`PendingReview`, `Confirmed`, `Rejected`), chưa có các trạng thái Nháp / Đã chốt / Hủy riêng cho `StockIn`.
- Chưa có cơ chế hủy phiếu nhập bằng bút toán đảo (`StockLedgerEntry`).

---

## 6. Xác minh B5, B6, B9 qua từ khóa và file
- **SignalR Hub:** `pos-api/src/PosApi/Hubs/PosHub.cs:1` (`class PosHub : Hub`).
- **VietQR động (POS06):** `pos-api/src/PosApi/Services/PaymentService.cs:1` (`GenerateVietQrAsync`, `ConfirmPaymentAsync`).
- **Router A2 / Dịch vụ AI:** `ai-service/app/routers/invoices.py:1` (tìm thấy thư mục `ai-service/` với các router FastAPI cho invoices, copilot, jobs).

---

## 7. Xác minh Frontend POS và Quản lý ca (B7)
- **Frontend POS:** Không tồn tại trong repository này (dự án hiện tại là backend API chuẩn .NET 8 và Python AI service).
- **Chặn bán khi chưa mở ca:** Hiện tại backend (`OrderService.cs`) **chưa** bắt buộc kiểm tra ca đang mở (`Shift`) khi tạo đơn hàng (mặc dù `Order` entity có trường `ShiftId`, xem `Entities.cs:228`).
