# Tổng hợp chức năng: tmSales & DigiSME Roadmap

> Nguồn: `tmsales_feature_doc.md` (khảo sát 28–29/10/2026) và `DigiSME_Roadmap_2_Thang.md` (lập 28/09/2026).
> Bản tổng hợp lập ngày 29/10/2026.

## 0. Mối quan hệ giữa hai tài liệu

| | tmSales | DigiSME Roadmap |
|---|---|---|
| Bản chất | Bản khảo sát hệ thống POS siêu thị **đang chạy** (v1.0.9667, Trung Minh Group Sales, Đà Nẵng) | Kế hoạch 2 tháng cho đội 3 người **xây một POS mới** |
| Phạm vi | 61 trang + 7 hướng dẫn, chỉ đọc/quan sát | 9 nhóm nghiệp vụ lấy từ tmSales + 4 mô hình AI |
| Vai trò | Hệ thống tham chiếu | Sản phẩm cần xây |

---

## 1. tmSales — chức năng hiện có

tmSales là phần mềm quản lý kinh doanh cho SME và hộ kinh doanh, gồm 3 phân hệ: **bán sỉ & kho**, **bán lẻ (POS)**, **dịch vụ & F&B**.

**Giao diện chung:** sidebar 9 nhóm, các trang mở dạng tab, mẫu ListView thống nhất (New / Delete / Edit / View / Export / Tìm kiếm / Lọc, phân trang, dòng "Đang lọc").

### 1.1. Welcome — Bảng điều khiển
- 4 KPI: doanh thu hôm nay, đơn hàng mới, khách đang nợ, số khách hàng.
- Biểu đồ biến động doanh thu 7 ngày.
- Top 5 bán chạy; nút làm mới dữ liệu.

### 1.2. Bán lẻ (POS)
| Màn hình | Chức năng |
|---|---|
| Danh mục Phí & Dịch vụ | Phí theo tỷ lệ % hoặc tiền mặt, có cờ tự động (ví dụ gói hàng, trang trí) |
| Đơn Bán Hàng | Chiết khấu VIP, thu nợ cũ, giảm giá, phí dịch vụ, thuế; hình thức thanh toán (tiền mặt / quẹt thẻ / ghi nợ); hình thức giao (shop tự giao, đơn vị vận chuyển); dùng điểm |
| Khách trả hàng | Lấy đơn gốc để lập phiếu trả |
| Nhật ký POS | Ghi lại hành động nhạy cảm (ví dụ hủy giỏ hàng đang bán): thời gian, nhân viên, tổng tiền liên quan |

### 1.3. Danh mục Bán hàng
- **Bảng giá bán:** 629 sản phẩm, đơn giá, chiết khấu, mã hàng, mã vạch, tỷ lệ quy đổi.
- **Nhóm đối tác:** nhóm khách và nhóm NCC; mỗi nhóm có chiết khấu mặc định, nhóm cha, cờ "Là NCC".
- **Khách hàng & NCC:** mã đối tác, nhóm, bảng giá áp dụng, thông tin ngân hàng, điểm tích lũy, phải thu / phải trả đầu kỳ; nút "Tính lại công nợ".
- **Lịch sử điểm:** giao dịch tích / đổi điểm theo đơn.
- **Phiên làm việc (ca):** nhân viên trực, máy xuất bill, tiền đầu ca, doanh thu theo hình thức, phiếu thu/chi, tiền mặt lý thuyết so với đếm thực tế, chênh lệch.

