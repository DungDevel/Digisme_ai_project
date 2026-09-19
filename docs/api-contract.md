# DigiSME POS — API Contract v1 (Phase 0)

> Tài liệu giao kèo giữa Backend (ASP.NET `pos-api` + FastAPI `ai-service`) và Frontend.
> Mọi endpoint trả về JSON UTF-8. Lỗi chung: `{"error": "thông điệp tiếng Việt"}` + status code.
> Auth: header `Authorization: Bearer <token>` (token từ `POST /api/v1/auth/login`).
> Tiền tệ: số nguyên VNĐ (decimal 18,2). Thời gian: ISO 8601 UTC.

**Trạng thái legend:** ✅ = phase 0 đã implement | 🟡 = contract chốt, implement theo sprint | ⬜ = định nghĩa trước, có thể chỉnh trước sprint

Base URL: `http://localhost:8000` (pos-api), `http://localhost:8001` (ai-service, Frontend gọi qua proxy `/ai/*`)

---

## M1. AUTH — ✅ phase 0

| Method | Path | Mô tả | Response |
|---|---|---|---|
| POST | `/api/v1/auth/register` | Tự đăng ký (mặc định role Cashier) | 201 `AuthResponse` |
| POST | `/api/v1/auth/login` | Đăng nhập | 200 `AuthResponse` |
| POST | `/api/v1/auth/forgot-password` | Cấp mã reset (phase 0 trả thẳng trong response — demo) | 200 `{code, validMinutes}` |
| POST | `/api/v1/auth/reset-password` | Đổi mật khẩu bằng mã | 200 / 400 |
| GET | `/api/v1/auth/me` | Thông tin user hiện tại (cần token) | 200 |

`AuthResponse`:
```json
{ "accessToken": "eyJ...", "username": "admin", "fullName": "Quản Lý", "role": "Manager", "expiresAtUnix": 1758280000 }
```
Role: `Cashier` (bán hàng, tạo đơn, trả hàng) | `Manager` (toàn quyền + báo cáo + quản lý danh mục/giá).

**Tài khoản seed phase 0:** `admin/admin123` (Manager), `cau1/cau11234` (Cashier).

---

## M2. HEALTH — ✅ phase 0

| Method | Path | Auth | Response |
|---|---|---|---|
| GET | `/api/v1/health` | Không | `{status:"ok", service, version, time}` |
| GET | `/api/v1/health/ready` | Không | `{status:"ready"|"degraded", database:bool, aiService:bool}` |

AI service: `GET :8001/health` cùng shape.

---

## M3. CATALOG (Module 2 dự án) — 🟡 Sprint 2 (lookup mẫu ✅)

### ✅ Endpoint mẫu phase 0

**GET `/api/v1/products/barcode/{code}`** — *tra mã vạch, yêu cầu p99 < 50ms*
```json
{ "id": 1, "name": "Coca-Cola 330ml", "barcode": "000000001", "categoryId": 1, "categoryName": "Nước giải khát",
  "retailPrice": 4000, "wholesalePrice": 3400, "memberPrice": 3600, "stock": 120, "baseUnit": "lon", "belowMinStock": false }
```
404: `{"error":"Không tìm thấy sản phẩm với mã '000' "}`

**GET `/api/v1/products?q=&categoryId=&page=1&pageSize=20`** — *tìm kiếm + phân trang*
```json
{ "page": 1, "pageSize": 20, "total": 35, "totalPages": 2,
  "items": [{ "id": 1, "name": "...", "barcode": "000000001", "categoryName": "...", "retailPrice": 4000, "stock": 120, "baseUnit": "lon" }] }
```

### 🟡 CRUD quản trị (Manager only, Sprint 2)

