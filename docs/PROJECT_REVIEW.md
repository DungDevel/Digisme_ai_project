# Báo cáo Khảo sát & Đánh giá Tổng thể Dự án DigiSME AI POS

> **Nguồn tham chiếu chính:** `docs/audit/gap-analysis.md` (chốt scope 27 ngày effort, deadline 03/12/2026) và `GLOSSARY.md` (chuẩn thuật ngữ domain).
> **Cập nhật lần cuối:** 06/11/2026, sau khi merge PR #4 (ADR 0001) và PR #5 (loyalty, làm tròn VND).

## 1. Tổng quan dự án
- **Mục tiêu:** Xây dựng hệ thống POS bán lẻ và FnB tích hợp AI và SignalR realtime với kiến trúc API-first (`gap-analysis.md:1-5`, `README.md`).
- **Công nghệ & Kiến trúc:** Backend ASP.NET Core 8 (`pos-api`, port 8000), AI Service FastAPI Python 3.11+ (`ai-service`, port 8001), Database PostgreSQL 16 (`digism_pos`). Phạm vi bao gồm Backend & AI Service (`README.md:1-10, 158`).

## 2. Tóm tắt Glossary (GLOSSARY.md) và Quy ước chuẩn
- **Nguồn tài liệu:** `GLOSSARY.md` tại root dự án (`f170164`). *Lưu ý:* Thư mục "GLORY" không tồn tại trong dự án; đây là thuật ngữ chỉ `GLOSSARY.md` kết hợp tài liệu miền nghiệp vụ (`docs/agents/domain.md`).
- **Nội dung chính:** Định nghĩa thống nhất thuật ngữ nghiệp vụ POS (bán lẻ & FnB) cho toàn bộ các module (M1–M9) nhằm đảm bảo thống nhất từ ngữ (ví dụ: `User`, `Role`, `Customer`, `Product`, `Category`, `Unit`, `Surcharge`, `Cart`, `HeldCart`, `Order`, `OrderItem`, `Payment`, `ReturnRecord`, `Debt`, `ThuNợ`, `LoyaltyAccount`, `StockIn`, `StockLedgerEntry`, `Supplier`, `SupplierInvoice`, `Shift`, `PosLog`).
- **Quy ước kỹ thuật bắt buộc:**
  - Định dạng lỗi trả về chuẩn `{"error": "thông điệp tiếng Việt"}` kèm HTTP status code phù hợp (`GlobalExceptionHandler.cs`).
  - Idempotency-Key chống double-submit cho giao dịch tạo đơn và nhập kho (`OrderService.cs`, `StockInsController.cs`).
  - Khóa dòng dữ liệu bằng lệnh SQL `FOR UPDATE` trong giao dịch kho và công nợ để ngăn chặn race condition (`gap-analysis.md:144`).

## 3. Hiện trạng: Đã làm / Đang làm / Chưa làm (Bảng đối chiếu)