### 1.4. Danh mục Quản lý (16 mục)
1. **Nhóm hàng** — cây cha-con, tiền tố mã, cờ hàng cân điện tử, phân hệ kinh doanh, ẩn trên POS.
2. **Sản phẩm** — mã hàng, mã vạch, nhóm, ĐVT, VAT, tồn tối thiểu, đổi điểm, giá bán / giá vốn / giá nhập cuối, số ngày tồn.
3. **Thành phần / Định lượng** — nguyên liệu tiêu hao theo sản phẩm (dùng cho F&B).
4. **Đơn vị quy đổi** — ĐVT quy đổi, tỷ lệ, mã vạch riêng, giá bán nhanh.
5. **Loại bảng giá** — Bán lẻ / Bán sỉ; có nút nhân bản.
6. **Ma trận điều chỉnh giá** — xem giá vốn, giá nhập, số ngày tồn, đơn giá, % CK, lãi theo từng loại giá.
7. **Danh mục Kho** (Kho cửa hàng, Kho vật tư).
8. **Đơn vị tính** (32 đơn vị).
9. **Khoản thu** (thu khác, thu nợ, thu tiền hàng).
10. **Nhóm chi phí** (chi khác, chi tiền hàng, chi vận chuyển, chi trả nợ, chi văn phòng).
11. **Ngân hàng** — 41 ngân hàng kèm mã BIN VietQR.
12. **Nhóm thuế** — thuế GTGT và TNCN theo ngành (bán lẻ 1,5% / 0,5%; ăn uống 3% / 1,5%).
13. **Chức vụ** — Giám đốc, Kế toán, Nhân viên bán hàng, Thu ngân, Thủ kho.
14. **Phiếu yêu cầu In Tem.**
15. **Thiết bị POS** — đăng ký máy theo fingerprint, bật/tắt hoạt động.
16. **Từ điển sản phẩm** — map tên nhận dạng từ AI/hóa đơn vào sản phẩm chuẩn.

### 1.5. Kho hàng
| Màn hình | Chức năng |
|---|---|
| Phiếu nhập kho | Theo loại nhập, trạng thái, người giao |
| Đơn hàng chờ | 3 vùng: tổng hợp hàng đặt trước (so với tồn, báo "Thiếu"), đơn chờ xuất kho, chi tiết đơn đang chọn |
| Phiếu xuất kho | Có chốt / hủy chốt phiếu; mã tracking SePay; số HĐĐT; hóa đơn nháp |
| Đơn hàng Mua | Đã thanh toán, còn lại (nợ) |
| Phiếu chuyển kho | Kho đi / kho đến |
| Phiếu kiểm kê | Kiểm kê theo kho |
| Trả hàng NCC | Chốt / hủy chốt |
| Thẻ kho | Nhập, xuất, tồn cuối, giá vốn theo chứng từ |
| Tồn kho chi tiết | Số lượng tồn theo sản phẩm (hiện có nhiều dòng tồn âm) |

### 1.6. Tài chính
- **Phiếu thu / Phiếu chi** — gắn đối tác, hình thức thanh toán, đơn hàng, phiên làm việc; có chốt / hủy chốt.
- **Sổ nhật ký chung** — gom chứng từ bán lẻ, thu, xuất kho; có nút xem chứng từ gốc.
- **Sổ công nợ** — nợ tăng / giảm, dư nợ sau giao dịch.
- **Sổ quỹ** — thu vào, chi ra, tồn quỹ sau giao dịch (tiền mặt, chuyển khoản QR).

### 1.7. Hệ thống
- **Bảng tính lương** và **Nhật ký thưởng - phạt.**
- **Cấu hình tùy chọn:** thông tin cửa hàng, ngân hàng thụ hưởng; mặc định (lãi, VAT, tỷ lệ tích / đổi điểm, cảnh báo tồn, định dạng mã hàng, mã cân, mã khách); tùy biến POS (trả hàng, thu nợ, in QR, khổ giấy, quét mã vạch mặc định); **hóa đơn điện tử qua SePay** (tự xuất, hóa đơn nháp); cấu hình Gemini.
- **Khổ giấy in tem** (giấy cuộn nhiệt, A4) và **Cấu hình mẫu in** (font, cỡ chữ, vị trí từng trường, phân hệ POS / F&B).
- **Nhân viên:** chức vụ, mã PIN quản lý, CCCD, tài khoản lương, chế độ lương, cờ nhân viên sales, kho và bảng giá mặc định.
- **Thông báo nội bộ** (khẩn cấp, đã giải quyết) và **Trung tâm bảo trì.**

### 1.8. Reports
- **Cân đối kho** (tồn đầu → nhập → xuất → tồn cuối, có doanh thu, lãi gộp).
- **Công nợ** (nợ đầu kỳ, phát sinh tăng / giảm, nợ cuối kỳ).
- **Doanh thu** (theo sản phẩm, có giá vốn và lãi gộp).
- **Báo cáo & Kê khai thuế** (theo kỳ, chọn mẫu báo cáo).
- **Dashboards** và **Thống kê dữ liệu:** doanh thu bán lẻ / bán sỉ, cơ cấu thanh toán, tổng thuế bán ra và mua vào, lợi nhuận gộp ước tính, top bán chạy / chậm, cảnh báo tồn lâu và dưới tồn tối thiểu, thu/chi chi tiết, nút lập đơn mua.