| Method | Path | Mô tả |
|---|---|---|
| GET | `/api/v1/catalog/products` | Danh sách quản trị (lọc, sort, phân trang — khác endpoint bán hàng) |
| POST | `/api/v1/catalog/products` | Tạo sản phẩm (kèm units quy đổi) |
| PUT | `/api/v1/catalog/products/{id}` | Sửa (giá, tồn định mức, units) |
| DELETE | `/api/v1/catalog/products/{id}` | Deactivate (soft delete, `isActive=false`) |
| GET/POST/PUT | `/api/v1/catalog/categories` | CRUD nhóm hàng |
| GET/POST/PUT | `/api/v1/catalog/surcharges` | CRUD phụ phí PhiPos: `{name, type:"percent"|"fixed", value, applyMode:"auto"|"manual", activeFrom?, activeUntil?}` |

Body tạo sản phẩm:
```json
{ "name": "Coca 330ml", "categoryId": 1, "barcode": "893...", "costPrice": 2800,
  "retailPrice": 4000, "wholesalePrice": 3400, "memberPrice": 3600, "minStock": 12,
  "units": [{ "name": "lon", "isBase": true, "conversionRate": 1 },
            { "name": "lốc", "isBase": false, "conversionRate": 6 },
            { "name": "thùng", "isBase": false, "conversionRate": 24 }] }
```

---

## M4. ORDERS — bán hàng (Module 1 + 3) — 🟡 Sprint 1

| Method | Path | Role | Mô tả |
|---|---|---|---|
| POST | `/api/v1/orders` | Cashier | **Tạo đơn tại quầy (F9)** — transaction: trừ kho + ledger + điểm + nợ |
| GET | `/api/v1/orders` | Manager | Lịch sử đơn: `?from=&to=&cashierId=&customerId=&status=&page=` |
| GET | `/api/v1/orders/{id}` | Cashier | Chi tiết đơn (kèm items) |
| POST | `/api/v1/orders/{id}/reprint` | Cashier | In lại hóa đơn (ghi PosLog `REPRINT`) |
| POST | `/api/v1/orders/{id}/cancel` | Cashier | Hủy đơn (ghi PosLog `ORDER_CANCELED`), trả lại kho |
| POST | `/api/v1/orders/items/{itemId}/cancel` | Cashier | Hủy món (ghi PosLog `ITEM_CANCELED`) |
| PUT | `/api/v1/orders/items/{itemId}/price` | Cashier | Sửa giá (ghi PosLog `PRICE_OVERRIDDEN`) |

Body tạo đơn:
```json
{ "customerId": null, "paymentMethod": "cash", "paidAmount": 100000,
  "discount": 0,
  "surcharges": [{ "surchargeId": 1, "overrideValue": null }],
  "items": [{ "productId": 1, "unitId": null, "qty": 3, "unitPrice": null }] }
```
- `unitId=null` → đơn vị gốc; khác gốc thì `qty` quy về gốc bằng conversion.
- `unitPrice=null` → hệ thống tính theo giá tầng (khách member → `memberPrice`, ...).
- Tiền thừa `change` được **backend tính lại** — không tin giá trị từ client.

Response 201:
```json
{ "id": 42, "code": "INV-20260920-0042", "subtotal": 56000, "discount": 0,
  "surchargeTotal": 0, "vat": 0, "total": 56000, "paymentMethod": "cash",
  "paidAmount": 100000, "change": 44000, "status": "completed",
  "loyaltyEarned": 56, "completedAt": "2026-09-20T09:30:00Z" }
```

### Hold / Resume cart (F7) — 🟡 Sprint 1

| Method | Path | Mô tả |
|---|---|---|
| POST | `/api/v1/carts/hold` | `{customerId?, note?, items:[{productId, unitId, qty, unitPrice}]}` → 201 `{id}` |
| GET | `/api/v1/carts/hold` | Danh sách đơn chờ (của thu ngân hiện tại) |
| POST | `/api/v1/carts/{id}/resume` | Lấy giỏ về (merge), đánh dấu resumed |
| DELETE | `/api/v1/carts/{id}` | Bỏ đơn chờ |

---

## M5. RETURNS (Module 3) — 🟡 Sprint 2

