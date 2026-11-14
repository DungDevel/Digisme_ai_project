# Chi tiết tính năng nhóm 8 (B1–B9) — bản điều chỉnh 11/11

Nguồn: `gap-analysis.md` (mục 3.8) và các quyết định ngày 11/11/2026. Thời hạn: 11/11 → 03/12 (feature freeze).

## 1. Tổng quan sau điều chỉnh

| Mã | Chức năng | Nhóm | Effort | Thay đổi so với file gốc |
| --- | --- | --- | --- | --- |
| B1 | Nhập kho | Đầu vào | 1,5 ngày | Giữ nguyên |
| B2 | Ghi nợ nhà cung cấp | Đầu vào | 1 ngày | Giữ nguyên |
| B3 | Rà soát kho đầu ra | (không còn là tính năng) | 0 ngày riêng | Gộp vào PR sửa lỗi V1–V3 |
| B4 | Thao tác bàn phím | Đầu ra | 1,5 ngày | Giữ nguyên |
| B5 | Tích hợp SePay | Đầu ra | 3 ngày | Làm polling trước, webhook sau |
| B6 | Điện thoại làm máy quét mã | Đầu ra | 2 ngày | Giữ nguyên, 2 giai đoạn |
| B7 | Báo cáo + Mở ca + Quầy/máy POS | Đầu ra | 3 ngày | +0,5 ngày cho Quầy và máy POS |
| B8 | Kiểm kê kho | Đầu ra | 1 ngày | Thêm quy tắc chặn bán và ngưỡng duyệt |
| B9 | AI: hóa đơn điện tử + giấy viết tay | Đầu ra | 4,5 ngày | Chốt phạm vi: đọc chứng từ của NCC |
|  | **Tổng** |  | **17,5 ngày** | Chưa tính tunnel B5 (+0,5 ngày, tùy chọn) |

**Thứ tự thực thi:** B1 → B2 → B7 → B5 → B4 → B8 → B6 → B9.

**Nếu thiếu thời gian, trễ theo thứ tự:** B9b → B6 giai đoạn 2 → B8.

## 2. Các quyết định đã chốt

1. **"Đầu vào / đầu ra" là cách chia nhóm chức năng**, không phải một tính năng. Đầu vào gồm B1, B2. Đầu ra gồm B4 đến B9.
2. **B9 = đọc chứng từ của NCC để nhập kho.** Xuất hóa đơn điện tử cho khách là hạng mục riêng, chưa nằm trong scope.
3. **Giấy viết tay làm trước = phiếu giao/nhập hàng của NCC.** Sổ ghi nợ và ghi chép tự do để sau.
4. **SePay chạy local:** dùng polling API SePay làm cơ chế chính, webhook bật thêm khi có địa chỉ HTTPS công khai.
5. **Ca làm việc gắn với quầy (két tiền)**, có ghi người mở và người chốt. Đổi người theo ca trong ngày.
6. **Số quầy và máy POS do chủ cửa hàng cấu hình**, không cố định trong code.
7. **Kiểm kê không chặn bán mặc định**, có cờ cấu hình để chặn. Ngưỡng duyệt tính theo giá trị tiền.

## 3. B1 — Nhập kho (đầu vào) · 1,5 ngày

**Mục tiêu:** có phiếu nhập kho theo từng kho, làm nguồn duy nhất cho tồn kho, giá vốn và thuế mua vào.

**Hiện trạng:** mục 3.4 không có phiếu Nhập kho, chỉ có `SupplierInvoice`. Cần xác minh `SupplierInvoice` hiện đã gắn `WarehouseId` chưa, hay chưa có luồng nhập nào.

**Phụ thuộc:** Multi-warehouse (đã xong), ADR 0001 (`Supplier`).

**Dữ liệu**

- `StockReceipt`: kho đích, `SupplierId`, ngày, trạng thái (Nháp / Đã chốt / Hủy), tổng tiền, VAT, chiết khấu, số tiền đã trả, hạn thanh toán (`DueDate`).
- `StockReceiptItem`: sản phẩm, đơn vị tính (có quy đổi), số lượng, giá nhập, VAT, chiết khấu.

