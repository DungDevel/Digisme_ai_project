# Báo cáo Review Tính năng B1 — Nhập kho theo kho (Backend)

> **Ngày review:** 13/11/2026  
> **Nhánh thực hiện:** `feature/b1-stockin-warehouse`  
> **Phạm vi:** Kiểm tra, đánh giá toàn diện mã nguồn, migration, unit test và rủi ro nghiệp vụ của B1 theo tài liệu điều chỉnh 11/11.

---

## 1. Kết quả Kiểm thử (Test)
- **Lệnh thực thi:** `dotnet test pos-api/tests/PosApi.Tests/PosApi.Tests.csproj`
- **Kết quả:** **147 passed, 0 failed, 0 skipped** (tăng 1 test so với mốc 146 trước đó).
- **Danh sách test mới bổ sung cho B1 (`StockInServiceTests.cs`):**
  1. `ConfirmStockIn_Success_UpdatesStockAndCreatesRecords` (`StockInServiceTests.cs:18`)
  2. `ConfirmStockIn_Idempotency_ReturnsSameResult` (`StockInServiceTests.cs:87`)
  3. `UploadAndGetInvoice_Success` (`StockInServiceTests.cs:126`)
  4. `RejectInvoice_Success` (`StockInServiceTests.cs:155`)
  5. `ConfirmStockIn_WithValidSupplierId_Success` (`StockInServiceTests.cs:174`)
  6. `ConfirmStockIn_WithInvalidSupplierId_ThrowsKeyNotFoundException` (`StockInServiceTests.cs:213`)
  7. `ManualStockIn_DraftUpdateConfirmCancel_WorkflowSuccess` (`StockInServiceTests.cs:244`)

- **Đối chiếu 7 nhóm test bắt buộc:**
  - (a) Chốt tăng đúng `WarehouseStock` của kho được chọn, hai kho không lẫn: **Chưa có test case riêng biệt kiểm tra song song 2 kho khác nhau.**
  - (b) Rollback toàn bộ khi lỗi giữa chừng: **Chưa có test tường minh về transaction rollback khi lỗi giữa chừng** (ngoại trừ cơ chế transaction disposal của `DatabaseFixture`).
  - (c) Hủy tạo bút toán đảo, tồn trở về như trước: **Đã có** (`ManualStockIn_DraftUpdateConfirmCancel_WorkflowSuccess`, `StockInServiceTests.cs:294`).
  - (d) Hủy bị chặn khi tồn sẽ âm: **Chưa có test case cụ thể kiểm tra ngoại lệ chặn hủy khi tồn kho không đủ** (`StockInService.cs:611`).
  - (e) Luồng OCR cũ vẫn chạy: **Đã có** (`ConfirmStockIn_Success_UpdatesStockAndCreatesRecords`, `UploadAndGetInvoice_Success`).
  - (f) Backfill migration đúng: **Chưa có unit/integration test tự động kiểm tra script migration backfill**.
  - (g) Chốt song song hai lần cùng một sản phẩm không mất cập nhật: **Chưa có test concurrency** cho chốt song song (mặc dù code có dùng `FOR UPDATE`).

---

## 2. Đánh giá Phạm vi (Scope)
- **Endpoints mới & cải tiến (`StockInsController.cs`):**
  - `GET /api/v1/stock-ins` (Mới hỗ trợ filter `warehouseId`, `supplierId`, `status`).
  - `GET /api/v1/stock-ins/{id}` (Cải tiến tra cứu linh hoạt giữa `StockIn` và `SupplierInvoice`).
  - `POST /api/v1/stock-ins` (Upload hóa đơn OCR - giữ nguyên).
  - `POST /api/v1/stock-ins/manual` (Mới - Tạo phiếu nhập kho nháp thủ công).
  - `PUT /api/v1/stock-ins/{id}` (Mới - Sửa phiếu nhập nháp).
  - `PUT /api/v1/stock-ins/{id}/items` (Cập nhật items hóa đơn pending - đã có từ trước).
  - `POST /api/v1/stock-ins/{id}/confirm` (Cải tiến - hỗ trợ chốt Draft StockIn hoặc OCR SupplierInvoice kèm `X-Idempotency-Key` và khóa `FOR UPDATE`).
  - `POST /api/v1/stock-ins/{id}/cancel` (Mới - Hủy phiếu nhập đã chốt, tạo bút toán đảo).
  - `DELETE /api/v1/stock-ins/{id}` (Xóa phiếu nháp hoặc từ chối hóa đơn).
