# DigiSME POS

Ngôn ngữ chuẩn của ngữ cảnh POS (bán lẻ + FnB) trong DigiSME — để M1/M2/M3 nói cùng một thứ khi bàn thiết kế. Chỉ khái niệm nghiệp vụ, không chi tiết triển khai.

## Language

### Nhận diện & vai trò

**User**:
Nhân viên đăng nhập POS (thu ngân, quản lý). Khác với `Customer` (người mua).
_Avoid_: tài khoản, nhân viên (nói chung)

**Role**:
Vai của `User`: `Cashier` (thu ngân), `Manager` (quản lý).
Ghi chú: khác với danh mục **Chức vụ** của tmSales (Giám đốc, Kế toán, Thu ngân, Thủ kho…); quan hệ giữa Role (DigiSME) và Chức vụ (tmSales) **chưa chốt**.
_Avoid_: quyền, permission

**Customer**:
Người mua hàng; có thể mang `Debt` và `LoyaltyAccount`.
_Avoid_: đối tác, khách, partner, tài khoản

### Danh mục

**Product**:
Mặt hàng bán ra, thuộc một `Category`.
_Avoid_: mặt hàng, hàng, item

**Category**:
Nhóm hàng phân cấp cha–con của `Product`.
_Avoid_: danh mục, nhóm (nói chung)

**Unit**:
Đơn vị tính của `Product` (hộp, vỉ, kg…).
_Avoid_: ĐVT, đơn vị (nói chung)

**Surcharge**:
Phí phụ thu tính thêm trên `Order` (gói hàng, trang trí, dịch vụ).
_Avoid_: phí, phụ phí

### Bán lẻ (POS)

**Cart**:
Giỏ hàng đang bán tại quầy, chưa thành `Order`.
_Avoid_: giỏ, basket, đơn tạm

**HeldCart**:
Giỏ đã "giữ đơn" (đang bán, chưa chốt).
_Avoid_: đơn giữ, đơn chờ

**Order**:
Một lần bán tại quầy, gồm các `OrderItem` và tổng tiền.
_Avoid_: giao dịch, hoá đơn, sale

**OrderItem**:
Một dòng hàng trong `Order`.
_Avoid_: mặt hàng đơn, dòng đơn

**Payment**:
Một lượt thu tiền của `Order` (tiền mặt, VietQR…).
_Avoid_: thanh toán (nói chung), phiếu thu

**ReturnRecord**:
Phiếu trả hàng gắn với `Order` gốc.
_Avoid_: đơn trả, phiếu hoàn

### Công nợ & điểm

**Debt**:
Khối công nợ chưa thu của `Customer`.
_Avoid_: nợ, công nợ (nói chung)

**ThuNợ**:
Một lần thu tiền làm giảm `Debt` (toàn bộ hoặc một phần) — `Open → PartiallyPaid / Settled`; khác `Payment`: `Payment` thu tiền cho `Order`, `ThuNợ` thu tiền cho `Debt`.
_Avoid_: trả nợ (nói chung), phiếu thu nợ, thu công nợ

**LoyaltyAccount**:
Tài khoản điểm tích lũy của `Customer`.
_Avoid_: điểm, tài khoản khách

### Kho

**StockIn**:
Phiếu nhập hàng vào kho.
_Avoid_: nhập kho (nói chung), đơn nhập

**StockLedgerEntry**:
Dòng sổ kho theo từng chứng từ (nhập, xuất, tồn cuối).
_Avoid_: thẻ kho, dòng tồn

**Supplier**:
Nhà cung cấp; entity riêng, tách khỏi `Customer`.
Ghi chú: được gọi tắt là **NCC**.

**SupplierInvoice**:
Hoá đơn nhà cung cấp đi kèm `StockIn`.
_Avoid_: đối tác, khách

### Vận hành & kiểm toán

**Shift**:
Phiên làm việc (ca) của nhân viên tại máy POS.
_Avoid_: ca, phiên (nói chung)

**PosLog**:
Nhật ký hành động nhạy cảm (hủy giỏ đang bán…): thời gian, nhân viên, tổng tiền.
_Avoid_: log, nhật ký (nói chung)
