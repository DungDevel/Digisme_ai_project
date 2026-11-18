# Danh sách câu hỏi mở (Nhóm 8 — B1–B9)

Tài liệu này tổng hợp các câu hỏi mở cần chủ đầu tư / người quản lý quyết định trước mốc feature freeze 03/12/2026. Các câu Q2–Q5 đã được chốt phương án mặc định và chờ xác nhận cuối cùng.

---

### Q1: Nguồn chứng từ nhập kho duy nhất (B1)
- **Vấn đề:** Hiện tại hệ thống dùng `SupplierInvoice` (hóa đơn OCR của nhà cung cấp) để xác nhận nhập kho (`StockInService`). Liệu có cần tách hẳn một entity phiếu nhập kho thủ công độc lập (`StockReceipt`) không phụ thuộc OCR hay giữ `StockIn` liên kết linh hoạt với `SupplierInvoice`?
- **Phương án đề xuất:** Giữ `StockIn` làm chứng từ nhập kho chung (có thể tạo thủ công hoặc sinh từ OCR của hóa đơn nhà cung cấp), cho phép nhập kho không bắt buộc phải có `SupplierInvoiceId` (đã hỗ trợ nullable qua P0-5 migration).

### Q2: Cơ chế chống trùng SePay và xử lý giao dịch lệch tiền (B5) — *Đã chốt, chờ xác nhận*
- **Vấn đề:** Khi khách chuyển khoản qua VietQR động nhưng chuyển thiếu hoặc thừa số tiền của đơn hàng, hệ thống nên xử lý tự động hay đưa vào hàng đợi "Chưa khớp" để thu ngân/kế toán xử lý thủ công?
- **Phương án đề xuất:** Đưa các giao dịch thiếu/thừa tiền vào bảng `BankTransactions` trạng thái "Chưa khớp" (`Pending`/`Mismatch`). Giao dịch đủ tiền tự động đổi trạng thái đơn sang `Paid` và đóng phiên thanh toán qua SignalR. Thu ngân/kế toán xác nhận thủ công các giao dịch lệch tiền kèm ghi `PosLog`.

### Q3: Cơ chế tunnel cho Webhook SePay khi chạy local (B5) — *Đã chốt, chờ xác nhận*
- **Vấn đề:** Dự án đang chạy local, SePay không gọi được webhook nếu không có domain HTTPS công khai. Có cần đầu tư 0,5 ngày dựng tunnel cố định (như Cloudflare Tunnel / ngrok) hay ưu tiên hoàn toàn polling API SePay trước?
- **Phương án đề xuất:** Ưu tiên polling API SePay làm cơ chế chính (polling định kỳ vài giây/lần), chỉ bật webhook khi môi trường có sẵn HTTPS công khai để tiết kiệm thời gian trước deadline 03/12.

### Q4: Quản lý số lượng Quầy (Counter) và Máy POS (B7) — *Đã chốt, chờ xác nhận*
- **Vấn đề:** Chủ cửa hàng có được tự do cấu hình số lượng quầy và máy POS hay cần giới hạn theo gói đăng ký dịch vụ của khách hàng?
- **Phương án đề xuất:** Cho phép cấu hình linh hoạt qua giao diện Cài đặt (Store Settings / Counter management), mặc định không giới hạn số lượng trong phiên bản nội bộ, chuẩn bị sẵn trường giới hạn để tích hợp licensing sau này.

### Q5: Ngưỡng duyệt chênh lệch kiểm kê kho (B8) — *Đã chốt, chờ xác nhận*
- **Vấn đề:** Mức ngưỡng duyệt chênh lệch kiểm kê kho (Dưới 200.000đ tự động duyệt, 200k–1M do Store Manager duyệt, trên 1M do Owner/Accountant duyệt) đã phù hợp với quy mô cửa hàng chưa?
- **Phương án đề xuất:** Giữ nguyên các mức khởi điểm như đề xuất (200.000đ và 1.000.000đ) nhưng cho phép chủ cửa hàng cấu hình lại ngưỡng này trong phần thiết lập hệ thống.

