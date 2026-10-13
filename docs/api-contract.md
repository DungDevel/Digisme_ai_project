# DigiSME POS — API Contract v1

> Tài liệu giao kèo giữa Backend (ASP.NET `pos-api` + FastAPI `ai-service`) và Frontend.
> Mọi endpoint trả về JSON UTF-8. Lỗi chung: `{"error": "thông điệp tiếng Việt"}` + status code.
> Auth: header `Authorization: Bearer <token>` (token từ `POST /api/v1/auth/login`).
> Tiền tệ: số nguyên VNĐ (decimal 18,2). Thời gian: ISO 8601 UTC.

**Trạng thái legend:** ✅ = đã implement và kiểm thử | 🟡 = contract chốt, implement theo sprint

Base URL: `http://localhost:8000` (pos-api), `http://localhost:8001` (ai-service, Frontend gọi qua proxy `/ai/*`)

---

## M1. AUTH — ✅
| Method | Path | Mô tả | Response |
|---|---|---|---|
| POST | `/api/v1/auth/register` | Tự đăng ký (mặc định role Cashier) | 201 `AuthResponse` |
| POST | `/api/v1/auth/login` | Đăng nhập | 200 `AuthResponse` |
| POST | `/api/v1/auth/forgot-password` | Cấp mã reset (trả thẳng trong response — demo) | 200 `{code, validMinutes}` |
| POST | `/api/v1/auth/reset-password` | Đổi mật khẩu bằng mã | 200 / 400 |
| GET | `/api/v1/auth/me` | Thông tin user hiện tại (cần token) | 200 |

`AuthResponse`:
```json
{ "accessToken": "eyJ...", "username": "admin", "fullName": "Quản Lý", "role": "Manager", "expiresAtUnix": 1758280000 }
```
Role: `Cashier` (bán hàng, tạo đơn, trả hàng) | `Manager` (toàn quyền + báo cáo + quản lý danh mục/giá).
Tài khoản seed: `admin/admin123` (Manager), `cau1/cau11234` (Cashier).

---

## M2. HEALTH — ✅
| Method | Path | Auth | Response |
|---|---|---|---|
| GET | `/api/v1/health` | Không | `{status:"ok", service, version, time}` |
| GET | `/api/v1/health/ready` | Không | `{status:"ready"|"degraded", database:bool, aiService:bool}` |

---

## M3. CATALOG — ✅ (Tra cứu barcode & tìm kiếm)
| Method | Path | Mô tả |
|---|---|---|
| GET | `/api/v1/products/barcode/{code}` | **Tra mã vạch khi thu ngân scan** — p99 < 50ms, trả tên, giá lẻ/sỉ/thành viên, tồn, đơn vị gốc, cảnh báo dưới định mức |
| GET | `/api/v1/products?q=&categoryId=&page=&pageSize=` | Tìm kiếm tên/barcode + lọc nhóm + phân trang |
| 🟡 | `/api/v1/catalog/...` | CRUD quản trị danh mục và sản phẩm (Manager only) |

Response mẫu `GET /api/v1/products/barcode/000000001`:
```json
{ "id": 1, "name": "Coca-Cola 330ml", "barcode": "000000001", "categoryId": 1, "categoryName": "Nước giải khát",
  "retailPrice": 4000, "wholesalePrice": 3400, "memberPrice": 3600, "stock": 120, "baseUnit": "lon", "belowMinStock": false }
```

---

## M4. ORDERS (Bán hàng) — ✅
| Method | Path | Role | Mô tả |
|---|---|---|---|
| POST | `/api/v1/orders` | Cashier | **Tạo đơn tại quầy** — transaction: trừ kho + ledger + điểm + nợ. Hỗ trợ header `Idempotency-Key` |
| 🟡 | `GET /api/v1/orders` | Manager | Lịch sử đơn (`?from=&to=&cashierId=&status=`) |

Body tạo đơn:
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

### Hold / Resume cart (F7) — ✅
| Method | Path | Mô tả |
|---|---|---|
| POST | `/api/v1/carts/hold` | Lưu snapshot giỏ hàng |
| GET | `/api/v1/carts/hold` | Danh sách giỏ hold của user |
| POST | `/api/v1/carts/{id}/resume` | Lấy giỏ về |
| DELETE | `/api/v1/carts/{id}` | Hủy giỏ hold |

---

## M5. RETURNS (Trả hàng) — 🟡
- `POST /api/v1/returns` — trả hàng theo đơn.

---

## M6. INVENTORY (Kho & Lịch sử) — ✅
| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET | `/api/v1/inventory/stock` | Cashier | Tồn kho hiện tại (`?belowMin=true`) |
| GET | `/api/v1/inventory/ledger` | Manager | Lịch sử biến động kho (`StockLedger` append-only) |

---

## M7. STOCK-IN & SUPPLIER INVOICES (Nhập kho & OCR) — ✅
Tất cả các endpoint dưới đây yêu cầu role `Manager` (`Authorization: Bearer <token>`).

