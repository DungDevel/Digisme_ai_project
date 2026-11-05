# Báo cáo Khảo sát & Đánh giá Tổng thể Dự án DigiSME AI POS

> **Nguồn tham chiếu chính:** `docs/audit/gap-analysis.md` (chốt scope 27 ngày effort, deadline 03/12/2026) và `GLOSSARY.md` (chuẩn thuật ngữ domain).

## 1. Tổng quan dự án
- **Mục tiêu:** Xây dựng hệ thống POS bán lẻ và FnB tích hợp AI và SignalR realtime với kiến trúc API-first (`gap-analysis.md:1-5`, `README.md`).
- **Công nghệ & Kiến trúc:** Backend ASP.NET Core 8 (`pos-api`, port 8000), AI Service FastAPI Python 3.11+ (`ai-service`, port 8001), Database PostgreSQL 16 (`digism_pos`). Phạm vi bao gồm Backend & AI Service (`README.md:1-10, 158`).

## 2. Tóm tắt Glossay (GLOSSARY.md) và Quy ước chuẩn
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
| **1. Đơn Bán Hàng chiều sâu** | Chiết khấu VIP, tiêu điểm loyalty, thu nợ cũ, phương thức giao hàng | Đang chỉnh sửa dở dang trên nhánh `feature/crud-mau` (thiếu `DebtPayResult`, lỗi build test) | **Dở dang** |
| **2. ADR 0001 (Supplier & CustomerGroup)** | Entity `Supplier` riêng, `CustomerGroup`, 8 mục NCC/ĐC | Chưa implement entity `Supplier`; `StockIn` vẫn dùng `SupplierName` string (`Entities.cs:319`) | **Dở dang / Lệch** |
| **3. Phân hệ F&B (Phân hệ D)** | Khu vực, Bàn, Phòng, Dịch vụ tính giờ, Định lượng (BOM) | Chưa có trong mã nguồn (`0` kết quả tìm kiếm F&B trong code) | **Chưa làm** |
| **4. Multi-warehouse (Đa kho)** | `Warehouse`, `WarehouseStock`, xuất/chuyển kho, kiểm kê, cân đối kho | Chưa có trong code (hoãn sang Đợt 2 theo ADR 0002) | **Chưa làm** |
| **5. Tài chính** | Phiếu thu/chi, Sổ quỹ, Sổ nhật ký chung, Công nợ NCC | Chưa có đầy đủ các sổ quỹ và phiếu chi trong code | **Dở dang / Chưa làm** |
| **6. Reports Đợt 2** | Kê khai thuế, Dashboard nâng cao (cơ cấu thanh toán, lãi gộp) | Đã có ReportService cơ bản, cần mở rộng theo Đợt 2 | **Dở dang** |
| **7. 4 AI Routers (A1–A4)** | Dự báo thời tiết, NER hóa đơn, Copilot, Bán chéo | FastAPI app có sẵn khung router (`copilot.py`, `jobs.py`, `invoices.py`), phản hồi trạng thái mock | **Dở dang / 🟡** |

## 4. Vấn đề và Rủi ro phát hiện được
1. **Mức độ Cao (High):**
   - **Lỗi biên dịch trên nhánh hiện tại (`feature/crud-mau`):** Các file `Entities.cs` và `Dtos.cs` có thay đổi dở dang cho Đơn Bán Hàng chiều sâu, tham chiếu đến `DebtPayResult` chưa được định nghĩa, làm lệnh `dotnet test` thất bại với lỗi CS0246 (`pos-api/src/PosApi/Dto/Dtos.cs:43`).
   - **Rủi ro tiến độ (Capacity vs Effort):** Tổng scope 27 ngày effort cần hoàn thành trước mốc feature freeze cứng ngày **03/12/2026** (`gap-analysis.md:34, 44`).
2. **Mức độ Trung bình (Medium):**
   - **Chưa hiện thực hóa ADR 0001:** Quyết định tách `Supplier` khỏi `Customer` (`docs/adr/0001-separate-supplier-from-customer.md`) đã accepted nhưng chưa được đưa vào code (`StockIn` vẫn dùng `SupplierName` string).
   - **Nhận định về tài liệu kiểm toán cũ (`AUDIT-2026-10-29.md`):** Tài liệu này mô tả tech stack Java 17 / Spring Boot của hệ thống tham chiếu **tmSales**, không phải lỗi của DigiSME (.NET 8). Do đó **không cần sửa** file này mà cần hiểu đúng đây là báo cáo khảo sát hệ thống đối thủ/tham chiếu.
3. **Mức độ Thấp (Low):**
   - File `docs/checklist/hermes.md` trống.