### 1.9. Trợ giúp — 7 hướng dẫn
01 Tổng quan hệ thống · 02 Tài khoản & phân quyền (QuanLy, KeToan, ThuNgan, ThuKho, Bep) · 03 Khởi tạo dữ liệu cốt lõi · 04 Quản lý hàng hóa · 05 Quản lý đối tác · 06 Bán lẻ (đăng ký thiết bị, mở/đóng ca, bán hàng) · 07 Thu ngân F&B (khu vực, bàn, phòng, dịch vụ tính giờ).

---

## 2. DigiSME Roadmap — những gì sẽ xây

**Mốc:** 28/09/2026 → feature freeze **03/12/2026** (khoảng 9,5 tuần).
**Đội hình:** M1 Backend (`pos-api` .NET + router Python của `ai-service`) · M2 AI Trainer (4 mô hình) · M3 Frontend (Vue 3).
**Phạm vi demo:** Bán lẻ (tạp hóa) và FnB đầy đủ; Bida/dịch vụ chỉ làm proof-of-concept.

### 2.1. Hiện trạng sau Sprint 1 (ước lượng mức đủ dùng)
| Module | Mức |
|---|---|
| Auth, Health, Carts (giữ/lấy đơn) | 85–95% |
| Orders (tạo đơn: tính giá ở server, khóa dòng trừ kho, idempotency, VAT, làm tròn) | ~55% |
| CRM / Debts | ~40% |
| Catalog (chỉ tra barcode, list/search) | ~35% |
| Audit `PosLog`, Returns, Inventory, VietQR, Shifts | 15–30% |
| `ai-service` | ~10% (code LLM cũ, chưa có inference) |
| SignalR, Frontend, Test | 0% |

### 2.2. Bốn mô hình AI (M2 train, M1 bọc router, M3 làm màn hình)
| # | Mô hình | Chức năng cho người dùng | Kỹ thuật | Đánh giá |
|---|---|---|---|---|
| A1 | **Dự báo nhu cầu + thời tiết** | Cảnh báo nhập hàng trước bão / mưa kéo dài. Retail: số lượng đề xuất nhập. FnB: gợi ý đổi cơ cấu món. Service: PoC | LSTM/GRU theo nhóm sản phẩm; nhãn nhạy cảm thời tiết; job gọi API thời tiết hằng ngày | MAE/MAPE so với trung bình trượt và seasonal-naive; ablation có/không thời tiết |
| A2 | **NER hóa đơn nhập hàng** | Chụp hóa đơn → trích xuất → khớp sản phẩm → soát lại (tô màu ô độ tin cậy thấp) → nhập kho | Tesseract + BiLSTM-CRF + char-CNN; khớp bằng `pg_trgm`/`unaccent` | F1 theo thực thể; so với regex; có/không char-CNN |
| A3 | **Copilot** | Hỏi đáp số liệu tiếng Việt; từ chối câu ngoài phạm vi | TextCNN/BiLSTM phân loại ~10 nhãn + `out_of_scope` → SQL whitelist → template trả lời | Accuracy, macro-F1, confusion matrix |
| A4 | **Gợi ý bán chéo** | Gợi ý mua kèm khi quét sản phẩm, có fallback theo danh mục cho sản phẩm mới | Apriori/FP-Growth (baseline) + Item2Vec | hit-rate@k, precision@k |

**Lưu ý trung thực:** dữ liệu huấn luyện là mô phỏng có kiểm soát, và tín hiệu "sắp có bão" lấy từ API ngoài (AI chỉ dự báo nhu cầu). Go/no-go cho từng mô hình vào 25/10.