**Việc cần làm**

- [ ] Tạo entity, migration EF, CRUD phiếu ở trạng thái Nháp.
- [ ] **Chốt phiếu trong một transaction:** tăng `WarehouseStock`, tăng `Products.CurrentStock`, ghi `StockLedger` (có `WarehouseId`), cập nhật giá vốn (bình quân gia quyền hoặc theo quy tắc ứng dụng đang dùng).
- [ ] **Hủy chốt bằng bút toán đảo**, không xóa dòng cũ. Chặn hủy nếu hàng đã bán khiến tồn âm.
- [ ] **Một nguồn chứng từ duy nhất:** phiếu nhập thay hoặc liên kết `SupplierInvoice`, để Kê khai thuế mua vào (3.6) chỉ đọc một nguồn.
- [ ] Phiếu Nháp là điểm vào chung cho nhập tay, quét mã (B6) và nhập từ ảnh/XML (B9).
- [ ] Ghi `PosLog` khi chốt, hủy, sửa.

**Xong khi:** tồn từng kho khớp tổng `StockLedger` sau khi nhập, chốt, hủy chốt. Có test integration.

## 4. B2 — Ghi nợ nhà cung cấp (đầu vào) · 1 ngày

**Mục tiêu:** nhập kho chưa trả đủ thì tự ghi nợ NCC. Thanh toán và trả hàng giảm nợ đúng.

**Điểm cần xác minh:** mục 3.5 ghi Công nợ NCC "đã xong" nhưng phụ thuộc ADR 0001, trong khi nhóm 2 chưa tick. Kiểm tra `Debt` của NCC đang gắn vào `Customer` hay `Supplier`.

**Phụ thuộc:** B1, ADR 0001, Phiếu chi (đã xong).

**Việc cần làm**

- [ ] Chốt phiếu nhập còn phần chưa trả: tự tạo hoặc tăng nợ NCC, ghi `DueDate`.
- [ ] Phiếu chi gắn NCC giảm nợ. Phân bổ vào phiếu nhập cũ nhất trước, hoặc cho chọn phiếu.
- [ ] Phiếu Trả NCC (3.4) giảm nợ tương ứng.
- [ ] Tuổi nợ 3 nhóm: dưới 30 ngày, 30–60 ngày, trên 60 ngày.
- [ ] Chặn xóa NCC còn nợ.
- [ ] Chức năng "Tính lại công nợ" (một trong 8 mục ADR 0001) dùng chung logic này.

**Xong khi:** dư nợ NCC = tổng ghi nợ − tổng đã trả. Sổ công nợ NCC khớp báo cáo nợ ở B7.

## 5. B3 — Rà soát kho đầu ra (không còn là tính năng riêng)

"Đầu ra" là tên nhóm chức năng nên B3 không có đầu việc phát triển riêng. Phần rà soát kho vẫn cần, vì B7 và B8 dựa vào số liệu kho đúng. Làm trong các PR sửa lỗi V1–V3:

- [ ] Mỗi đường hàng rời kho (bán, xuất kho, chuyển kho, trả NCC) trừ đúng `WarehouseStock` của kho phát sinh và có `StockLedger`.
- [ ] Hàng khách trả cộng lại đúng kho của hóa đơn gốc (V3).
- [ ] Bán hàng chọn kho xuất theo quầy (mặc định lấy từ `Counter`, xem B7).
- [ ] Hủy hoặc sửa chứng từ ghi `PosLog`.
- [ ] Bán ghi nợ khách dùng chung `Debt` với thu nợ cũ (nhóm 1).

**Xong khi:** bất biến "tồn = tổng sổ kho" đúng sau mỗi loại đầu ra.

## 6. B4 — Thao tác bàn phím · 1,5 ngày

**Mục tiêu:** thu ngân hoàn tất một đơn (tìm hàng → số lượng → khách → chiết khấu → thanh toán) không cần chuột.

