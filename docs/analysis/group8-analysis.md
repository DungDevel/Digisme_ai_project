# Phân tích chi tiết tính năng nhóm 8 (B1–B9) đối chiếu với codebase DigiSME

> **Ngày lập báo cáo:** 12/11/2026  
> **Phạm vi:** Nhóm 8 "Bổ sung 11/11" (B1–B9) đối chiếu với `docs/audit/gap-analysis.md` và `docs/Chi tiết tính năng nhóm 8 (B1–B9) — bản điều chỉnh 11 11.md`.  
> **Ràng buộc:** Chỉ đọc và phân tích, không sửa code/migration. Ghi kết quả tại `docs/analysis/`.

---

## 1. Bảng tổng hợp hiện trạng và effort nhóm 8

| Mã | Tên chức năng | Nhóm | Effort mới | Trạng thái hiện tại | Mức tin cậy | Rủi ro chính |
|---|---|---|---|---|---|---|
| **B1** | Nhập kho | Đầu vào | 1,5 ngày | Có một phần | Cao | Chưa gắn `WarehouseId` vào chứng từ nhập |
| **B2** | Ghi nợ nhà cung cấp | Đầu vào | 1 ngày | Có một phần | Cao | `Debt` hiện gắn `Customer`, cần đồng bộ `SupplierDebt` |
| **B3** | Rà soát kho đầu ra | (Gộp) | 0 ngày riêng | Đã có / Đang chuẩn hóa | Cao | Sai lệch tồn kho khi bán/trả hàng |
| **B4** | Thao tác bàn phím | Đầu ra | 1,5 ngày | Chưa có (FE) | Trung bình | Xung đột phím tắt với trình duyệt và Unikey |
| **B5** | Tích hợp SePay | Đầu ra | 3 ngày | Chưa có | Trung bình | Đồng bộ trạng thái đơn, xử lý lệch tiền |
| **B6** | Điện thoại làm máy quét mã | Đầu ra | 2 ngày | Chưa có | Trung bình | Yêu cầu HTTPS ở Giai đoạn 2 |
| **B7** | Báo cáo + Mở ca + Quầy/POS | Đầu ra | 3 ngày | Có một phần | Cao | Thiếu entity `Counter` và `PosDevice` |
| **B8** | Kiểm kê kho | Đầu ra | 1 ngày | Đã có phần cơ bản | Cao | Cần bổ sung cờ chặn bán và ngưỡng duyệt |
| **B9** | AI: HĐĐT & Giấy viết tay | Đầu ra | 4,5 ngày | Có một phần (A2 sẵn) | Thấp | Độ chính xác OCR viết tay, chi phí gọi API |
| | **Tổng cộng** | | **17,5 ngày** | | | Chưa tính tunnel B5 (+0,5 ngày) |

**Thứ tự thực thi đề xuất:** B1 → B2 → B7 → B5 → B4 → B8 → B6 → B9.  
**Thứ tự cắt giảm khi thiếu thời gian:** B9b → B6 (giai đoạn 2) → B8.

---

## 2. Phân tích chi tiết từng tính năng theo thứ tự thực thi (Kèm Migration, File, Test, Tin cậy & PR)

### 2.1. B1 — Nhập kho (Đầu vào) · 1,5 ngày
- **Hiện trạng:** Có một phần (`StockInService.cs`). Chưa phân định theo kho (`WarehouseId`).
- **Migration & Xử lý dữ liệu cũ:** Thêm cột `WarehouseId` (nullable trong migration đầu, sau đó backfill gán vào kho mặc định `IsDefault = true` cho các bản ghi `StockIn` cũ, trước khi set NOT NULL nếu cần).
- **File dự kiến sửa/tạo:**
  - `pos-api/src/PosApi/Domain/Entities.cs`
  - `pos-api/src/PosApi/Services/StockInService.cs`
  - `pos-api/src/PosApi/Controllers/StockInsController.cs`
- **Kế hoạch test:** Unit test xác nhận chốt phiếu nhập tăng tồn đúng kho cụ thể; integration test kiểm tra transaction rollback khi lỗi sản phẩm.
- **Mức tin cậy effort:** Cao (1,5 ngày).
- **Đề xuất PR:** PR-B1-01 (Entity & Migration), PR-B1-02 (Service & Controller logic). Merge trước tiên.

---

### 2.2. B2 — Ghi nợ nhà cung cấp (Đầu vào) · 1 ngày
- **Hiện trạng:** Có một phần (`SupplierDebtService.cs`). Chưa tự động sinh nợ từ phiếu nhập.
- **Migration & Xử lý dữ liệu cũ:** Không đổi schema lớn, tận dụng bảng `SupplierDebts` đã có. Dữ liệu cũ không có khoản nợ tự sinh sẽ được khởi tạo qua script migration batch nếu cần.
- **File dự kiến sửa/tạo:**
  - `pos-api/src/PosApi/Services/StockInService.cs` (tích hợp tạo nợ khi nhập chưa trả đủ)
  - `pos-api/src/PosApi/Services/SupplierDebtService.cs`
  - `pos-api/src/PosApi/Controllers/SupplierDebtsController.cs`