### 2.3. Chín nhóm nghiệp vụ — Đợt 1 (phủ rộng) và Đợt 2 (chiều sâu)
| Nhóm | Đợt 1 (bắt buộc) | Đợt 2 (khi có thêm nhân lực / thời gian) |
|---|---|---|
| Welcome / Dashboard | 4 KPI, biểu đồ 7 ngày, top 5 | — |
| Bán lẻ POS | Màn hình POS, list/chi tiết/in lại đơn, hủy đơn/món, sửa giá có phân quyền, trả hàng, ép ca mở, API đọc `PosLog` | Nhật ký POS lọc nâng cao, hoàn tiền nhiều hình thức |
| Danh mục bán hàng | CRUD khách, nhóm khách, NCC (entity `Supplier` riêng) | Bảng giá theo nhóm khách, `WholesalePrice` vào logic giá, loyalty tier, danh mục ca |
| Danh mục quản lý (15 mục) | CRUD sản phẩm (+ đơn vị quy đổi, barcode theo đơn vị), nhóm hàng, đơn vị, phụ phí | Kho, ngân hàng, nhóm thuế, chức vụ, phiếu in tem, thiết bị POS, 4 mục chưa rõ tên |
| Kho hàng | Sổ kho, tồn kho (`belowMin`), điều chỉnh, nhập kho thủ công + OCR, trigger chặn sửa/xóa ledger | Xuất kho, chuyển kho (đa kho), kiểm kê, trả NCC |
| Tài chính | Phiếu thu nợ, sổ công nợ khách | Phiếu chi, sổ quỹ, công nợ NCC |
| Hệ thống | API cấu hình cửa hàng (`BusinessType`, `Region`), CRUD nhân viên, SignalR | Phân quyền chi tiết theo chức vụ, thông báo nội bộ có lịch sử, refresh token, rate limit, audit `LOGIN_FAILED` |
| Reports | Doanh thu, tồn kho, công nợ | Cân đối ca, xuất file, lọc nâng cao |
| Trợ giúp | Trang hướng dẫn trong ứng dụng | FAQ theo màn hình |
| Ca làm việc | Mở/kết ca, `expectedCash`, `diff`, ép ca khi tạo đơn | — |
| VietQR | Sinh chuỗi EMVCo, `simulate`, đóng đơn khi paid | Poll thật / webhook ngân hàng |

### 2.4. Việc kỹ thuật phải xử lý
**Bug P0 (tuần đầu):** — 5/5 đã có bản sửa trong code (đối chiếu 01/11/2026):
1. ✅ `GlobalExceptionHandler` bắt `BusinessRuleException`/`StockShortageException` → 400 (`GlobalExceptionHandler.cs:30`).
2. ✅ Idempotency placeholder đặt **đầu transaction**, fill cùng commit — không còn lưu trước commit (`OrderService.cs:97,371`; composite unique `AppDbContext.cs:268`).
3. ✅ Đã thêm `FOR ROLE pos_user` — `ai_reader` đọc được bảng EF (`db/init/001_roles.sql:23`).
4. ✅ `POST /api/v1/debts/{id}/pay` — `Debt` không kẹt `Open` (`DebtsController.cs:73`, `DebtService.cs:8`).
5. ✅ FK `StockIn.SupplierInvoiceId` nullable + `SetNull` — hợp luồng OCR (`Entities.cs:319`, `AppDbContext.cs:231`).

**P1 / P2:** trigger chặn sửa/xóa `StockLedger` và `PosLog`, tìm kiếm bỏ dấu, ép ca, giới hạn `unitPrice` do client gửi, rate limit auth; trước khi nộp bài: tắt trả mã reset trong response, khóa CORS, đưa secret ra biến môi trường, sửa salt/so sánh hash.

**5 quyết định thiết kế lõi (chốt ở Sprint 2):** đa kho (`WarehouseId`, `WarehouseStock`) · phân quyền chi tiết · tách NCC khỏi `Customer` · nhóm thuế vs VAT · quy tắc chọn giá 4 cột.

### 2.5. Lịch Sprint
| Sprint | Thời gian | Trọng tâm |
|---|---|---|
| S2 | 28/09 – 11/10 | Vá P0, test `OrderService`, dữ liệu mô phỏng, A4, chốt JSON 4 endpoint AI, khung Vue + màn hình POS |
| S3 | 12/10 – 25/10 | Train A3 và A2, router Python thật, nhập kho OCR, CRUD sản phẩm, 4 màn hình AI. **Go/no-go AI 25/10** |
| S4 | 26/10 – 08/11 | A1, job thời tiết, đơn hàng / trả hàng / ca, audit API. Demo trọn 4 luồng AI |
| S5 | 09/11 – 22/11 | Phủ đủ 9 nhóm Đợt 1, đóng gói và đóng băng mô hình, bảng đánh giá cuối |
| S6 | 23/11 – 03/12 | Chốt nốt, bug bash, bảo mật, đo hiệu năng, **feature freeze 03/12** |

