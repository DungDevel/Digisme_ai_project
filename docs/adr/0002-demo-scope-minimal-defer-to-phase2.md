# Phạm vi demo: đơn kho, 2 vai, VAT 1 mức, giá Lẻ/Member — hoãn chiều sâu sang Đợt 2

Giữ đúng mức tối giản của code hiện tại cho bản demo; 4 chiều sâu dưới là Đợt 2 trong roadmap và **hoãn sang Đợt 2**, không thiết kế schema chi tiết bây giờ:

- **Đơn kho** — chưa có `WarehouseId`/`WarehouseStock` đa kho.
- **2 vai cố định** `Cashier`/`Manager` — chưa phân quyền chi tiết `Role→Permission`.
- **VAT 1 mức** toàn store — chưa nhóm thuế theo sản phẩm.
- **Giá chọn 2/4 cột** (Lẻ + Member) — chưa bảng giá theo nhóm khách.

Lý do: demo chỉ cần chạy được 9 nhóm Đợt 1; 4 chiều sâu này đều là Đợt 2, thiết kế schema chi tiết lúc này là chi phí thiết kế cho phạm vi không demo. ADR này chỉ ghi ranh giới + lý do hoãn, không thiết kế schema.

Status: accepted.

## Hệ quả

Nếu làm Đợt 2, cần các migration sau:

- `WarehouseId` vào sổ kho/tồn kho (`StockLedgerEntry` + tồn kho theo kho).
- Bảng `Role`–`Permission` cho phân quyền chi tiết.
- `TaxGroup` (nhóm thuế) theo sản phẩm.
- Giá theo nhóm khách (mở thêm 2 cột giá còn lại / bảng giá theo nhóm).