**Bộ phím tắt đề xuất** (nhóm chốt lại, lưu trong một file cấu hình để đổi không cần sửa code):

| Phím | Chức năng |
| --- | --- |
| F1 | Mở bảng liệt kê phím tắt |
| F2 | Tìm sản phẩm |
| F3 | Chọn khách |
| F4 | Chiết khấu |
| F7 | Đơn treo (đã có) |
| F8 | Dùng điểm |
| F9 | Thanh toán |
| Esc | Hủy / đóng |
| Enter | Thêm dòng |
| + / − | Đổi số lượng |
| Delete | Xóa dòng |

**Việc cần làm**

- [ ] Lớp xử lý phím toàn màn hình POS, đọc cấu hình từ file.
- [ ] Ô quét mã luôn giữ focus. Nhận chuỗi ký tự nhanh từ máy quét USB/Bluetooth kết thúc bằng Enter.
- [ ] Thanh gợi ý phím tắt ở chân màn hình.
- [ ] `preventDefault` cho phím trùng trình duyệt (F1, F5, F6, F11). Kiểm tra trên Chrome và Edge.
- [ ] Dùng `event.code` thay vì `event.key`, vì bộ gõ tiếng Việt (Unikey, EVKey) có thể làm sai `event.key`.
- [ ] Phím tắt không kích hoạt khi đang gõ trong ô nhập (trừ phím F và Esc).

**Xong khi:** một đơn đầy đủ hoàn tất chỉ bằng bàn phím. Ô quét nhận đúng khi quét liên tục.

## 7. B5 — Tích hợp SePay · 3 ngày (+0,5 ngày nếu dựng tunnel)

**Mục tiêu:** khách chuyển khoản qua VietQR động thì đơn tự chuyển Đã thanh toán, POS nhận thông báo, ca và sổ quỹ khớp.

**Bối cảnh:** hệ thống chạy local, nên SePay **không gọi webhook về được** nếu chưa có địa chỉ HTTPS công khai. Vì vậy làm hai đường, bắt đầu bằng polling.

**Phụ thuộc:** POS06 (VietQR động), SignalR, Sổ quỹ. Cần tài khoản ngân hàng đã liên kết SePay và chạy Test mode trước khi dùng tiền thật.

**Dữ liệu:** bảng `BankTransactions` gồm `SepayId` (unique), `ReferenceCode`, ngân hàng, số tiền, loại vào/ra, nội dung, `code`, thời gian, `MatchedOrderId`, trạng thái (Khớp / Chưa khớp / Lệch tiền / Trùng), payload gốc.

**Việc cần làm**

- [ ] **Polling (cơ chế chính khi chạy local):** job gọi API giao dịch của SePay mỗi vài giây, chỉ lấy giao dịch mới.
- [ ] **Webhook (bật khi có HTTPS):** `POST /api/payments/sepay/webhook`, không cần JWT nhưng xác thực bằng HMAC-SHA256 (khuyến nghị) hoặc API Key, so sánh constant-time. Trả HTTP 200/201 kèm `{"success": true}`.
- [ ] Logic đối chiếu **dùng chung** cho cả polling và webhook.
- [ ] **Chống trùng** theo `SepayId`, kết hợp `referenceCode`, `transferType`, `transferAmount`, vì SePay có retry.
- [ ] Đối chiếu đơn theo mã đơn trong nội dung chuyển khoản. Đủ tiền → Paid. Thiếu → chờ thêm. Thừa → ghi nhận phần thừa và báo thu ngân. Đơn đã hủy hoặc đã thanh toán → vào hàng "Chưa khớp".
- [ ] Đẩy SignalR về đúng quầy POS ("Đã nhận X đ cho đơn Y") để màn hình thanh toán tự đóng.
- [ ] Màn hình "Giao dịch chưa khớp" cho Store Manager/Accountant xử lý tay. Nút "Xác nhận thủ công" của thu ngân bắt buộc ghi `PosLog` (rủi ro gian lận).
- [ ] Ghi Sổ quỹ kênh QR và cộng vào `ExpectedTransfer` của ca đang mở trên quầy.
- [ ] Khóa lưu trong biến môi trường (`SEPAY_API_KEY`, `SEPAY_WEBHOOK_SECRET`), thêm tên vào `.env.example` và danh sách secret của GitHub Actions. **Không commit giá trị.**
- [ ] (Tùy chọn, +0,5 ngày) dựng tunnel có địa chỉ cố định để bật webhook.
- [ ] Test: payload mẫu, gửi trùng, sai số tiền, sai khóa, đơn đã hủy. Thử bằng Test mode của SePay.