- **Kế hoạch test:** Test tạo phiếu nhập nợ → kiểm tra dư nợ NCC tăng đúng `DueDate`; test phiếu chi/trả NCC giảm nợ chính xác.
- **Mức tin cậy effort:** Cao (1 ngày).
- **Đề xuất PR:** PR-B2-01 (Tự động sinh nợ từ StockIn & liên kết trả nợ). Merge sau B1.

---

### 2.3. B3 — Rà soát kho đầu ra (Gộp trong lỗi V1–V3) · 0 ngày riêng
- **Hiện trạng:** Đã hoàn thành phần lớn qua `ReturnService.cs` và `OrderService.cs`.
- **Migration & Xử lý dữ liệu cũ:** Không đổi schema.
- **Kế hoạch test:** Regression test giữ nguyên bất biến `tồn kho = tổng StockLedger`.

---

### 2.4. B4 — Thao tác bàn phím (Đầu ra) · 1,5 ngày
- **Hiện trạng:** Chưa có lớp xử lý phím tắt trên giao diện.
- **Migration & Xử lý dữ liệu cũ:** Không có migration DB.
- **File dự kiến sửa/tạo:** Frontend POS keydown event handler & shortcut config file.
- **Kế hoạch test:** Kiểm tra trên Chrome/Edge với bộ gõ tiếng Việt (Unikey/EVKey) dùng `event.code`.
- **Mức tin cậy effort:** Trung bình (1,5 ngày).
- **Đề xuất PR:** PR-B4-01 (Keyboard shortcut listener & focus management).

---

### 2.5. B5 — Tích hợp SePay (Đầu ra) · 3 ngày (+0,5 ngày nếu dựng tunnel)
- **Hiện trạng:** Chưa tích hợp SePay.
- **Migration & Xử lý dữ liệu cũ:** Tạo bảng mới `BankTransactions`. Không có dữ liệu cũ cần backfill.
- **File dự kiến sửa/tạo:**
  - `pos-api/src/PosApi/Domain/Entities.cs`
  - `pos-api/src/PosApi/Services/SepayPaymentService.cs` (mới)
  - `pos-api/src/PosApi/Controllers/PaymentsController.cs`
- **Kế hoạch test:** Test payload mẫu SePay, test chống trùng `SepayId`, test xử lý lệch tiền (chưa khớp).
- **Mức tin cậy effort:** Trung bình (3 ngày).
- **Đề xuất PR:** PR-B5-01 (Entity & Polling service), PR-B5-02 (Webhook endpoint & SignalR notification).

---

### 2.6. B6 — Điện thoại làm máy quét mã vạch (Đầu ra) · 2 ngày
- **Hiện trạng:** Chưa có tính năng quét qua camera mobile.
- **Migration & Xử lý dữ liệu cũ:** Không có migration DB.
- **File dự kiến sửa/tạo:** Trang web mobile `/scan`, SignalR pairing hub integration.
- **Kế hoạch test:** Quét 20 mã vạch liên tục qua camera mobile gửi về đúng phiên POS qua SignalR.
- **Mức tin cậy effort:** Trung bình (2 ngày).
- **Đề xuất PR:** PR-B6-01 (Mobile scan page & SignalR stream).

---

### 2.7. B7 — Báo cáo + Mở ca + Quầy/máy POS (Đầu ra) · 3 ngày
- **Hiện trạng:** `ShiftService.cs` có quản lý ca cơ bản, thiếu `Counter`, `PosDevice` và gắn `CounterId`.
- **Migration & Xử lý dữ liệu cũ:** Tạo bảng `Counters`, `PosDevices`. Thêm `CounterId` (nullable) vào bảng `Shifts`. Dữ liệu ca cũ sẽ được migration gán vào một Quầy mặc định (`Counter-Default`) được tạo tự động trong migration.
- **File dự kiến sửa/tạo:**
  - `pos-api/src/PosApi/Domain/Entities.cs`
  - `pos-api/src/PosApi/Services/ShiftService.cs`
  - `pos-api/src/PosApi/Controllers/ShiftsController.cs`, `CountersController.cs`
- **Kế hoạch test:** Test mở ca theo quầy, chặn bán khi chưa mở ca, chốt ca đối soát tiền mặt/chuyển khoản.
- **Mức tin cậy effort:** Cao (3 ngày).
- **Đề xuất PR:** PR-B7-01 (Counter & PosDevice entities & APIs), PR-B7-02 (Shift integration & counter-based restriction).

