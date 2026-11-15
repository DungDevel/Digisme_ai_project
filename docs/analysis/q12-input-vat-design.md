# Tài liệu Thiết kế: Kê khai thuế GTGT đầu vào cho phiếu nhập thủ công (Q12)

> **Trạng thái:** Đã thiết kế kỹ thuật, chờ kế toán xác nhận trước khi triển khai code.  
> **Căn cứ pháp lý:** Luật Thuế GTGT số 48/2024/QH15 và Nghị định 181/2025/NĐ-CP.  
> **Phụ thuộc:** Mô đun B2 (Thanh toán, chứng từ thu/chi ngân hàng, phương thức thanh toán không dùng tiền mặt). Dự kiến triển khai sau B2 (Gói B1b, ước lượng 1 ngày).

---

## 1. Phạm vi nghiệp vụ
- Áp dụng cho các phiếu nhập kho thủ công (`StockIn`) không liên kết với hóa đơn điện tử OCR từ nhà cung cấp (`SupplierInvoiceId = null`).
- Tính năng kê khai thuế mua vào (`GetTaxDeclarationAsync`) chỉ áp dụng tự động cộng dồn vào tổng khấu trừ khi cửa hàng áp dụng **phương pháp tính thuế khấu trừ** (được cấu hình qua `StoreSetting.StoreType`). Nếu cửa hàng dùng phương pháp trực tiếp, báo cáo thuế đầu vào chỉ hiển thị dạng thông tin tham khảo, không khấu trừ.

## 2. Thay đổi Cấu trúc Dữ liệu (Database Schema)
Bổ sung các cột nullable vào bảng `StockIns`:
- `VatInvoiceNo` (varchar(64), null): Số hóa đơn GTGT.
- `VatInvoiceSerial` (varchar(32), null): Ký hiệu hóa đơn / mẫu số.
- `VatInvoiceDate` (timestamp, null): Ngày phát hành hóa đơn.
- `SellerTaxCode` (varchar(32), null): Mã số thuế người bán.
- `VatRate` (decimal(5,2), null): Tỷ lệ thuế suất GTGT (ví dụ 0, 5, 10).
- `VatAmount` (decimal(18,4), null): Tiền thuế GTGT đầu vào.

## 3. Phân loại 3 nhóm trong `GetTaxDeclarationAsync`
Khi tổng hợp báo cáo thuế đầu vào cho các phiếu nhập thủ công, hệ thống phân chia thành 3 nhóm rõ rệt:
1. **Nhóm A — Đủ điều kiện khấu trừ:**
   - Trạng thái: `Confirmed`, có đầy đủ thông tin hóa đơn hợp lệ (hoặc `SupplierInvoice` trạng thái `Confirmed`), không vi phạm điều kiện thanh toán.
   - Xử lý: Cộng dồn vào `TotalInputVat` và `TotalPurchaseAmount`.
2. **Nhóm B — Chờ kế toán duyệt (Pending Review):**
   - Điều kiện: Giá trị giao dịch từ 5.000.000 VNĐ trở lên (gồm VAT) nhưng chưa có chứng từ thanh toán không dùng tiền mặt liên kết (đối chiếu với dữ liệu ngân hàng / `CashVouchers` từ mô đun B2).
   - Xử lý: Liệt kê riêng trong báo cáo phụ, **không** cộng vào tổng khấu trừ thuế đầu vào cho đến khi kế toán xác nhận chứng từ thanh toán qua ngân hàng.
3. **Nhóm C — Thiếu hóa đơn / Không hợp lệ:**
   - Điều kiện: Phiếu nhập thủ công không có số hóa đơn GTGT hoặc thiếu thông tin bắt buộc.
   - Xử lý: Liệt kê cảnh báo để nhân viên/kế toán bổ sung chứng từ.
   - Các phiếu đã hủy (`Cancelled`) bị loại bỏ hoàn toàn.

## 4. Các câu hỏi cần Kế toán doanh nghiệp xác nhận (Q12)
1. Kỳ kê khai thuế GTGT đầu vào được tính theo **ngày phát hành hóa đơn** (`VatInvoiceDate`) hay **ngày nhập kho** (`StockIn.CreatedAt`)?
2. Doanh nghiệp hiện đang áp dụng phương pháp kê khai thuế GTGT nào (**Khấu trừ** hay **Trực tiếp** trên doanh thu)?
3. Có cần cấu hình hạn mức ngoại lệ hoặc quy tắc kiểm tra chứng từ thanh toán không dùng tiền mặt khác ngoài ngưỡng 5 triệu VNĐ theo quy định hiện hành không?