**Xong khi:** mô phỏng giao dịch trên SePay Test mode thì đơn tự chuyển Paid, POS nhận thông báo, ca và sổ quỹ khớp.

## 8. B6 — Điện thoại làm máy quét mã vạch · 2 ngày

**Mục tiêu:** dùng điện thoại (Android hoặc iOS) thay máy quét mã vạch cho POS.

**Giai đoạn 1 (làm sớm cùng B4, gần như không tốn công)**

- [ ] Dùng ứng dụng điện thoại bên thứ ba có chế độ gửi mã như bàn phím, kết hợp ô quét luôn focus ở B4.
- [ ] Viết hướng dẫn cài đặt và kiểm tra tốc độ nhận ký tự.

* Không cần HTTPS, không phân biệt hệ điều hành.

**Giai đoạn 2 (tích hợp sẵn, làm sau)**

- [ ] POS hiển thị QR ghép cặp chứa token ngắn hạn, dùng một lần, gắn user và quầy.
- [ ] Điện thoại mở trang `/scan`, quét liên tục bằng camera, gửi mã qua SignalR đến đúng phiên POS.
- [ ] POS nhận mã vào đúng ngữ cảnh đang mở: giỏ hàng, phiếu nhập (B1), kiểm kê (B8).
- [ ] Báo rung/âm khi quét được. Báo rõ khi mã không có trong hệ thống. Giới hạn tốc độ gửi.
- [ ] Dùng thư viện giải mã (ví dụ ZXing) cho cả hai nền tảng, vì API quét mã sẵn có của trình duyệt (đặc biệt trên iPhone) chưa hỗ trợ đồng đều.

**Rủi ro:** camera chỉ chạy trên HTTPS. Nếu đã dựng tunnel cho B5 thì dùng chung địa chỉ đó. Nếu chưa, giai đoạn 2 cần chứng chỉ HTTPS nội bộ hoặc tunnel.

**Xong khi:** quét 20 mã liên tiếp từ điện thoại, giỏ hàng nhận đủ, không nhận nhầm sang máy POS khác.

## 9. B7 — Báo cáo + Mở ca + Quầy/máy POS · 3 ngày

**Mục tiêu:** báo cáo số liệu đúng, ca làm việc chặt chẽ theo quầy, chủ cửa hàng tự chọn số quầy và máy POS.

**Phụ thuộc:** các PR sửa lỗi V1 (doanh số), V3 (kho), V4 (chốt ca). Phần thu SePay cần B5. B7 trùng một phần với Dashboard nâng cao (3.6), nên làm chung.

### 9.1. Quầy và máy POS (+0,5 ngày)

- `Counter` (Quầy): tên, chi nhánh, kho xuất mặc định, trạng thái (Hoạt động / Khóa), thứ tự hiển thị.
- `PosDevice` (Máy POS): tên máy, `CounterId`, mã thiết bị, trạng thái.
- Một quầy có nhiều máy POS, nhưng mỗi quầy chỉ có một ca mở.

* [ ] Màn hình **Cài đặt → Quầy & máy POS** cho Owner/Store Manager: thêm, sửa, khóa quầy, đăng ký máy vào quầy.
* [ ] Giới hạn số quầy và máy đặt qua một cấu hình (ví dụ theo gói), mặc định không giới hạn.
* [ ] Không cho khóa hoặc xóa quầy đang có ca mở.

### 9.2. Mở ca và bàn giao ca (1 ngày)