- **DTOs mới (`Dtos.cs`):** `StockInCreateRequest`, `StockInUpdateRequest`, `StockInItemResponse`, `StockInResponse`, `PagedStockInResponse`.
- **Cột mới trong cơ sở dữ liệu (`StockIns`):** `WarehouseId` (int, bắt buộc), `Status` (enum `StockInStatus`: Draft=0, Confirmed=1, Cancelled=2), `CreatedBy` (int).
- **Idempotency-Key:** Lưu tại bảng `IdempotencyRecords` (`StockInService.cs:466`), thời hạn hết hạn 24 giờ (`ExpiresAt = DateTime.UtcNow.AddHours(24)`), lưu hash request (`RequestHash`) để kiểm tra xung đột nội dung.
- **CreatedBy:** Dùng để ghi nhận ID người dùng tạo phiếu hoặc xác nhận nhập kho (`StockInService.cs:177, 343`).
- **Phạm vi kiểm soát:** Không phát hiện mã nguồn thuộc B2 (ghi nợ nhà cung cấp tự động) trong PR này. Phạm vi B1 được giữ đúng mục tiêu cốt lõi.

---

## 3. Bất biến Tồn kho (Stock Invariant)
- **Mối quan hệ:** Theo thiết kế hệ thống, `Products.CurrentStock = SUM(WarehouseStock.Stock)`. 
- **Rủi ro dữ liệu cũ:** Các lô nhập kho trước khi áp dụng `WarehouseId` chưa được phân bổ vào `WarehouseStocks`. Khi thực hiện nghiệp vụ chốt hoặc hủy phiếu mới, code thực hiện khóa `FOR UPDATE` trên cả `Products` và `WarehouseStocks` (`StockInService.cs:290-305`). Nếu dữ liệu `WarehouseStock` cũ chưa khớp tổng tồn, việc hủy phiếu nhập có thể bị chặn sai hoặc làm lệch tồn kho theo kho.

---

## 4. Xử lý Phiếu cũ (Old Receipts)
- Khi hủy một `StockIn` cũ đã được backfill vào kho mặc định (`IsDefault = true`) qua migration, nhưng bản ghi `WarehouseStock` tương ứng chưa từng được cộng khi nhập (vì dữ liệu cũ chỉ cập nhật `Products.CurrentStock`), hàm `CancelStockInAsync` sẽ đọc `whStock?.Stock` bằng 0 hoặc nhỏ hơn lượng cần trừ, dẫn đến việc ném ngoại lệ `BusinessRuleException` chặn hủy (`StockInService.cs:611`).
- **Hiện trạng:** Chưa có unit test kiểm tra luồng hủy phiếu cũ đã backfill.

---

## 5. Tác động Thuế (Tax)
- Hàm `ReportService.GetTaxDeclarationAsync` (`ReportService.cs:129`) tính tổng giá trị mua vào bằng tổng `StockIns.Total` trong khoảng thời gian và tính thuế đầu vào `= totalPurchaseAmount * 0.1m`.
- **Rủi ro:** Cả phiếu nhập thủ công (không có hóa đơn VAT hợp lệ / `SupplierInvoiceId = null`) và phiếu OCR đều được gom chung vào báo cáo thuế mua vào. Điều này có thể gây sai lệch pháp lý nếu phiếu thủ công nội bộ chưa có hóa đơn tài chính hợp pháp.

---

## 6. Quy tắc Giá vốn (Cost Price)
- Quy tắc giá vốn chưa có sự thay đổi hay tính toán lại phương pháp bình quân gia quyền/FIFO trong `StockInService`. Các mục nhập ghi nhận `UnitCost` nhưng chưa cập nhật tự động vào `Product.CostPrice`.

---

## 7. Ranh giới Thực thể & Trạng thái (Entity Boundaries)
- Endpoint `GET /api/v1/stock-ins/{id}` dùng cơ chế fallback từ `StockIn` sang `SupplierInvoice` (`StockInsController.cs:43-48`). 
- `StockInStatus` (Draft, Confirmed, Cancelled) và `SupplierInvoiceStatus` (PendingReview, Confirmed, Rejected) là hai enum riêng biệt (`Entities.cs:20, 22`), tuy nhiên việc dùng chung không gian ID tra cứu qua một endpoint có thể gây khó khăn cho việc định hướng client.

---

## 8. Migration & Backfill
- **Migration:** `20261112162008_AddStockInWarehouseAndStatus.cs`
- **Script Backfill:** Cập nhật `WarehouseId` sang kho mặc định (`IsDefault = true`) hoặc kho đầu tiên. 
- **Đánh giá:** Chạy an toàn trên DB có sẵn dữ liệu. Trên DB trống, lệnh UPDATE không ảnh hưởng và chuyển sang AlterColumn thành NOT NULL thành công. Có phương án `Down()` đầy đủ để rollback.

---

## 9. Báo cáo Bổ sung
- **Tên branch:** `feature/b1-stockin-warehouse`
- **Danh sách commit gần đây:** `3c2bcfb`, `1dddc95`, `6303909`, `3427cad`, `1793937`, `b940a92`, `c712ec7`, `503dc18`, `5cbb264`, `39ca577`.
- **Câu hỏi mở phát sinh:** Đã bổ sung các câu **Q9, Q10, Q11** vào file `docs/analysis/group8-open-questions.md`.

---

