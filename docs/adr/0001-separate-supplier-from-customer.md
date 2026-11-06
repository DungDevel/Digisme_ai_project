# Tách Supplier khỏi Customer

Trong tmSales, NCC và khách gộp chung thành "đối tác" (nhóm đối tác + cờ "Là NCC"). DigiSME không đi theo mô hình gộp đó: `Supplier` là entity riêng, tách khỏi `Customer`, và là việc bắt buộc của Đợt 1. Lý do: NCC và khách có vòng đời dữ liệu khác nhau — NCC gắn với nhập kho (`StockIn`/`SupplierInvoice`), khách gắn với bán lẻ (`Debt`/`LoyaltyAccount`); gộp chung sẽ pha loãng hai mô hình và ép hai luồng không liên quan vào một bảng.

Status: accepted — đã quyết, **chưa implement** (việc Đợt 1). Hiện code mới có `StockIn.SupplierName` (string).
