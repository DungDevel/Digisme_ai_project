# Gap Analysis — tmSales ↔ DigiSME (scope backend chốt)

> Lập ngày 04/11/2026. Nguồn: `tong_hop_tmSales_va_DigiSME.md` (29/10/2026), `adr/0001-separate-supplier-from-customer.md`, khảo sát code `pos-api` hiện tại.
> Trạng thái: **scope chốt** — thay thế mọi đề xuất cắt scope trước đó.

---

## 1. Quyết định scope (đã được xác nhận)

| Câu hỏi | Quyết định |
|---|---|
| Cắt chức năng nào? | **Không cắt.** Toàn bộ scope Đợt 1 + Đợt 2 được giữ. |
| Deadline thực | **03/12** (feature freeze theo roadmap). Mục tiêu tự đặt: hoàn thành tuần này (trước **08/11**). |
| 4 AI routers | **Xếp cuối hàng đợi**, phát triển lâu dài, kéo đến **30/11** — không ép vào deadline 08/11. |
| Tài nguyên | Máy chạy **24/7 liên tục** (không có ràng buộc giờ người). |

---

## 2. Scope backend cuối cùng

### 2.1. Bảng effort theo thứ tự thực thi

| # | Nhóm | Items | Effort | Chốt |
|---|---|---|---|---|
| 1 | **Đơn Bán Hàng chiều sâu** | VIP, dùng điểm, thu nợ cũ, hình thức giao | **3 ngày** (L×1) | 04/11 → 08/11 |
| 2 | **ADR 0001** | `Supplier`, `CustomerGroup`, 8 mục NCC/ĐC | **3 ngày** (M×1 + S×7) | 04/11 → 08/11 |
| 3 | **F&B** (phân hệ D) | Khu vực, Bàn, Phòng, Dịch vụ tính giờ, Định lượng | **5 ngày** (L×3 + M×2) | 04/11 → 08/11 |
| 4 | **Multi-warehouse** | `Warehouse`, `WarehouseStock`, Xuất kho, Chuyển kho, Kiểm kê, Cân đối kho, Trả NCC | **5 ngày** (L×2 + M×3 + S×2) | 04/11 → 08/11 |
| 5 | **Tài chính** | Phiếu thu/chi, Sổ quỹ, Sổ NK, Công nợ NCC | **4 ngày** (M×4) | 04/11 → 08/11 |
| 6 | **Reports Đợt 2** | Kê khai thuế, Dashboard nâng cao | **3 ngày** (M×2) | 04/11 → 08/11 |
| | **Tổng (trước 08/11)** | | **23 ngày** | |
| 7 | **4 AI routers** | A1 (L), A2 (M), A3 (M), A4 (M) | **4 ngày** (L×1 + M×3) | **08/11 → 30/11** (lâu dài) |

**Tổng scope: 27 ngày effort.**

### 2.2. Tính toán capacity — nói thẳng

- 23 ngày effort ÷ 4 ngày lịch (04/11 → 08/11) = **vượt 5.75×** ngay cả khi máy chạy 24/7 liên tục.
- **Không thể "hoàn thiện" nghĩa đen** trong 4 ngày lịch nếu hiểu effort theo công việc tuần tự.
- Cách xử lý (đã chốt bằng câu trả lời Q10):
  1. **Không cắt** — mọi item giữ nguyên.
  2. **Máy chạy cả ngày** — không dừng theo ca, không chờ người giữa các bước build/test/deploy.
  3. **Ưu tiên rơi xuống** = đúng thứ tự 1 → 6 trên: nếu đến 08/11 mà chưa hết, nhóm số thứ tự **cao** (Reports Đợt 2 → Tài chính → Multi-warehouse → F&B → ADR 0001 → Đơn Bán Hàng) là nhóm **trễ trước**, kéo vào khoảng đệm 08/11 → 30/11, **trước** khi chạm 4 AI routers.
  4. **03/12** là backstop cứng: mọi thứ phải về nhà trước feature freeze.
- **4 AI routers** là hàng đợi cuối: bắt đầu khi các nhóm 1–6 ổn định, hoặc song song từ 08/11; hoàn thành mục tiêu 30/11, chậm nhất trước 03/12.

### 2.3. Phụ thuộc giữa các nhóm