| Method | Path | Mô tả |
|---|---|---|
| POST | `/api/v1/returns` | `{orderId, reason, items:[{orderItemId, qty}]}` → 201 `{code:"PHTRA-20261010-0001", totalReturned}` |
| GET | `/api/v1/returns?from=&to=&page=` | Lịch sử trả hàng (Manager) |
| GET | `/api/v1/returns/{id}` | Chi tiết |

Validate: `qty + returnedQty cũ ≤ qty đơn gốc`; hoàn theo đơn giá tại thời điểm bán; tự cộng kho + ledger.

---

## M6. INVENTORY / STOCK LEDGER (Module 4) — 🟡 Sprint 2

| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET | `/api/v1/inventory/ledger` | Manager | `?productId=&from=&to=&page=` — lịch sử biến động |
| GET | `/api/v1/inventory/stock` | Cashier | Tồn kho hiện tại (filter `belowMin=true` cho cảnh báo) |
| POST | `/api/v1/inventory/adjust` | Manager | Điều chỉnh kho thủ công `{productId, deltaQty, note}` — ghi ledger + PosLog |
| GET | `/api/v1/inventory/replenishment` | Manager | Gợi ý nhập hàng theo sales velocity (Sprint 3) |

Entry ledger:
```json
{ "id": 1024, "productId": 1, "productName": "Coca-Cola 330ml", "qty": -3, "balance": 117,
  "refType": "sale", "refId": "INV-20260920-0042", "note": null, "createdAt": "..." }
```

### Stock-in từ OCR — 🟡 Sprint 3

| Method | Path | Mô tả |
|---|---|---|
| POST | `/api/v1/stock-ins` | Nhận ảnh hóa đơn (multipart) → upload ai-service extract → lưu `supplier_invoice` status `pending_review` |
| GET | `/api/v1/stock-ins/{id}` | Ảnh + dữ liệu OCR đã trích (để UI side-by-side) |
| PUT | `/api/v1/stock-ins/{id}/items` | Chủ hàng chỉnh sửa items sau khi AI lệch |
| POST | `/api/v1/stock-ins/{id}/confirm` | Xác nhận → tạo phiếu nhập `PHNK-...`, cộng kho + ledger |
| DELETE | `/api/v1/stock-ins/{id}` | Từ chối (chưa confirm) |

---

## M7. CRM / CUSTOMERS (Module 5) — 🟡 Sprint 2-3

| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET | `/api/v1/customers/by-phone/{phone}` | Cashier | **F2**: `{customer, totalDebt, points, tier}` — hiện nhanh khi gõ SĐT |
| GET | `/api/v1/customers?q=&page=` | Cashier | Tìm khách |
| POST | `/api/v1/customers` | Cashier | Tạo khách + loyalty account |
| PUT | `/api/v1/customers/{id}` | Cashier | Sửa |
| GET | `/api/v1/customers/{id}/debts` | Manager | Sổ nợ chi tiết |
| POST | `/api/v1/debts/{id}/pay` | Cashier | Phiếu thu nợ `{amount, method}` |
| GET | `/api/v1/loyalty/summary` | Manager | Thống kê điểm/tier (Sprint 3) |

---

## M8. PAYMENTS / VIETQR (Module 5) — 🟡 Sprint 2

| Method | Path | Role | Mô tả |
|---|---|---|---|
| POST | `/api/v1/orders/{id}/payments/vietqr` | Cashier | Sinh QR động → `{paymentId, qrValue, amount, expiresAt, content}` |
| GET | `/api/v1/payments/{id}/status` | Cashier | **Frontend poll 3s** khi QR hiển thị: `{status:"pending"|"paid"|"expired"}` |
| POST | `/api/v1/payments/simulate/{id}` | Manager | **Mock bank** (demo): giả lập khách đã chuyển → đóng đơn |

`qrValue` = chuỗi EMVCo chuẩn (Frontend vẽ QR từ chuỗi này bằng `qrcode` lib). Khi `paid`: pos-api tự động đóng đơn + trừ điểm/nợ + SignalR notify.