### Q6: Chính sách bảo mật và lưu trữ ảnh chứng từ AI (B9b)
- **Vấn đề:** Việc gửi ảnh giấy viết tay / chứng từ nhà cung cấp ra dịch vụ AI bên ngoài (FastAPI / Vision model) liên quan đến dữ liệu nhạy cảm của doanh nghiệp. Cần chính sách phê duyệt hay cam kết gì không?
- **Phương án đề xuất:** Cần có thỏa thuận bảo mật dữ liệu với hạ tầng AI sử dụng, hoặc khuyến khích chạy mô hình AI cục bộ (local model) nếu yêu cầu bảo mật tuyệt đối. Trước mắt cho phép lưu trữ ảnh trên Storage nội bộ và gọi service xử lý bất đồng bộ.

---

### Q7: Xử lý dữ liệu cũ cho StockIn khi bổ sung WarehouseId (B1) — *Mới bổ sung*
- **Vấn đề:** Khi thêm cột `WarehouseId` bắt buộc vào `StockIn`, các bản ghi `StockIn` cũ trong database chưa có `WarehouseId` sẽ xử lý ra sao?
- **Phương án đề xuất:** Cho phép `WarehouseId` nullable trong giai đoạn chuyển đổi hoặc tự động gán vào kho mặc định (`IsDefault = true`) của cửa hàng thông qua migration data fix.

### Q8: Xử lý dữ liệu ca làm việc cũ khi gắn CounterId vào Shift (B7) — *Mới bổ sung*
- **Vấn đề:** Khi `Shift` bắt buộc gắn với `CounterId`, các ca làm việc cũ đã mở trước khi có quầy sẽ liên kết thế nào?
- **Phương án đề xuất:** Tạo một Quầy mặc định (`Counter-Default`) trong migration khởi tạo và gán `CounterId` của quầy mặc định này cho tất cả các bản ghi `Shift` cũ chưa có quầy.

### Q9: Bất biến tồn kho khi hủy phiếu nhập cũ (B1)
- **Vấn đề:** Khi hủy một `StockIn` cũ đã được backfill vào kho mặc định qua migration nhưng `WarehouseStock` chưa từng được cộng khi nhập (do dữ liệu cũ chỉ cập nhật `Products.CurrentStock`), thao tác hủy sẽ bị chặn do `currentWhStock < item.Qty`. Cần cơ chế làm sạch dữ liệu cũ hoặc xử lý ngoại lệ cho phiếu nhập cũ trước thời điểm phân kho.
- **Phương án đề xuất:** Bổ sung script migration đồng bộ tồn kho sang kho mặc định cho toàn bộ lịch sử trước khi áp dụng chặt chẽ `WarehouseStock` vào nghiệp vụ hủy phiếu.

### Q10: Chứng từ nhập thủ công không có hóa đơn tài chính trong kê khai thuế mua vào (B1)
- **Vấn đề:** `ReportService.GetTaxDeclarationAsync` tính tổng thuế đầu vào bằng 10% tổng giá trị của toàn bộ `StockIns` trong khoảng thời gian. Các phiếu nhập thủ công nội bộ (không có `SupplierInvoiceId` hoặc hóa đơn GTGT hợp lệ) có bị gom vào kê khai thuế GTGT mua vào gây rủi ro pháp lý không?
- **Phương án đề xuất:** Phân tách rõ `StockIn` có hóa đơn VAT hợp lệ (`SupplierInvoiceId != null` hoặc có cờ `IsTaxable`) mới đưa vào báo cáo thuế đầu vào.

### Q11: Xung đột định danh ID trong endpoint tra cứu `GET /api/v1/stock-ins/{id}` (B1)
- **Vấn đề:** Controller dùng chung một endpoint `GET /api/v1/stock-ins/{id}` để tra cứu `StockIn` trước, nếu không thấy thì fallback sang `SupplierInvoice` (`StockInsController.cs:43-48`). Nếu ID của `StockIn` và `SupplierInvoice` không cùng không gian số hoặc bị trùng bảng, có thể dẫn đến trả nhầm entity.
- **Phương án đề xuất:** Tách biệt rõ route `/api/v1/stock-ins/{id}` và `/api/v1/supplier-invoices/{id}` hoặc đảm bảo phân biệt rõ kiểu trả về.