```
ADR 0001 (Supplier, CustomerGroup)
  └─→ unblock 8 mục NCC/ĐC (Trả NCC, Công nợ NCC, giá NCC...)
Multi-warehouse (Warehouse, WarehouseStock)
  └─→ Kiểm kê, Cân đối kho, Xuất kho, Chuyển kho, Trả NCC
Đơn Bán Hàng chiều sâu (VIP, điểm, nợ cũ, giao)
  └─→ dùng `Debt` có sẵn + `LoyaltyAccount` (CRM ~40% hiện tại)
Tài chính (Phiếu thu/chi, Sổ quỹ, Sổ NK, Công nợ NCC)
  └─→ đọc `Debt` (khách + NCC sau ADR 0001), ledger ca
```

Không có item nào trong 1→6 chặn item khác ngoài các mũi tên trên → **thứ tự 1 → 6 đã tối ưu** (ADR 0001 trước F&B/Finance để unblock, Multi-warehouse trước Finance vì Trả NCC cần `Warehouse`).

---

## 3. Chi tiết item theo nhóm

### 3.1. Đơn Bán Hàng chiều sâu (3 ngày — L×1)
Hiện trạng: Orders ~55% (tạo đơn, tính giá server, VAT, làm tròn, idempotency có).
- [ ] **Chiết khấu VIP**: áp theo `Customer.IsVip` / tier; giá sau CK ghi vào `OrderItem.Price` server-side.
- [ ] **Dùng điểm**: tiêu điểm `LoyaltyAccount` trong đơn → giảm tiền; ghi `PointTransaction`.
- [ ] **Thu nợ cũ**: nhận thanh toán kèm thu `Debt` (Open) của khách tại đơn — mở rộng `POST /debts/{id}/pay` vào luồng tạo đơn.
- [ ] **Hình thức giao**: `DeliveryMethod` (shop tự giao / đơn vị vận chuyển) + phí vận chuyển tính server.

### 3.2. ADR 0001 — `Supplier` + `CustomerGroup` (3 ngày — M×1 + S×7)
- [ ] Entity `Supplier` (tách khỏi `Customer` — ADR đã accepted, chưa implement).
- [ ] Entity `CustomerGroup` (nhóm khách, nhóm NCC; chiết khấu mặc định, nhóm cha).
- [ ] 8 mục NCC/ĐC: CRUD NCC, CRUD nhóm ĐC, cờ nhóm "Là NCC", bảng giá áp dụng theo ĐC, `SupplierInvoice` gắn `SupplierId` (thay `SupplierName` string), "Tính lại công nợ", màn hình NCC/ĐC list+chi tiết, migration EF.

### 3.3. F&B — phân hệ D (5 ngày — L×3 + M×2)
Hiện trạng: **0 trong code** (demo FnB là phạm vi demo bắt buộc theo roadmap §2).
- [ ] **Khu vực** (L): entity `Area` + CRUD.
- [ ] **Bàn** (L): entity `Table` (gắn `Area`, sức chứa, trạng thái trống/đang dùng/khóa).
- [ ] **Phòng** (L): entity `Room` (tính giờ theo phút/giờ, dịch vụ tính giờ).
- [ ] **Dịch vụ tính giờ** (M): bảng giá thời gian, tính phí theo số phút làm tròn.
- [ ] **Định lượng** (M): BOM nguyên liệu theo sản phẩm (tiêu hao khi bán) — nối với kho.

### 3.4. Multi-warehouse (5 ngày — L×2 + M×3 + S×2)
- [ ] **`Warehouse`** (L): entity đa kho (kho cửa hàng, kho vật tư — theo tmSales 1.4.7).
- [ ] **`WarehouseStock`** (L): tồn theo kho, ledger `StockLedger` thêm `WarehouseId`.
- [ ] **Xuất kho** (M): phiếu xuất, chốt/hủy chốt.
- [ ] **Chuyển kho** (M): phiếu kho đi → kho đến, đồng bộ 2 đầu.
- [ ] **Kiểm kê** (M): theo kho, chênh lệch điều chỉnh.
- [ ] **Cân đối kho** (S): tồn đầu → nhập → xuất → tồn cuối (+ doanh thu, lãi gộp).
- [ ] **Trả NCC** (S): phiếu trả, chốt/hủy — *phụ thuộc ADR 0001*.