- `Shift` thêm `CounterId`, `OpenedBy`, `ClosedBy`. Xác minh trước: `Shift` đã có `OpeningCash`, có thể backend mở ca đã có sẵn và phần thiếu chỉ là giao diện và luồng chặn.

* [ ] Modal mở ca: nhập tiền đầu ca (gợi ý bằng tiền cuối ca trước, có thể đếm theo mệnh giá), chọn quầy.
* [ ] Mỗi quầy một ca mở. Mỗi thu ngân một ca mở.
* [ ] Chặn bán khi quầy chưa mở ca. `Order.ShiftId` gắn ca đang mở.
* [ ] Bàn giao ca = chốt ca cũ (đối soát) rồi mở ca mới.
* [ ] Mở ca và Chốt ca (V4) dùng chung màn hình đếm mệnh giá.
* [ ] Ghi `PosLog` cho mở, chốt, bàn giao.

### 9.3. Báo cáo (1,5 ngày)

- [ ] **Doanh thu:** trừ hàng trả và giá vốn tương ứng (sửa V1). Xem theo ngày, ca, quầy, thu ngân, chi nhánh, phương thức thanh toán.
- [ ] **Nợ:** công nợ khách và NCC, tổng nợ, tuổi nợ 3 nhóm, nợ quá hạn, chi tiết theo đối tượng, xuất Excel.
- [ ] **Cân đối kho:** xác minh tồn đầu + nhập − xuất = tồn cuối theo từng kho, sau khi sửa V3.
- [ ] **Sổ quỹ:** xác minh đủ hoàn tiền trả hàng, thu từ SePay (B5), thu/chi theo ca.

**Xong khi:** báo cáo doanh thu khớp (bán − trả), báo cáo nợ khớp sổ công nợ, quy trình mở ca → bán → chốt ca đối soát khớp. Nếu bảng quầy hoặc backend mở ca đã có sẵn thì effort có thể giảm còn 2,5 ngày.

## 10. B8 — Kiểm kê kho · 1 ngày

**Mục tiêu:** kiểm kê chạy thật, có duyệt chênh lệch, không làm gián đoạn bán hàng.

**Hiện trạng:** mục 3.4 ghi đã xong (theo kho, chênh lệch điều chỉnh). Việc đầu tiên là xác minh chạy thật, sau đó bổ sung.

**Quy tắc đề xuất**

- **Không chặn bán mặc định.** Hệ thống ghi tồn tại thời điểm đếm từng dòng nên đơn bán trong lúc kiểm kê không làm sai số chênh lệch.
- Hiển thị cảnh báo "đang kiểm kê" trên các mặt hàng liên quan.
- Có cờ cấu hình "chặn bán khi kiểm kê" cho trường hợp kiểm kê toàn kho ngoài giờ.
- Ngưỡng duyệt tính theo giá trị tiền = |chênh lệch| × giá vốn, cấu hình được. Giá trị dưới đây chỉ là mức khởi điểm, chỉnh theo quy mô cửa hàng:

| Giá trị chênh lệch | Ai duyệt |
| --- | --- |
| Dưới 200.000đ | Tự động điều chỉnh, có ghi vết |
| 200.000đ – 1.000.000đ | Store Manager |
| Trên 1.000.000đ | Owner / Accountant |

- Người đếm không được tự duyệt chênh lệch của chính mình.

**Việc cần làm**

- [ ] Chạy thật: tạo phiếu, nhập số đếm, chốt → sinh bút toán điều chỉnh `StockLedger` đúng kho.
- [ ] Kiểm kê một phần (theo nhóm hàng hoặc vị trí), không bắt buộc cả kho.
- [ ] Nhập số đếm bằng quét mã (B6) hoặc ô quét luôn focus (B4).
- [ ] Duyệt chênh lệch theo ngưỡng giá trị, ghi `PosLog`.
- [ ] Cờ chặn bán và cảnh báo trên mặt hàng.
- [ ] In phiếu kiểm kê.

**Xong khi:** sau chốt kiểm kê, tồn hệ thống = số đếm, có bút toán điều chỉnh, báo cáo cân đối kho phản ánh đúng.

