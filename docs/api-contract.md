# DigiSME POS — API Contract v1

> Tài liệu giao kèo giữa Backend (ASP.NET `pos-api` + FastAPI `ai-service`) và Frontend.
> Mọi endpoint trả về JSON UTF-8. Lỗi chung: `{"error": "thông điệp tiếng Việt"}` + status code.
> Auth: header `Authorization: Bearer <token>` (token từ `POST /api/v1/auth/login`).
> Tiền tệ: số nguyên VNĐ (decimal 18,2). Thời gian: ISO 8601 UTC.

**Trạng thái legend:** ✅ = đã implement và kiểm thử | 🟡 = contract chốt, implement theo roadmap

Base URL: `http://localhost:8000` (pos-api), `http://localhost:8001` (ai-service, Frontend gọi qua proxy `/ai/*`)

---

## M1. AUTH — ✅
| Method | Path | Auth | Mô tả | Response |
|---|---|---|---|---|
| POST | `/api/v1/auth/register` | Không | Tự đăng ký (mặc định role Cashier) | 201 `AuthResponse` |
| POST | `/api/v1/auth/login` | Không | Đăng nhập | 200 `AuthResponse` |
| POST | `/api/v1/auth/forgot-password` | Không | Cấp mã reset (trả thẳng trong response — demo) | 200 `{code, validMinutes}` |
| POST | `/api/v1/auth/reset-password` | Không | Đổi mật khẩu bằng mã | 200 / 400 |
| GET | `/api/v1/auth/me` | Bearer | Thông tin user hiện tại | 200 |

`AuthResponse`:
```json
{ "accessToken": "eyJ...", "username": "admin", "fullName": "Quản Lý", "role": "Manager", "expiresAtUnix": 1758280000 }
```
Role: `Cashier` (bán hàng, tạo đơn, trả hàng) | `Manager` (toàn quyền + báo cáo + quản lý danh mục/giá).
Tài khoản seed (dev/test): `admin/admin123` (Manager), `cau1/cau11234` (Cashier).

---

## M2. HEALTH — ✅
| Method | Path | Auth | Response |
|---|---|---|---|
| GET | `/api/v1/health` | Không | `{status:"ok", service, version, time}` |
| GET | `/api/v1/health/ready` | Không | `{status:"ready"|"degraded", database:bool, databaseError:string?, aiService:bool}` |

---

## M3. CATALOG / PRODUCTS — ✅
| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET | `/api/v1/products/barcode/{code}` | Không | **Tra mã vạch khi thu ngân scan** — p99 < 50ms, trả tên, giá, tồn, đơn vị, cảnh báo dưới định mức |
| GET | `/api/v1/products` | Không | Tìm kiếm sản phẩm (`?q=&categoryId=&page=&pageSize=`) |

Response mẫu `GET /api/v1/products/barcode/000000001`:
```json
{ "id": 1, "name": "Coca-Cola 330ml", "barcode": "000000001", "categoryId": 1, "categoryName": "Nước giải khát",
  "retailPrice": 4000, "wholesalePrice": 3400, "memberPrice": 3600, "stock": 120, "baseUnit": "lon", "belowMinStock": false, "isWeighed": false }
```

---

## M4. ORDERS & CARTS (Bán hàng & Giỏ hàng chờ) — ✅
| Method | Path | Role | Mô tả |
|---|---|---|---|
| POST | `/api/v1/orders` | Cashier | **Tạo đơn tại quầy** — transaction: trừ kho + ledger + điểm + nợ. Hỗ trợ header `Idempotency-Key` |
| POST | `/api/v1/carts/hold` | Cashier | Lưu snapshot giỏ hàng chờ (F7) |
| GET | `/api/v1/carts/hold` | Cashier | Danh sách giỏ hold của user hiện tại |
| POST | `/api/v1/carts/{id}/resume` | Cashier | Lấy giỏ hold về |
| DELETE | `/api/v1/carts/{id}` | Cashier | Hủy giỏ hold |

Body tạo đơn (`POST /api/v1/orders`):
```json
{ "customerId": null, "paymentMethod": "cash", "paidAmount": 100000,
  "discount": 0,
  "surcharges": [],
  "items": [{ "productId": 1, "unitId": null, "qty": 2, "unitPrice": 14373 }] }
```

Response 201:
```json
{ "id": 1, "code": "INV-20260924-0001", "subtotal": 28746, "discount": 0,
  "surchargeTotal": 0, "vat": 2613, "total": 28746, "paymentMethod": "cash",
  "paidAmount": 100000, "change": 71254, "status": "completed",
  "loyaltyEarned": 28, "completedAt": "2026-10-13T10:00:00Z" }
```

---

## M5. RETURNS (Trả hàng) — ✅
Tất cả các endpoint yêu cầu role `Manager` (`Authorization: Bearer <token>`).

| Method | Path | Mô tả |
|---|---|---|
| POST | `/api/v1/returns` | Tạo phiếu trả hàng (hoàn kho + ghi sổ ledger) |
| GET | `/api/v1/returns` | Lịch sử trả hàng (`?page=&pageSize=`) |
| GET | `/api/v1/returns/{id}` | Chi tiết phiếu trả hàng |

Body tạo phiếu trả (`POST /api/v1/returns`):
```json
{
  "orderId": 1,
  "reason": "Khách đổi trả do lỗi sản phẩm",
  "items": [
    { "orderItemId": 1, "qty": 1 }
  ]
}
```

---