### 2.6. Sức chứa và rủi ro chính
| Người | Khối lượng | Sức chứa (48 ngày) | Nhận định |
|---|---|---|---|
| M1 Backend | ~59 (Đợt 1) / ~100 (tổng) | 48 | Đợt 1 vượt ~20%, tổng gấp ~2 lần |
| M3 Frontend | ~54 / ~82 | 48 | Đợt 1 vượt ~10%, tổng gấp ~1,7 lần |
| M2 AI Trainer | ~51 | 48 | Gần như không dư |

- Mỗi mảng chỉ có 1 người → điểm hỏng đơn lẻ; cả 3 phải chạy được hệ thống bằng `docker compose up`.
- Mô hình chưa vượt baseline ở 25/10 → dùng phương án dự phòng (LLM API để cứu demo, ghi rõ trong báo cáo).
- **Không được trượt:** 4 mô hình AI, test `OrderService`, Đợt 1 của mọi nhóm, POS, đơn hàng/trả hàng/ca, nhập kho OCR, hardening auth, bảo mật Sprint 6.
- Thứ tự rơi xuống nếu hết thời gian (từ dưới lên của bảng Đợt 2): FAQ → VietQR sâu → Reports sâu → Hệ thống sâu → Tài chính sâu → Danh mục quản lý còn lại → Kho sâu → Bảng giá theo nhóm khách.

---

## 3. Đối chiếu tmSales ↔ DigiSME

### 3.1. Điểm tmSales có thể lấp chỗ thiếu của roadmap
Roadmap ghi "4 mục danh mục quản lý chưa biết tên" và "nội dung Trợ giúp chưa biết". File khảo sát tmSales đã cung cấp:
- Danh mục quản lý thực tế có **16 mục** (danh sách ở mục 1.4). Các mục roadmap chưa gọi tên gồm: **Thành phần / Định lượng, Loại bảng giá, Ma trận điều chỉnh giá, Khoản thu, Nhóm chi phí, Từ điển sản phẩm**. Cần đối chiếu để xác định 4 mục còn thiếu là mục nào.
- Nội dung Trợ giúp: 7 chủ đề hướng dẫn (mục 1.9).

### 3.2. Chức năng tmSales mà roadmap chưa đề cập
| Chức năng tmSales | Ghi chú |
|---|---|
| Bảng tính lương, nhật ký thưởng - phạt | Roadmap Hệ thống chỉ có CRUD nhân viên |
| Hóa đơn điện tử qua SePay (tự xuất, hóa đơn nháp) | Roadmap chỉ có VietQR |
| Cấu hình mẫu in và khổ giấy in tem | Roadmap có "phiếu in tem" ở Đợt 2 nhưng chưa có mẫu in |
| Đơn hàng chờ (hàng đặt trước so với tồn) | Chưa có tương đương |
| Báo cáo & kê khai thuế | Roadmap chỉ có 3 báo cáo Đợt 1 |
| Trung tâm bảo trì | Chưa có tương đương |
| Phân hệ F&B đầy đủ (khu vực, bàn, bếp, tính giờ) | Roadmap nhắm demo FnB nhưng chưa liệt kê module bàn/khu vực |
| Đơn hàng mua với công nợ NCC | Có trong Đợt 2 (kho / tài chính) |

### 3.3. Phần chỉ có ở DigiSME
- 4 mô hình AI (dự báo nhu cầu theo thời tiết, NER hóa đơn, Copilot, bán chéo). tmSales mới có "từ điển sản phẩm" và cấu hình Gemini, chưa có tính năng AI tương đương.
- Kiến trúc API-first, idempotency, ledger kho append-only, kiểm thử tự động.

### 3.4. Lưu ý khi đọc dữ liệu từ file tmSales
- Dữ liệu là dữ liệu thật của một cửa hàng (ví dụ tồn kho âm, ca "Dang mo" từ 29/05/2026), nên chỉ nên dùng làm tham chiếu chức năng.
- Một số ô trong file khảo sát bị cắt ngắn hoặc gộp cột, vì vậy tên cột cần kiểm tra lại trên hệ thống thật trước khi chốt thiết kế.
- Tài khoản khảo sát `quanly@banle` không có mật khẩu; không nên đưa thông tin đăng nhập hay mã PIN nhân viên trong file vào tài liệu chia sẻ ngoài.