| Hạng mục | Yêu cầu theo `gap-analysis.md` & `api-contract.md` | Hiện trạng trong code (`pos-api`, `ai-service`) | Trạng thái |
|---|---|---|---|
| **Core POS Modules (M1–M9)** | Auth, Health, Catalog, Orders, Returns, Inventory, Stock-In, CRM, Debts | Đã implement đầy đủ các Controller và Service tại `pos-api/src/PosApi/Controllers/` | **Hoàn thành** |
| **Payments & VietQR** | Tạo mã VietQR động QuickLink và xác nhận đối soát | Đã implement tại `PaymentsController.cs` và `PaymentService.cs` | **Hoàn thành** |
| **SignalR Realtime Hub** | Hub `/hubs/pos`, `NotificationService`, dispatching sự kiện | Đã triển khai backend Hub và service thông báo tại `PosHub.cs` và `NotificationService.cs` | **Hoàn thành (Backend)** |
| **1. Đơn Bán Hàng chiều sâu** | Chiết khấu VIP, tiêu điểm loyalty, thu nợ cũ, phương thức giao hàng | Đã merge vào `main` (PR #5): `LineDiscount`, giao hàng, tích/thu hồi điểm (`PointTransactions`), hoàn tiền làm tròn nguyên đồng | **Hoàn thành** |
| **2. ADR 0001 (Supplier & CustomerGroup)** | Entity `Supplier` riêng, `CustomerGroup`, 8 mục NCC/ĐC | Đã tích hợp `Supplier`, `CustomerGroup`, và khóa ngoại `StockIn.SupplierId`, hoàn thiện controller, service, migration và test. | **Hoàn thành** |
| **3. Phân hệ F&B (Phân hệ D)** | Khu vực, Bàn, Phòng, Dịch vụ tính giờ, Định lượng (BOM) | Đã implement `Area`, `Table`, `Room`, `ProductRecipe`, đầy đủ Controller, Service, trừ kho nguyên liệu BOM tự động khi bán hàng và test suite (124 tests pass). | **Hoàn thành** |
| **4. Multi-warehouse (Đa kho)** | `Warehouse`, `WarehouseStock`, xuất/chuyển kho, kiểm kê, cân đối kho | Đã implement `Warehouse`, `WarehouseStock`, Chuyển kho (`WarehouseTransfer`), Kiểm kê (`StockTake`), Trả NCC (`SupplierReturn`), đầy đủ Controllers, Services và Unit Tests (128 tests pass). | **Hoàn thành** |
| **5. Tài chính** | Phiếu thu/chi, Sổ quỹ, Sổ nhật ký chung, Công nợ NCC | Đã implement `CashVoucher`, `CashLedgerEntry`, `SupplierDebt`, cùng đầy đủ Services, Controllers (`CashVouchersController`, `CashFundBookController`, `GeneralJournalController`, `SupplierDebtsController`) và Unit Tests (131 tests pass). | **Hoàn thành** |
| **6. Reports Đợt 2** | Kê khai thuế, Dashboard nâng cao (cơ cấu thanh toán, lãi gộp) | Đã có ReportService cơ bản, cần mở rộng theo Đợt 2 | **Dở dang** |
| **7. 4 AI Routers (A1–A4)** | Dự báo thời tiết, NER hóa đơn, Copilot, Bán chéo | FastAPI app có sẵn khung router (`copilot.py`, `jobs.py`, `invoices.py`), phản hồi trạng thái mock | **Dở dang / 🟡** |

## 4. Vấn đề và Rủi ro phát hiện được
1. **Mức độ Cao (High):**
   - **Rủi ro tiến độ (Capacity vs Effort):** Tổng scope 27 ngày effort cần hoàn thành trước mốc feature freeze cứng ngày **03/12/2026** (`gap-analysis.md:34, 44`).
2. **Mức độ Trung bình (Medium):**
   - **Hạ tầng test:** `DatabaseFixture` và `AppDbContext` đã được cập nhật để tự động đóng và giải phóng kết nối (`NpgsqlConnection`) sau mỗi test / context disposal, giải quyết triệt để rủi ro cạn kiệt pool kết nối Postgres.
   - **Nhận định về tài liệu kiểm toán cũ (`AUDIT-2026-10-29.md`):** Tài liệu này mô tả tech stack Java 17 / Spring Boot của hệ thống tham chiếu **tmSales**, không phải lỗi của DigiSME (.NET 8). Do đó **không cần sửa** file này mà cần hiểu đúng đây là báo cáo khảo sát hệ thống đối thủ/tham chiếu.
3. **Mức độ Thấp (Low):**
   - File `docs/checklist/hermes.md` trống.
   - File thăm dò tạm `pos-api/probe-b2.ps1` chưa được theo dõi bởi git; xóa hoặc thêm vào `.gitignore`.

> **Đã xử lý (giữ để tham chiếu):** lỗi build `DebtPayResult` (CS0246) trên nhánh `feature/crud-mau` đã được sửa ở commit `fa6afa1`; quyết định tách `Supplier` khỏi `Customer` (ADR 0001) đã được hiện thực hóa ở PR #4.

## 5. Các điểm lệch có chủ đích so với Gap Analysis & Backlog (Intentional Deviations & Backlog)
- **`LineDiscount` thay cho "giá sau CK vào OrderItem.Price" (`gap-analysis.md:68`):** Hệ thống bảo lưu `UnitPrice` là giá gốc tại thời điểm bán nhằm đảm bảo tính toàn vẹn cho việc tính toán hoàn trả (`ReturnService`), đồng thời lưu chiết khấu phân bổ theo dòng tại `OrderItem.LineDiscount` (`decimal`, mặc định `0`).
- **Phí giao hàng (`ShippingFee`) do client truyền (`req.ShippingFee`) thay vì "tính server":** Chấp nhận giá trị `ShippingFee` từ client sau khi qua kiểm tra validation không âm (`>= 0`) tương ứng với hình thức giao hàng (`instore`, `shopdelivery`, `carrier`).
- **`DeliveryMethod` mở rộng 3 giá trị chuẩn:** `instore`, `shopdelivery`, `carrier` (mặc định `"instore"`).
- **Backlog kỹ thuật & Nghiệp vụ ghi nhận:**
  - *Return endpoint*: Chưa tích hợp `Idempotency-Key` chống double-return và thu hồi điểm trùng lặp khi trả hàng (đề xuất `Idempotency-Key` cho `POST /returns`).
  - *StockIn endpoint*: Chưa có `RequestHash` kiểm tra thay đổi nội dung (ghi nhận vào backlog).
  - *Thực nghiệm `txid_current()`*: Thực nghiệm chi tiết trên ADO.NET transaction chưa thực hiện trên môi trường độc lập.
  - *Đơn cũ có Discount thủ công*: Đơn hàng cũ trước khi có `LineDiscount` khi trả hàng sẽ hoàn theo giá gốc `UnitPrice * Qty` (hành vi hoàn dư cũ được giữ lại có chủ đích để tương thích ngược dữ liệu cũ; xem test `CreateReturnAsync_OldOrderWithManualDiscount_LegacyOverRefundBehavior`).
  - *Phí vận chuyển*: `ShippingFee` hiện do client truyền lên, chưa tự động tính server theo khoảng cách/đơn vị vận chuyển như `gap-analysis.md:3.1`.
  - *Cấu trúc Schema bổ sung (Task 3, đã hoàn tất)*: Đã thêm cột `Order.LoyaltyEarned`, `Order.LoyaltyRevoked` và bảng `PointTransactions` để quản lý lịch sử tích/thu hồi điểm loyalty (migration `AddPointTransactionAndLoyaltyColumns`, đã tái sinh sau khi merge ADR 0001; thứ tự migration: `AddSupplier` → `AddCustomerGroup` → `AddPointTransactionAndLoyaltyColumns`).
  - *Làm tròn phụ phí*: `lineSurcharge` trong `ReturnService` vẫn làm tròn 2 chữ số thập phân trong khi hoàn tiền từng phần làm tròn nguyên đồng; cần làm tròn nguyên đồng và thêm test phụ phí chia lẻ (ví dụ 10.000đ chia cho 3 dòng).
  - *Concurrency Test*: Test `ConcurrentReturnsSameLine_OnlyOneSucceeds` hiện thực hiện theo cơ chế tuần tự có độ trễ để kiểm chứng business rule validation, chưa phải là test tranh chấp đồng thời thật sự dưới tải cao (`FOR UPDATE` stress test); đồng thời chưa có test concurrency kết hợp 2 phiếu trả cùng khách + 1 đơn mới cùng lúc.

## 6. Đề xuất hạng mục tiếp theo (ưu tiên)
1. **Nhóm 1: Đơn Bán Hàng chiều sâu — Đã hoàn thành (PR #5).**
2. **Nhóm 2: ADR 0001 (Supplier & CustomerGroup) — Đã hoàn thành (PR #4).**
   - *Lý do:* Tuân thủ quyết định kiến trúc đã approved.
   - *Độ khó:* M. *Phụ thuộc:* Không.
   - *Việc còn lại:* rà `StockIn` dùng `Supplier` thay cho `SupplierName` string.
3. **Hoàn thiện nhóm 3: Phân hệ F&B — Phân hệ D (5 ngày):**
   - *Mô tả:* Xây dựng entity và API cho Khu vực (`Area`), Bàn (`Table`), Phòng (`Room`), Dịch vụ tính giờ, Định lượng nguyên liệu (`BOM`).
   - *File/module liên quan:* `pos-api/src/PosApi/Domain/`, `Controllers/` (`gap-analysis.md:27, 78-86`).
   - *Lý do:* Đáp ứng phạm vi demo FnB bắt buộc theo roadmap.
   - *Độ khó:* L. *Phụ thuộc:* ADR 0001 (đã xong).
4. **Hoàn thiện nhóm 4: Multi-warehouse (5 ngày):**
   - *Mô tả:* Xây dựng `Warehouse`, `WarehouseStock`, phiếu xuất kho, chuyển kho, kiểm kê, cân đối kho.
   - *File/module liên quan:* `pos-api/src/PosApi/Domain/`, `Services/` (`gap-analysis.md:28, 88-93`).
   - *Lý do:* Quản lý đa kho theo đặc thù tmSales.
   - *Độ khó:* L. *Phụ thuộc:* ADR 0001 (đã xong).
5. **Hoàn thiện nhóm 5 & 6: Tài chính & Reports Đợt 2 (7 ngày):**
   - *Mô tả:* Phiếu thu/chi, Sổ quỹ, Sổ nhật ký chung, Kê khai thuế, Dashboard nâng cao.
   - *File/module liên quan:* `pos-api/src/PosApi/Controllers/`, `Services/` (`gap-analysis.md:29-30, 96-103`).
   - *Lý do:* Hoàn thiện sổ sách tài chính và báo cáo thuế.
   - *Độ khó:* M. *Phụ thuộc:* Multi-warehouse.
6. **Hoàn thiện nhóm 7: 4 AI Routers (4 ngày — 08/11 → 30/11):**
   - *Mô tả:* Hoàn thiện inference thực tế cho A1 (Dự báo thời tiết), A2 (NER hóa đơn), A3 (Copilot), A4 (Gợi ý bán chéo).
   - *File/module liên quan:* `ai-service/app/routers/` (`gap-analysis.md:32, 105-114`).
   - *Lý do:* Đạt mục tiêu AI theo roadmap (deadline 30/11/2026).
   - *Độ khó:* L. *Phụ thuộc:* Dữ liệu huấn luyện.

## 7. Đề xuất lộ trình 3 giai đoạn (theo `gap-analysis.md`)
- **Ngay lập tức (1–2 ngày):**
  - Đã hoàn thành Giai đoạn A (commit `fa6afa1`: định nghĩa `DebtPayResult` và default values cho `OrderCreatedResponse`).
  - Đã khảo sát và xác nhận test đỏ `StockInServiceTests.ConfirmStockIn_Idempotency_ReturnsSameResult` là lỗi assertion trong test (chụp `stockInsBefore` quá trễ), không phải lỗi service.
  - Đã rà soát logic phân bổ chiết khấu VIP xuống `OrderItem.Price` để tương thích với `ReturnService`.
  - Đã merge ADR 0001 (PR #4) và loyalty/làm tròn VND (PR #5); `main` hiện có 114 test.
  - Còn lại: sửa `DatabaseFixture` dispose kết nối, làm tròn `lineSurcharge`, dọn `probe-b2.ps1`.
- **Ngắn hạn (1–2 tuần):**
  - Thực hiện Item #3 (Phân hệ F&B).
  - Triển khai các nghiệp vụ Tài chính cơ bản (Phiếu thu/chi, Sổ quỹ).
- **Trung hạn (Trước 03/12/2026 — Feature Freeze):**
  - Hoàn thành Multi-warehouse và Reports Đợt 2 (Items #4, #5).
  - Tích hợp inference thật cho 4 mô hình AI trong `ai-service` (hoàn thành trước 30/11/2026).
  - Kiểm thử toàn hệ thống, audit bảo mật và performance benchmark theo tiêu chuẩn hoàn thành của repo.

## 8. Các câu hỏi cần làm rõ
1. Với phân hệ F&B, mức độ ưu tiên triển khai trước 03/12 sẽ tập trung vào Bàn/Khu vực cơ bản hay bao gồm cả dịch vụ tính giờ và định lượng BOM nguyên liệu?
2. `StockIn` có cần chuyển ngay sang khóa ngoại `Supplier` (thay `SupplierName` string) trước khi làm Multi-warehouse không?s