### 3.5. Tài chính (4 ngày — M×4)
- [ ] **Phiếu thu / Phiếu chi** (M): gắn ĐC, hình thức TT, đơn hàng, phiên làm việc; chốt/hủy.
- [ ] **Sổ quỹ** (M): thu vào, chi ra, tồn quỹ sau giao dịch (tiền mặt, QR).
- [ ] **Sổ NK** (M): sổ nhật ký chung gom chứng từ (bán lẻ, thu, xuất kho).
- [ ] **Công nợ NCC** (M): nợ NCC tăng/giảm, dư sau giao dịch — *phụ thuộc ADR 0001*.

### 3.6. Reports Đợt 2 (3 ngày — M×2)
- [ ] **Kê khai thuế** (M): theo kỳ, chọn mẫu; tổng thuế bán ra/mua vào từ `OrderItem.Vat` + `SupplierInvoice`.
- [ ] **Dashboard nâng cao** (M): cơ cấu thanh toán, lợi nhuận gộp, top bán chạy/chậm, cảnh báo tồn lâu/dưới tồn tối thiểu, thu/chi chi tiết.

### 3.7. 4 AI routers (4 ngày — L×1 + M×3) — 08/11 → 30/11
Bọc router Python `ai-service` cho 4 mô hình (M2 train, M1 router, M3 màn hình):
- [ ] **A1** (L): Dự báo nhu cầu + thời tiết — job gọi API thời tiết hằng ngày, endpoint cảnh báo + số lượng đề xuất.
- [ ] **A2** (M): NER hóa đơn nhập — upload ảnh → trích xuất → khớp sản phẩm (`pg_trgm`/`unaccent`) → soát lại → nhập kho.
- [ ] **A3** (M): Copilot tiếng Việt — phân loại ~10 nhãn + `out_of_scope` → SQL whitelist → template trả lời.
- [ ] **A4** (M): Gợi ý bán chéo — Apriori/Item2Vec, fallback theo danh mục cho sản phẩm mới.

Tiêu chuẩn "xong" của từng router: endpoint trả 200 có payload thật (kể cả khi model chưa train — trả fallback + cờ `model_status: pending`), có test integration.

---

## 4. Tiêu chuẩn hoàn thành (mỗi item)

Theo vòng kiểm chứng chuẩn của repo (skill `digisme-ai-project`):
1. `dotnet build pos-api/src/PosApi/PosApi.csproj` — 0 lỗi.
2. `dotnet test pos-api/tests/PosApi.Tests/PosApi.Tests.csproj` — pass count ≥ baseline 38.
3. `docker compose build pos-api` + `docker compose up -d pos-api` (bắt buộc `background=true`, xác nhận uptime mới).
4. Probe endpoint thật bằng Python `urllib`: login → GET/POST endpoint đổi, **200 + payload thật** là vạch xong.
5. Query EF Core 8: tránh `GroupBy` trên navigation — dùng join + `ToListAsync()` + group client-side cho report window-bounded (kèm comment lý do). Query 400 runtime → thêm regression test trong cùng change.

---

## 5. Rủi ro & giả định

| Rủi ro | Xử lý |
|---|---|
| 23 ngày effort trong 4 ngày lịch — không đủ nếu tuần tự | Máy 24/7 + ưu tiên rơi xuống theo thứ tự 1 → 6; đệm 08/11 → 30/11 trước khi chạm AI. |
| ADR 0001 trượt → 8 mục NCC/ĐC kẹt | ADR 0001 ở #2, làm sớm nhất có thể sau Đơn Bán Hàng. |
| EF Core 8 translation failures (ReportService class) | Đã có pattern an toàn (mục 4); mọi query 400 → regression test. |
| Model AI chưa có inference (ai-service ~10%) | Router trả fallback + cờ status; không block demo. Go/no-go từng model giữ theo roadmap S3. |
| Dữ liệu F&B/Ban mới — migration EF | Mỗi entity mới = 1 migration, chạy `db/init` idempotent, không seed dữ liệu tmSales thật (chỉ lấy tham chiếu chức năng — lưu ý 1.3 của tổng hợp). |

---

## 6. Lệnh rút lui (đã HỦY — ghi lại để khỏi tranh luận sau này)

~~"Cắt 4 AI + ADR 0001, giữ Đơn Bán Hàng chiều sâu"~~ — **bị từ chối** (Q10). Toàn bộ scope giữ; 4 AI routers chỉ **dời lịch** sang 08/11 → 30/11, không cắt.