## M6. INVENTORY (Quản lý kho) — ✅
| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET | `/api/v1/inventory` | AnyRole | Danh sách tồn kho (`?productId=&categoryId=&page=&pageSize=`) |
| GET | `/api/v1/inventory/low-stock` | AnyRole | Danh sách sản phẩm tồn kho thấp hơn định mức (`page`, `pageSize`) |
| GET | `/api/v1/inventory/{productId}` | AnyRole | Chi tiết tồn kho và lịch sử biến động (`StockLedger`) của sản phẩm |
| POST | `/api/v1/inventory/adjust` | ManagerOnly | Điều chỉnh kho thủ công (`?productId=...`, body: `InventoryAdjustRequest`) |

Body điều chỉnh kho (`POST /api/v1/inventory/adjust?productId=1`):
```json
{
  "deltaQty": 5,
  "note": "Kiểm kê định kỳ lệch tăng 5"
}
```

---

## M7. STOCK-IN & SUPPLIER INVOICES (Nhập kho & OCR) — ✅
Tất cả các endpoint yêu cầu role `Manager`.

| Method | Path | Mô tả |
|---|---|---|
| POST | `/api/v1/stock-ins` | Upload ảnh hóa đơn (`multipart/form-data`, file `file`) → OCR extract |
| GET | `/api/v1/stock-ins/{id}` | Xem chi tiết hóa đơn và kết quả OCR |
| PUT | `/api/v1/stock-ins/{id}/items` | Chỉnh sửa danh sách mặt hàng của hóa đơn pending |
| POST | `/api/v1/stock-ins/{id}/confirm` | Xác nhận nhập kho, khóa dòng (`FOR UPDATE`), cộng tồn kho + ledger (`StockLedgerEntry`), hỗ trợ header `X-Idempotency-Key` |
| DELETE | `/api/v1/stock-ins/{id}` | Từ chối hóa đơn (`Rejected`) |

---

## M8. CRM / CUSTOMERS (Khách hàng) — ✅
| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET | `/api/v1/customers/by-phone/{phone}` | AnyRole | **F2**: Tra khách theo SĐT (hiện nợ + điểm + tier) |
| POST | `/api/v1/customers` | AnyRole | Tạo khách mới + tài khoản loyalty (validate & chống trùng SĐT) |
| GET | `/api/v1/customers` | AnyRole | Tìm kiếm khách hàng (`?q=&page=&pageSize=`, kèm tổng nợ) |

---

## M9. DEBTS (Công nợ) — ✅
| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET | `/api/v1/debts` | AnyRole | Danh sách công nợ (`?customerId=&status=Open|PartiallyPaid|Settled&page=&pageSize=`) |
| GET | `/api/v1/debts/{id}` | AnyRole | Chi tiết một công nợ |
| POST | `/api/v1/debts/{id}/pay` | ManagerOnly | Thu công nợ (toàn bộ hoặc một phần, khóa dòng `FOR UPDATE`, ghi audit log) |

Body thu nợ (`POST /api/v1/debts/{id}/pay`):
```json
{
  "amount": 50000,
  "note": "Thu bớt tiền nợ mặt"
}
```

---

## Shifts (Quản lý ca làm việc) — ✅
| Method | Path | Role | Mô tả |
|---|---|---|---|
| POST | `/api/v1/shifts/open` | AnyRole | Mở ca làm việc mới (`openingCash`) |
| GET | `/api/v1/shifts/current` | AnyRole | Xem ca đang mở của user hiện tại |
| POST | `/api/v1/shifts/close` | AnyRole | Đóng ca + kiểm đếm tiền mặt (`countedCash`), tính tiền kỳ vọng và chênh lệch |
| GET | `/api/v1/shifts` | ManagerOnly | Lịch sử ca làm việc (`?page=&pageSize=`) |

---

## Payments & VietQR (Thanh toán QR động) — ✅
| Method | Path | Role | Mô tả |
|---|---|---|---|
| POST | `/api/v1/payments/vietqr` | AnyRole | Tạo mã VietQR động cho đơn hàng dựa trên thông tin cài đặt cửa hàng (`orderId`) |
| POST | `/api/v1/payments/{id}/confirm` | ManagerOnly | Xác nhận trạng thái giao dịch thanh toán (`status`, `reference`) |

---

## SignalR Realtime Notifications (Sprint 4) — ✅
- **Hub Endpoint:** `/hubs/pos`
- **Authentication:** JWT Bearer (truyền qua query string `?access_token=<token>` khi kết nối WebSocket).
- **Client Method lắng nghe:** `ReceiveNotification`
- **Cấu trúc payload thông báo:**
  ```json
  {
    "eventId": "uuid",
    "eventType": "OrderCreated | OrderCompleted | PaymentConfirmed | LowStockAlert | ShiftOpened | ShiftClosed",
    "occurredAtUtc": "2026-10-14T12:00:00Z",
    "entityId": "123",
    "message": "Nội dung thông báo tiếng Việt"
  }
  ```

---

## M10. AI SERVICE (FastAPI) — 🟡 (Mock ✅)
| Method | Path | Mô tả |
|---|---|---|
| POST | `/api/v1/ai/invoices/extract` | OCR trích xuất hóa đơn nhập kho |
| POST | `/api/v1/ai/copilot/ask` | Copilot hỏi đáp dữ liệu POS bằng tiếng Việt |

---

## Quy ước chung
1. **Paging:** `page` (1-based) + `pageSize` (max 100); response có `total`, `totalPages`.
2. **Enum trong JSON:** chuỗi chữ thường (`cash`, `completed`, `pending_review`, `confirmed`, `open`, `settled`).
3. **Mã nghiệp vụ:** `INV-...` (Đơn hàng), `PHNK-...` (Phiếu nhập kho), `PHTRA-...` (Phiếu trả hàng).
4. **Sai lệch giá/tiền:** Backend luôn tính lại toàn bộ.
5. **Timeout SLA:** Barcode lookup p99 < 50ms; Search p99 < 300ms; Order create p95 < 500ms.