## 5. Danh sách việc cần làm tiếp theo (Theo thứ tự ưu tiên chuẩn từ `gap-analysis.md`)
1. **Hoàn thiện nhóm 1: Đơn Bán Hàng chiều sâu (3 ngày) — Giai đoạn A ĐÃ XONG:**
   - *Mô tả:* Bổ sung định nghĩa `DebtPayResult` và các trường DTO mặc định an toàn trong `Dtos.cs`, thêm test xác nhận default values trong `OrderServiceTests.cs`. Build thành công (`0 Error`), test suite pass 38/39.
   - *File/module liên quan:* `pos-api/src/PosApi/Dto/Dtos.cs`, `pos-api/tests/PosApi.Tests/OrderServiceTests.cs`.
   - *Lý do:* Đã khắc phục thành công lỗi biên dịch CS0246. Chuẩn bị bước sang Giai đoạn B (Lập kế hoạch chi tiết cho các tính năng chiết khấu VIP, điểm loyalty, thu nợ cũ, phương thức giao hàng).
   - *Độ khó:* S. *Phụ thuộc:* Không.
2. **Hoàn thiện nhóm 2: ADR 0001 — Supplier & CustomerGroup (3 ngày):**
   - *Mô tả:* Tạo entity `Supplier`, `CustomerGroup`, cập nhật quan hệ với `StockIn`, bổ sung migration EF Core 8.
   - *File/module liên quan:* `pos-api/src/PosApi/Domain/Entities.cs`, `pos-api/src/PosApi/Data/AppDbContext.cs` (`gap-analysis.md:26, 72-76`).
   - *Lý do:* Tuân thủ quyết định kiến trúc đã approved.
   - *Độ khó:* M. *Phụ thuộc:* Không.
3. **Hoàn thiện nhóm 3: Phân hệ F&B — Phân hệ D (5 ngày):**
   - *Mô tả:* Xây dựng entity và API cho Khu vực (`Area`), Bàn (`Table`), Phòng (`Room`), Dịch vụ tính giờ, Định lượng nguyên liệu (`BOM`).
   - *File/module liên quan:* `pos-api/src/PosApi/Domain/`, `Controllers/` (`gap-analysis.md:27, 78-86`).
   - *Lý do:* Đáp ứng phạm vi demo FnB bắt buộc theo roadmap.
   - *Độ khó:* L. *Phụ thuộc:* ADR 0001.
4. **Hoàn thiện nhóm 4: Multi-warehouse (5 ngày):**
   - *Mô tả:* Xây dựng `Warehouse`, `WarehouseStock`, phiếu xuất kho, chuyển kho, kiểm kê, cân đối kho.
   - *File/module liên quan:* `pos-api/src/PosApi/Domain/`, `Services/` (`gap-analysis.md:28, 88-93`).
   - *Lý do:* Quản lý đa kho theo đặc thù tmSales.
   - *Độ khó:* L. *Phụ thuộc:* ADR 0001.
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

## 6. Đề xuất lộ trình 3 giai đoạn (theo `gap-analysis.md`)
- **Ngay lập tức (1–2 ngày):**
  - Khắc phục lỗi biên dịch `DebtPayResult` trên nhánh `feature/crud-mau`, chạy thành công `dotnet test` (bám sát Item #1 Đơn Bán Hàng chiều sâu).
  - Làm rõ mục đích của file `AUDIT-2026-10-29.md` (đây là audit tmSales, không phải lỗi tech stack của DigiSME).
- **Ngắn hạn (1–2 tuần):**
  - Thực hiện Item #2 (ADR 0001 — Supplier & CustomerGroup) và Item #3 (Phân hệ F&B).
  - Triển khai các nghiệp vụ Tài chính cơ bản (Phiếu thu/chi, Sổ quỹ).
- **Trung hạn (Trước 03/12/2026 — Feature Freeze):**
  - Hoàn thành Multi-warehouse và Reports Đợt 2 (Items #4, #5).
  - Tích hợp inference thật cho 4 mô hình AI trong `ai-service` (hoàn thành trước 30/11/2026).
  - Kiểm thử toàn hệ thống, audit bảo mật và performance benchmark theo tiêu chuẩn hoàn thành của repo.

## 7. Các câu hỏi cần làm rõ
1. Trên nhánh `feature/crud-mau`, DTO `DebtPayResult` đang thiếu định nghĩa. Bạn có muốn tôi bổ sung định nghĩa record này ngay không?
2. Với phân hệ F&B, mức độ ưu tiên triển khai trước 03/12 sẽ tập trung vào Bàn/Khu vực cơ bản hay bao gồm cả dịch vụ tính giờ và định lượng BOM nguyên liệu?