## 10. Kết luận & Kiến nghị
- **Kết quả test:** 147 passed, 0 failed.
- **Thiếu sót test:** Cần bổ sung test cho đa kho, rollback transaction, chặn hủy khi âm kho và backfill migration.
- **Rủi ro nghiệp vụ:** Xử lý hủy phiếu cũ chưa có lịch sử `WarehouseStock`, và việc gom phiếu nhập thủ công vào báo cáo thuế GTGT.
- **Kết luận:** **B1 CẦN SỬA (Cần bổ sung test case, tách bạch báo cáo thuế và xử lý an toàn cho phiếu cũ trước khi merge).**

### Danh sách việc cần sửa (Action Items - Đã hoàn thành):
1. Bổ sung unit tests cho đủ 7 nhóm test + test chặn hủy phiếu cũ (153/153 test pass).
2. Lọc rõ chỉ các `StockIn` có liên kết hóa đơn VAT đã xác nhận mới đưa vào `GetTaxDeclarationAsync`.
3. Bổ sung cờ `IsBackfilled` và migration `AddIsBackfilledToStockIns` để chặn hủy an toàn đối với các phiếu nhập lịch sử cũ.

---

## 11. Bổ sung sau sửa (Post-fix Review)

1. **Bất biến `Products.CurrentStock = tổng WarehouseStock`:**
   - Trên dữ liệu seed/test mới, hệ thống duy trì đồng bộ qua `WarehouseStock` updates (`StockInService.cs:370-386`, `ReturnService.cs:284-304`). Tuy nhiên, trên dữ liệu lịch sử trước B1, các giao dịch cũ chỉ cập nhật `Products.CurrentStock` mà chưa phân bổ vào `WarehouseStocks`. Điều này có thể dẫn đến lệch tạm thời khi thao tác hủy phiếu nhập cũ hoặc xuất kho ở các kho không có dữ liệu `WarehouseStock`. Đã chặn hủy phiếu cũ có cờ `IsBackfilled = true` để tránh rủi ro âm kho.

2. **Quy tắc giá vốn:**
   - Quy tắc giá vốn **không thay đổi** so với trước B1. Hệ thống tiếp tục sử dụng giá nhập `UnitCost` ghi nhận tại `StockInItem.UnitCost` (`StockInService.cs:362`), chưa tự động tính lại bình quân gia quyền vào `Product.CostPrice`.

3. **Idempotency-Key:**
   - Lưu tại bảng `IdempotencyRecords` (`StockInService.cs:466`).
   - Thời hạn hết hạn: 24 giờ (`ExpiresAt = DateTime.UtcNow.AddHours(24)`, `StockInService.cs:496`).
   - Phạm vi sử dụng: Endpoint xác nhận nhập kho (`POST /api/v1/stock-ins/{id}/confirm`, `StockInService.cs:457`).
   - Đánh giá: Nên giữ lại để ngăn chặn việc gửi trùng lặp yêu cầu chốt phiếu khi mạng chậm hoặc client retry.

4. **Danh sách endpoint chính xác của `StockInsController`:**
   - `[HttpGet] GET /api/v1/stock-ins` (Mới - Lấy danh sách phiếu nhập hỗ trợ filter `warehouseId`, `supplierId`, `status`, `StockInsController.cs:24`)
   - `[HttpGet("{id:int}")] GET /api/v1/stock-ins/{id}` (Cải tiến - Tra cứu fallback `StockIn` và `SupplierInvoice`, `StockInsController.cs:37`)
   - `[HttpPost] POST /api/v1/stock-ins` (Upload ảnh hóa đơn OCR, `StockInsController.cs:58`)
   - `[HttpPost("manual")] POST /api/v1/stock-ins/manual` (Mới - Tạo phiếu nhập nháp thủ công, `StockInsController.cs:88`)
   - `[HttpPut("{id:int}")] PUT /api/v1/stock-ins/{id}` (Mới - Sửa phiếu nhập nháp, `StockInsController.cs:111`)
   - `[HttpPut("{id:int}/items")] PUT /api/v1/stock-ins/{id}/items` (Cập nhật items hóa đơn pending - đã có từ trước, `StockInsController.cs:134`)
   - `[HttpPost("{id:int}/confirm")] POST /api/v1/stock-ins/{id}/confirm` (Cải tiến - Xác nhận nhập kho cho Draft StockIn hoặc OCR SupplierInvoice kèm `Idempotency-Key` và khóa `FOR UPDATE`, `StockInsController.cs:152`)
   - `[HttpPost("{id:int}/cancel")] POST /api/v1/stock-ins/{id}/cancel` (Mới - Hủy phiếu nhập đã chốt, tạo bút toán đảo và chặn phiếu cũ `IsBackfilled`, `StockInsController.cs:198`)
   - `[HttpDelete("{id:int}")] DELETE /api/v1/stock-ins/{id}` (Xóa phiếu nhập nháp hoặc từ chối hóa đơn OCR, `StockInsController.cs:220`)
   - Toàn bộ controller yêu cầu phân quyền `[Authorize(Policy = "ManagerOnly")]` (`StockInsController.cs:14`).