### Q12: Xác nhận quy tắc kê khai thuế đầu vào cho phiếu nhập thủ công (B1) — *Đã thiết kế, chờ kế toán xác nhận*
- **Vấn đề:** Xác định quy tắc kê khai thuế GTGT đầu vào cho các phiếu nhập thủ công không có hóa đơn OCR (`SupplierInvoiceId = null`).
- **Phương án đề xuất:** Đã hoàn tất tài liệu thiết kế chi tiết tại `docs/analysis/q12-input-vat-design.md` với 3 nhóm phân loại (A: Đủ điều kiện, B: Chờ duyệt chứng từ thanh toán không dùng tiền mặt, C: Thiếu hóa đơn) và các câu hỏi cần bộ phận kế toán xác nhận.

### Q13: Cập nhật giá vốn (`Product.CostPrice`) khi nhập kho (B1)
- **Vấn đề:** Hệ thống hiện tại giữ nguyên `Product.CostPrice` khi xác nhận nhập kho (`StockInService.cs:355-365`), chưa tự động tính lại theo phương pháp bình quân gia quyền hay giá nhập cuối.
- **Phương án đề xuất:** Duy trì cơ chế hiện tại (giữ giá vốn tĩnh hoặc cập nhật thủ công) hoặc bổ sung tùy chọn tự động cập nhật giá bình quân gia quyền theo cấu hình hệ thống, ảnh hưởng trực tiếp đến báo cáo COGS và lợi nhuận gộp trong `GetAdvancedDashboardAsync`.

### Q14: Bất biến tồn kho khi sản phẩm có tồn kho cũ nhưng chưa có `WarehouseStock` (B1)
- **Vấn đề:** Sản phẩm có `CurrentStock > 0` từ trước nhưng KHÔNG có dòng `WarehouseStock` tương ứng trong bảng `WarehouseStocks`. Khi chốt phiếu nhập kho 5 đơn vị vào kho cụ thể, `CurrentStock` tăng lên tổng nhưng `WarehouseStock` chỉ ghi nhận 5 đơn vị mới, dẫn đến vi phạm bất biến `CurrentStock = SUM(WarehouseStock.Stock)`.
- **Phương án đề xuất:** 
  1. Chạy script migration backfill tự động tạo dòng `WarehouseStock` tại kho mặc định bằng với `CurrentStock` cũ trước khi áp dụng phân kho.
  2. Bổ sung kiểm tra trong service nhập/xuất kho: nếu chưa có dòng `WarehouseStock` cho tồn ban đầu, tự động quy đổi hoặc khởi tạo vào kho mặc định.

### Q15: Xử lý Idempotency khi dùng chung một Key cho hai phiếu khác nhau (B1)
- **Vấn đề:** Khi client sử dụng cùng một `Idempotency-Key` cho hai request chốt phiếu nhập kho với nội dung (payload / items / tổng tiền) khác nhau, hệ thống hiện tại so sánh request hash và ném ngoại lệ `ConflictException` ("Idempotency-Key đã được dùng cho nội dung khác").
- **Phương án đề xuất:** Giữ nguyên hành vi hiện tại (ném ngoại lệ `ConflictException`) nhằm đảm bảo tính toàn vẹn và ngăn chặn client tái sử dụng nhầm khóa cho giao dịch khác. Tài liệu hóa cho đội ngũ frontend/client quy định sinh `Idempotency-Key` mới cho mỗi request nghiệp vụ độc lập.

### Q18: Tạo hoặc sửa sản phẩm không khởi tạo / cập nhật WarehouseStock
- **Vấn đề:** `ProductService.cs:82` (`CreateAsync`) và `ProductService.cs:114` (`UpdateAsync`) khi tạo sản phẩm không khởi tạo `WarehouseStock`.
- **Kết luận & Quyết định đã chốt:** Tồn kho chỉ thay đổi qua phiếu nhập kho (B1), kiểm kê, điều chỉnh, bán, trả. Tạo sản phẩm KHÔNG được nhập tồn (`CurrentStock` mặc định = 0). Các luồng nghiệp vụ (`StockTakeService`, `ReturnService`, `WarehouseTransferService`, v.v.) khi phát sinh giao dịch tồn kho đều tự động tạo dòng `WarehouseStock` nếu thiếu. Do đó không có luồng nào bị hỏng. Q18 là rủi ro tiềm ẩn đã được bổ sung test hồi quy (`ProductService_Create_ZeroStock_MaintainsInvariant`).