---

## M9. SHIFT / END-OF-SHIFT (Module 1.5) — 🟡 Sprint 2

| Method | Path | Role | Mô tả |
|---|---|---|---|
| POST | `/api/v1/shifts` | Cashier | Mở ca `{openingCash}` — bắt buộc có ca mở mới tạo đơn |
| POST | `/api/v1/shifts/{id}/close` | Cashier | Kết ca `{countedCash}` → `{expectedCash, diff}` (báo cáo chênh lệch) |
| GET | `/api/v1/shifts?status=open` | Cashier | Ca đang mở |
| GET | `/api/v1/shifts/reports?from=&to=` | Manager | Báo cáo đối soát tiền mặt |

---

## M10. AUDIT LOG (Module 3.3) — 🟡 từ Sprint 1 (bắt đầu ghi), Sprint 2 (API)

| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET | `/api/v1/audit/logs` | Manager | `?action=&from=&to=&userId=&entityType=&page=` |
| GET | `/api/v1/audit/logs/{id}` | Manager | Chi tiết |

Action codes: `ITEM_CANCELED`, `ORDER_CANCELED`, `PRICE_OVERRIDDEN`, `ORDER_DELETED`, `DRAWER_OPENED_NO_SALE`, `REPRINT`, `STOCK_ADJUST`, `RETURN_CREATED`, `LOGIN_FAILED`.
Bảng **append-only** — DB trigger chặn UPDATE/DELETE.

---

## M11. AI SERVICE (FastAPI, Module 4.2 + 5.3) — 🟡 Sprint 3 (mock ✅)

Frontend gọi qua `/ai/*` (proxy). Internal token do pos-api thêm, Frontend không thấy.

| Method | Path | Mô tả |
|---|---|---|
| POST | `/api/v1/ai/invoices/extract` | Multipart `file` → OCR: `{invoiceId, items:[{name,unit,qty,unitPrice,confidence}], overallConfidence, engine, reviewRequired}` |
| POST | `/api/v1/ai/copilot/ask` | `{question, conversationId?}` → `{answer, data[], chartType, function}` — 4 câu hỏi mẫu tài liệu |

Copilot functions (Sprint 3, nội bộ): `get_revenue`, `top_products`, `cancellations_by_cashier`, `stock_below_min`, `shift_summary`, `debt_summary`, `sales_velocity`, `category_breakdown`.

Internal (pos-api → ai-service, token nội bộ): `POST /api/v1/ai/jobs/notify/stock-low`, `POST /api/v1/ai/jobs/ping`.

---

## M12. NOTIFICATIONS / REALTIME (Module 6) — 🟡 Sprint 2

SignalR hub: `ws://host/hub/notifications` (kèm token).

Client subscribe channel: `stock-low`, `order-hold`, `vietqr-paid`, `shift-closed`, `system`.
Server push shape:
```json
{ "channel": "stock-low", "title": "Sắp hết hàng",
  "body": "Coca-Cola 330ml còn 8 lon (định mức 12)", "entity": { "type": "product", "id": 1 }, "at": "..." }
```

---

## Quy ước chung

1. **Paging:** `page` (1-based) + `pageSize` (max 100); response luôn có `total`, `totalPages`.
2. **Enum trong JSON:** chuỗi chữ thường (`cash`, `completed`, `percent`).
3. **Id:** số nguyên tăng; mã nghiệp vụ là chuỗi (`INV-...`, `PHTRA-...`, `PHNK-...`).
4. **Sai lệch giá/tiền:** backend luôn tính lại; client chỉ gửi giá trị đề xuất.
5. **Timeout SLA:** barcode lookup p99 < 50ms; search p99 < 300ms; order create p95 < 500ms (ngoại trừ OCR/Copilot: LLM timeout 10s, trả fallback).
6. **Rate limit** (Phase 4): `/ai/*` 10 req/min/user; auth 5 fail/5min → 429.