---

### 2.8. B8 — Kiểm kê kho (Đầu ra) · 1 ngày
- **Hiện trạng:** `StockTakeService.cs` đã có sẵn tính năng cơ bản.
- **Migration & Xử lý dữ liệu cũ:** Bổ sung cấu hình chặn bán và phân quyền duyệt (nếu cần thêm cột vào StoreSetting hoặc StockTake).
- **File dự kiến sửa/tạo:**
  - `pos-api/src/PosApi/Services/StockTakeService.cs`
- **Kế hoạch test:** Test tạo phiếu, đếm thực tế, duyệt chênh lệch theo ngưỡng giá trị tiền.
- **Mức tin cậy effort:** Cao (1 ngày).
- **Đề xuất PR:** PR-B8-01 (Approval threshold & block-sale config logic).

---

### 2.9. B9 — AI: Hóa đơn điện tử và giấy viết tay (Đầu ra) · 4,5 ngày
- **Hiện trạng:** Đã có sẵn A2 router (`ai-service/app/routers/invoices.py`).
- **Migration & Xử lý dữ liệu cũ:** Không đổi schema POS ngoài các trường `SupplierInvoice` sẵn có.
- **File dự kiến sửa/tạo:** Mở rộng Python FastAPI service (`ai-service`) và C# ingestion client.
- **Kế hoạch test:** Test file XML hóa đơn điện tử và ảnh viết tay qua pipeline AI sinh phiếu nhập nháp.
- **Mức tin cậy effort:** Thấp (do phụ thuộc vào độ chính xác của mô hình thị giác ngoài).
- **Đề xuất PR:** PR-B9-01 (HĐĐT XML parser), PR-B9-02 (Giấy viết tay OCR & Review UI).

---

## 3. Giải pháp xử lý rủi ro đồng thời đa quầy (Concurrency)
- **Vấn đề:** Khi nhiều quầy POS cùng bán hàng hoặc cập nhật kho, có thể xảy ra tranh chấp dữ liệu (race condition) trên tồn kho sản phẩm hoặc phiên làm việc.
- **Giải pháp kỹ thuật:**
  1. Sử dụng **Row-level locking (`FOR UPDATE`)** trong các transaction EF Core khi truy vấn sản phẩm và kho (`WarehouseStock`), như đã áp dụng tại `StockInService.cs:116` và `ReturnService.cs:118`.
  2. Áp dụng **Optimistic Concurrency (`RowVersion` / Timestamp)** đối với các entity có nguy cơ xung đột cao.
  3. Viết **Concurrent Integration Tests** (nhiều Task chạy song song cùng trừ tồn kho một sản phẩm hoặc chốt ca cùng lúc) để kiểm chứng tính toàn vẹn dữ liệu.

---

## 4. Tính lại Capacity và Lộ trình đến 03/12
- **Thời gian lịch còn lại:** Từ 12/11/2026 đến 03/12/2026 là **21 ngày lịch**.
- **Tổng effort còn lại:**
  - Nhóm 8 (B1–B9): **17,5 ngày**
  - Phần chưa xong của nhóm 1, 2, 6 (Reports nâng cao, 8 mục NCC hoàn thiện): **4 ngày**
  - AI routers (A1, A3, A4): **4 ngày**
  - PR sửa lỗi phát sinh V1–V4: **2 ngày**
  - **Tổng cộng effort:** **27,5 ngày** so với **21 ngày lịch** (tỷ lệ tải ~1.3x).
- **Chiến lược xử lý:**
  - Máy chạy 24/7 liên tục không gián đoạn.
  - Áp dụng nguyên tắc **trễ theo thứ tự ưu tiên** khi thiếu thời gian: Cắt giảm/dời lịch **B9b** (Giấy viết tay) → **B6 Giai đoạn 2** (Quét mã mobile) → **B8** (Kiểm kê nâng cao).

---

## 5. Đề xuất thứ tự Merge PR
1. `PR-B1-01` & `PR-B1-02` (Nhập kho phân theo kho)
2. `PR-B2-01` (Ghi nợ nhà cung cấp tự động)
3. `PR-B7-01` & `PR-B7-02` (Quầy, máy POS và Mở ca)
4. `PR-B5-01` & `PR-B5-02` (Tích hợp SePay polling & webhook)
5. `PR-B4-01` (Thao tác bàn phím POS)
6. `PR-B8-01` (Kiểm kê kho & ngưỡng duyệt)
7. `PR-B6-01` (Quét mã qua điện thoại)
8. `PR-B9-01` & `PR-B9-02` (AI HĐĐT & Giấy viết tay)