### Q26: Bán hàng không trừ tồn kho theo kho (`WarehouseStocks`)
- **Vấn đề:** `OrderService.CreateOrderAsync` (`pos-api/src/PosApi/Services/OrderService.cs:62`) trước đây chỉ trừ `Product.CurrentStock` mà không trừ `WarehouseStock` của kho xuất, dẫn đến lệch bất biến tồn kho Q14 khi nhập kho tiếp theo.
- **Trạng thái:** **Đã sửa**. `OrderService` (`pos-api/src/PosApi/Services/OrderService.cs:62`, `923`) không tự tạo kho `WH-DEFAULT` hay `WarehouseStock`, thiếu dòng tồn kho tại kho sẽ ném `StockShortageException`. Hủy đơn (`CancelOrderAsync` - `pos-api/src/PosApi/Services/OrderService.cs:923`) khóa dòng `Orders` `FOR UPDATE` (`OrderService.cs:938`) rồi kiểm `Status` (`OrderService.cs:950`); nếu đã hủy thì ném `BusinessRuleException` (`OrderService.cs:952`); hủy đơn cũ không có `WarehouseId` chỉ hoàn `CurrentStock` và ghi `PosLog` `ORDER_CANCEL_NO_WAREHOUSE_ID` (`pos-api/src/PosApi/Services/OrderService.cs:1032`). Lưu ý: chưa kiểm hủy song song (test chạy một luồng).

### Q27: Trả nhà cung cấp trừ `WarehouseStocks` nhưng không trừ `CurrentStock`
- **Vấn đề:** Nghiệp vụ trả hàng nhà cung cấp (`SupplierReturnService.cs:161-175`) duyệt phiếu trả NCC chỉ trừ `WarehouseStocks`, không trừ `Product.CurrentStock`.
- **Trạng thái:** **Đã sửa**. Đã bổ sung `item.Product.CurrentStock = Round3(item.Product.CurrentStock - item.Qty);` tại `SupplierReturnService.cs:171` trong `ConfirmSupplierReturnAsync`. Kiểm thử bằng test hồi quy `ConfirmSupplierReturn_UpdatesWarehouseStockAndCurrentStock_MaintainsQ14` (`pos-api/tests/PosApi.Tests/SupplierReturnServiceTests.cs`). Cancel chỉ hủy Draft, không đụng kho. Quan sát phụ về đồng thời: chưa kiểm, chưa sửa.

### Q28: Lệch bất biến Q14 khi bán hàng rồi nhập kho tiếp theo
- **Vấn đề:** Bán hàng không trừ `WarehouseStock` khiến tổng tồn kho các kho khác `CurrentStock`, gây ngoại lệ `BusinessRuleException` khi chốt phiếu nhập kho kế tiếp.
- **Trạng thái:** **Đã sửa**. Đã khắc phục tại `OrderService` (`pos-api/src/PosApi/Services/OrderService.cs:62`, `923`), kiểm thử bằng test hồi quy `Q28_SellThenStockIn_Success_NoInvariantException` (`pos-api/tests/PosApi.Tests/OrderServiceTests.cs:270`).

