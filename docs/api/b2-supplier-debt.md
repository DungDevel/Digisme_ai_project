# Tài liệu API: B2 — Ghi nợ nhà cung cấp (Supplier Debt)

## 1. Tổng quan
Phân hệ quản lý công nợ nhà cung cấp tự động sinh từ phiếu nhập kho (`StockIn`), phân bổ thanh toán từ phiếu chi (`CashVoucher`), giảm nợ từ trả hàng nhà cung cấp (`SupplierReturn`), tính tuổi nợ (Aging), và tính lại công nợ batch (Recalculate).

---

## 2. Endpoints

### 2.1. Tra cứu danh sách công nợ nhà cung cấp
- **Endpoint:** `GET /api/v1/supplier-debts`
- **Quyền:** Yêu cầu xác thực (`[Authorize]`)
- **Query Parameters:**
  - `supplierId` (int, optional): Lọc theo nhà cung cấp.
  - `status` (int / enum, optional): Lọc theo trạng thái công nợ (`0 = Open`, `1 = PartiallyPaid`, `2 = Settled`).
  - `page` (int, default: 1)
  - `pageSize` (int, default: 20)
- **Response Example (200 OK):**
  ```json
  {
    "page": 1,
    "pageSize": 20,
    "total": 1,
    "totalPages": 1,
    "items": [
      {
        "id": 1,
        "supplierId": 5,
        "supplierName": "Công ty TNHH ABC",
        "amount": 10000000,
        "paidAmount": 3000000,
        "remainingAmount": 7000000,
        "status": "PartiallyPaid",
        "referenceType": "StockIn",
        "referenceId": "PHNK-20261114-1234",
        "stockInId": 12,
        "dueDate": "2026-12-14T00:00:00Z",
        "note": "Công nợ nhập kho PHNK-20261114-1234",
        "createdAt": "2026-11-14T10:00:00Z"
      }
    ]
  }
  ```

### 2.2. Thanh toán trực tiếp công nợ NCC
- **Endpoint:** `POST /api/v1/supplier-debts/{id}/pay`
- **Quyền:** Yêu cầu xác thực (`[Authorize]`)
- **Request Body:**
  ```json
  {
    "amount": 2000000,
    "note": "Thanh toán chuyển khoản"
  }
  ```
- **Response Example (200 OK):**
  ```json
  {
    "supplierDebtId": 1,
    "amount": 10000000,
    "paidAmount": 5000000,
    "remainingAmount": 5000000,
    "status": "PartiallyPaid"
  }
  ```

### 2.3. Báo cáo tuổi nợ (Aging)
- **Endpoint:** `GET /api/v1/supplier-debts/aging`
- **Quyền:** Yêu cầu xác thực (`[Authorize]`)
- **Query Parameters:**
  - `supplierId` (int, optional)
- **Response Example (200 OK):**
  ```json
  [
    {
      "supplierId": 5,
      "supplierName": "Công ty TNHH ABC",
      "totalRemaining": 7000000,
      "aging": {
        "under30": 7000000,
        "between30And60": 0,
        "over60": 0,
        "overdue": 0
      }
    }
  ]
  ```

### 2.4. Tính lại công nợ (Recalculate Batch)
- **Endpoint:** `POST /api/v1/supplier-debts/recalculate`
- **Quyền:** Yêu cầu xác thực (`[Authorize]`)
- **Request Body:**
  ```json
  {
    "preview": true
  }
  ```
- **Response Example (200 OK):**
  ```json
  {
    "preview": true,
    "totalSuppliersChecked": 10,
    "discrepanciesFound": 0,
    "discrepancies": []
  }
  ```

---

## 3. Mã lỗi phổ biến (Error Responses)
- **400 Bad Request (`BusinessRuleException`):**
  - Tiền trả vượt số còn nợ.
  - Hủy phiếu nhập khi đã có phát sinh thanh toán phân bổ.
  - Xóa nhà cung cấp khi còn số dư công nợ.
  - Số tiền trả lúc nhập không hợp lệ (âm hoặc vượt tổng tiền).
- **404 Not Found (`KeyNotFoundException`):**
  - Không tìm thấy công nợ hoặc nhà cung cấp.