### 1. Upload ảnh hóa đơn (OCR)
- **POST** `/api/v1/stock-ins`
- **Content-Type**: `multipart/form-data`
- **Form Data**: `file` (file ảnh `.jpg`, `.jpeg`, `.png` hoặc `.pdf`)
- **Mô tả**: Lưu file vào thư mục `Uploads/`, gọi OCR engine (mock/ai-service) trích xuất thông tin, tạo `SupplierInvoice` với trạng thái `PendingReview` và ghi audit log `SUPPLIER_INVOICE_UPLOADED`.
- **Response 201 Created**: Trả về object `SupplierInvoice`.

### 2. Xem chi tiết hóa đơn
- **GET** `/api/v1/stock-ins/{id}`
- **Path Param**: `id` (int)
- **Mô tả**: Lấy thông tin chi tiết hóa đơn, kết quả OCR (`OcrResultJson`), độ tin cậy và liên kết phiếu nhập kho (`StockIn`) nếu đã xác nhận.
- **Response 200 OK**: Trả về object `SupplierInvoice`.

### 3. Chỉnh sửa mặt hàng hóa đơn
- **PUT** `/api/v1/stock-ins/{id}/items`
- **Path Param**: `id` (int)
- **Request Body**:
  ```json
  [
    { "productId": 1, "qty": 10, "unitCost": 15000 }
  ]
  ```
- **Mô tả**: Cho phép chủ hàng cập nhật lại danh sách mặt hàng khi OCR trích xuất chưa chính xác (chỉ áp dụng khi hóa đơn ở trạng thái `PendingReview`).
- **Response 200 OK**: Trả về `SupplierInvoice` đã cập nhật.

### 4. Xác nhận nhập kho
- **POST** `/api/v1/stock-ins/{id}/confirm`
- **Path Param**: `id` (int)
- **Header**: `X-Idempotency-Key` (string, tùy chọn — chống tạo phiếu trùng khi retry)
- **Request Body**:
  ```json
  {
    "items": [
      { "productId": 1, "qty": 10, "unitCost": 15000 }
    ],
    "supplierName": "Công ty TNHH ABC",
    "note": "Nhập hàng đợt tháng 10"
  }
  ```
- **Mô tả**:
  - Kiểm tra idempotency key qua bảng `IdempotencyRecords`.
  - Sử dụng transaction với chiến lược thực thi của EF Core và khóa dòng `FOR UPDATE` trên bảng `SupplierInvoices` và `Products` (sắp xếp theo ID để tránh deadlock).
  - Kiểm tra hóa đơn tồn tại và ở trạng thái `PendingReview`.
  - Tạo mã phiếu nhập `PHNK-yyyyMMdd-nnnn`.
  - Cộng tồn kho sản phẩm (`CurrentStock += qty`), ghi nhận vào `StockLedger` (`RefType.Purchase`, `RefId = stockInCode`).
  - Cập nhật trạng thái hóa đơn thành `Confirmed` và liên kết `StockInId`.
  - Ghi audit log `STOCK_IN_CONFIRMED`.
- **Response 201 Created**:
  ```json
  {
    "id": 1,
    "code": "PHNK-20261013-A1B2",
    "total": 150000,
    "createdAt": "2026-10-13T10:30:00Z"
  }
  ```

### 5. Từ chối hóa đơn
- **DELETE** `/api/v1/stock-ins/{id}`
- **Path Param**: `id` (int)
- **Mô tả**: Từ chối hóa đơn ở trạng thái `PendingReview`, chuyển trạng thái thành `Rejected`.
- **Response 200 OK**: `{"message": "Đã từ chối hóa đơn thành công"}`.

---

## M8. CRM / CUSTOMERS (Khách hàng) — ✅
| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET | `/api/v1/customers/by-phone/{phone}` | Cashier | **F2**: Tra khách theo SĐT (hiện nợ + điểm + tier) |
| POST | `/api/v1/customers` | Cashier | Tạo khách mới + tài khoản loyalty |
| GET | `/api/v1/customers?q=&page=&pageSize=` | Cashier | Tìm kiếm khách hàng |

---

## M9. AI SERVICE (FastAPI) — 🟡 (Mock ✅)
| Method | Path | Mô tả |
|---|---|---|
| POST | `/api/v1/ai/invoices/extract` | OCR trích xuất hóa đơn |
| POST | `/api/v1/ai/copilot/ask` | Copilot hỏi đáp dữ liệu POS |

---

## Quy ước chung
1. **Paging:** `page` (1-based) + `pageSize` (max 100); response có `total`, `totalPages`.
2. **Enum trong JSON:** chuỗi chữ thường (`cash`, `completed`, `pending_review`, `confirmed`).
3. **Mã nghiệp vụ:** `INV-...` (Đơn hàng), `PHNK-...` (Phiếu nhập kho).
4. **Sai lệch giá/tiền:** Backend luôn tính lại toàn bộ.
5. **Timeout SLA:** Barcode lookup p99 < 50ms; Search p99 < 300ms; Order create p95 < 500ms.