### Q29: Xử lý dữ liệu đã bán trước bản sửa
- **Vấn đề:** Dữ liệu có thể bị lệch bất biến Q14 từ hai nguồn rõ ràng: (1) đơn đã bán trước bản sửa (`CurrentStock < SUM(WarehouseStocks)`), và (2) hủy đơn cũ không có `WarehouseId` hoặc hoàn BOM khi không có dòng kho (`CurrentStock > SUM(WarehouseStocks)`). `ReconcileAsync` (Q14) chỉ xử lý `diff > 0` và không được gọi tự động từ mã ứng dụng.
- **Trạng thái:** **Đã sửa (công cụ quản trị)**. Đã bổ sung endpoint quản trị `POST /api/v1/inventory/reconcile` (`InventoryController.cs`), hỗ trợ `DryRun = true` (mặc định) để tính toán chênh lệch (`ComputeDiffsAsync`) và `DryRun = false` để tự động cân bằng tồn kho (`ReconcileAsync`) đối với sản phẩm thừa (`diff > 0`) vào kho mặc định (hoặc kho chỉ định hợp lệ) kèm ghi `StockLedger` và `PosLog` trong transaction execution strategy.
- **Lưu ý nghiệp vụ:** 
  - Trường hợp `diff < 0` (thiếu tồn kho so với warehouse stocks) chỉ thực hiện báo cáo (phương án C), không tự động sửa `CurrentStock` hay `WarehouseStocks`.
  - Tham số `TargetWarehouseId` nếu được truyền nhưng không tồn tại (`KeyNotFoundException`) hoặc không hoạt động (`InvalidOperationException`) sẽ bị từ chối nghiệp vụ (không thực hiện).
- **Quan sát kỹ thuật:** 
  - (1) `ReconcileAsync` cũ không quản lý transaction riêng hay khóa dòng (`FOR UPDATE`).
  - (2) Bút toán `StockLedger` cân bằng sử dụng `Balance = p.CurrentStock`.
  - (3) `ComputeDiffsAsync` đọc không khóa dòng, do đó báo cáo có thể lệch nhẹ so với phần ghi nếu phát sinh bán hàng đồng thời; nên chạy công cụ đối soát này ngoài giờ bán.
- **Kiểm thử:** Đã phủ unit tests tại `InventoryReconcilerTests.cs` (`ComputeDiffs_DryRun_DoesNotModifyDatabase`, `ReconcileApply_Surplus_UpdatesWarehouseStockAndLedger_Idempotent`, `ReconcileApply_Deficit_DoesNotModifyStock`, `ComputeDiffs_IgnoresInactiveProducts`, `ComputeDiffs_NoActiveWarehouse_ThrowsInvalidOperationException`, `ReconcileInventory_NullBody_DefaultsToDryRun`, `Reconcile_NonExistentWarehouse_ThrowsKeyNotFoundException_NothingWritten`, `Reconcile_InactiveWarehouse_ThrowsInvalidOperationException`).

### Q20: Phân loại phiếu nhập tạo giữa migration B1 và B2 trong tính lại công nợ
- **Vấn đề thực tế trong mã nguồn:** Các hằng số `B2_MIGRATION_DATE`, `B2_ROLLOUT_DATE`, `IsB2Applied` hoàn toàn không tồn tại trong mã nguồn thực tế. Quy tắc phân biệt "trước B2" chưa được cài đặt. `RecalculateAsync` trong `SupplierDebtService` chỉ so sánh số dư (`recordedBalance` vs `calculatedBalance`) và báo cáo lệch (discrepancy), KHÔNG tạo hoặc sửa công nợ (`SupplierDebts`). Do đó, phiếu cũ thiếu công nợ sẽ chỉ bị báo lệch (discrepancy) chứ không bị tự động sinh nợ sai.
- **Trạng thái DB & Giả định:** DB dev `digism_pos` hiện có migration B1 nhưng chưa có B2. Lưu ý: nhận định "chưa DB nào áp Q14 bản cũ" và "DB duy nhất là DB dev" là giả định của chủ dự án, không phải điều Claude Code đã xác minh độc lập.
- **Phương án ghi nhận:** Giữ nguyên hành vi hiện tại của `RecalculateAsync` (chỉ báo lệch, không tự sửa/tạo nợ) để tránh can thiệp ngoài ý muốn.

### Q25: Chứng từ nhập tồn ban đầu khi khởi tạo hệ thống
- **Vấn đề:** Muốn nhập tồn hiện có khi cửa hàng bắt đầu sử dụng hệ thống thì dùng phiếu nhập kho B1 hay cần chứng từ "tồn đầu kỳ" riêng?
- **Phương án đề xuất:** (Chỉ ghi nhận) Sử dụng phiếu nhập kho B1 với nhà cung cấp nội bộ hoặc cờ tồn đầu kỳ (`IsBackfilled`), hoặc xây dựng chứng từ kiểm kê/điều chỉnh đầu kỳ chuyên biệt.