## 11. B9 — AI: hóa đơn điện tử và giấy viết tay · 4,5 ngày

**Phạm vi đã chốt:** **đọc** chứng từ của NCC để tạo phiếu nhập Nháp. Mở rộng router A2. Việc **xuất** hóa đơn điện tử cho khách (kết nối nhà cung cấp hóa đơn, chữ ký số, gửi cơ quan thuế) là hạng mục riêng, ngoài scope này.

**Phụ thuộc:** A2, B1, ADR 0001 (cần trường MST trong `Supplier`).

### 11.1. B9a — Hóa đơn điện tử của NCC (1,5 ngày)

- [ ] Nhận file XML, đọc bằng parser (không cần OCR): MST và tên người bán, số và ký hiệu hóa đơn, ngày, dòng hàng, thuế suất, tổng tiền.
- [ ] Khớp NCC theo MST. Chống nhập trùng theo (MST, ký hiệu, số hóa đơn).
- [ ] PDF hóa đơn điện tử: lấy lớp chữ trong PDF trước, không có mới chuyển sang đọc ảnh.
- [ ] Khớp sản phẩm bằng `pg_trgm`/`unaccent` như A2.
- [ ] Tạo phiếu nhập Nháp (B1), kèm dữ liệu cho Kê khai thuế mua vào.

### 11.2. B9b — Giấy viết tay (3 ngày)

- **Loại giấy làm trước:** phiếu giao/nhập hàng viết tay của NCC. Sổ ghi nợ và ghi chép tự do để sau.

* [ ] Đọc ảnh bằng mô hình thị giác, trả dữ liệu có cấu trúc kèm **độ tin cậy từng trường**.
* [ ] Màn hình soát lại đặt cạnh nhau (ảnh bên trái, bảng bên phải), tô nổi ô có độ tin cậy thấp.
* [ ] **Không bao giờ tự nhập kho khi chưa có người xác nhận.**
* [ ] Đo chất lượng trên bộ mẫu có đáp án do nhóm tự chụp (nên có 20–30 ảnh phiếu thật). Báo tỷ lệ đúng theo trường, không cam kết con số khi mẫu ít.
* [ ] Lỗi hoặc timeout dịch vụ AI: cho nhập tay, không chặn quy trình (cờ `model_status: pending`).

- Lưu ý: gửi ảnh chứng từ ra dịch vụ AI bên ngoài tốn chi phí và là dữ liệu kinh doanh. Nhóm cần chốt chính sách trước khi dùng dữ liệu thật.

**Xong khi:** 1 file XML và 1 ảnh viết tay mẫu đi hết luồng đến phiếu nhập Nháp, có test integration.

## 12. Việc cần xác minh trong code trước khi bắt đầu

1. Nhập hàng hiện đi qua `SupplierInvoice` chưa gắn `WarehouseId`, hay chưa có luồng nhập nào (B1).
2. `Debt` của NCC đang gắn vào `Customer` hay `Supplier` (B2).
3. `Shift` đã có `CounterId` hay bảng quầy chưa, backend mở ca đã có đến đâu (B7).
4. Kiểm kê (3.4) có chạy thật không (B8).
5. Nhóm 1, 2, 6 vẫn là `[ ]`. Nếu đã làm thì tick lại để tính đúng capacity.
6. Baseline test: mục 4 ghi 38, báo cáo Claude Code ghi 142. Cần chốt vạch thực tế.
7. Ngân hàng có nằm trong danh sách SePay hỗ trợ không (B5).

## 13. Capacity

Nhóm 8: 17,5 ngày. Cộng phần còn lại (nhóm 1, 2, 6 chưa tick: 9 ngày; 4 AI routers: 4 ngày) là khoảng 30,5 ngày so với 22 ngày lịch còn đến 03/12, tỷ lệ khoảng 1,4 lần. Con số này chưa tính các PR sửa lỗi V1–V4. Khi vượt, trễ theo thứ tự B9b → B6 giai đoạn 2 → B8.
