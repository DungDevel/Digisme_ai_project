# DigiSME POS — Backend & AI Service

Bộ backend cho hệ thống POS bán lẻ SME, gồm **2 service**:

| Service | Công nghệ | Port | Vai trò |
|---|---|---|---|
| `pos-api` | ASP.NET Core 8 (C#) | `8000` | Nghiệp vụ chính: auth, bán hàng, kho, nhập kho, khách hàng, thu chi |
| `ai-service` | FastAPI (Python 3.11+) | `8001` | OCR hóa đơn nhập kho + Copilot hỏi đáp dữ liệu |
| PostgreSQL 16 | Docker | `5432` | Database chung `digism_pos` (21 bảng, 18 entity) |

> 📄 API contract chi tiết (request/response từng endpoint, các module): [`docs/api-contract.md`](docs/api-contract.md)

---

## 1. Yêu cầu máy

| Công cụ | Phiên bản | Kiểm tra |
|---|---|---|
| **Git for Windows** (hoặc clone bằng bất kỳ cách nào) | bất kỳ | `git --version` |
| **Docker Desktop** | ≥ 24 | `docker --version` + `docker ps` |
| **.NET 8 SDK** | 8.0.x | `dotnet --version` → `8.0.xxx` |
| **Python** | 3.11+ (chỉ cần cho chế độ dev local, KHÔNG cần nếu chạy full Docker) | `python --version` |

Cài .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0 (chọn **SDK**, không phải Runtime).
Docker Desktop: bật feature **"Use the WSL 2 based engine"** (Settings → General) để container chạy ổn định.

---

## 2. Chuẩn bị

```bash
# 1) Lấy code về (dùng repo/git, hoặc copy thư mục DigiSME_AI_Project sang máy bạn)
cd <thư-mục> chứa dự án

# 2) Bật Docker Desktop, chờ icon xanh

# 3) Chạy toàn bộ stack bằng Docker (khuyến nghị — đồng nhất mọi máy)
docker compose up --build -d
```

`docker compose up` làm đủ 4 việc:
1. Đưa Postgres 16 lên (container `digism_postgres`, DB `digism_pos`, port 5432).
2. Chạy script SQL init tạo role `pos_user` / `ai_reader` (`db/init/001_roles.sql` — chỉ chạy khi volume DB **lần đầu** tạo).
3. Build + chạy `pos-api` (port 8000). **App tự chạy EF migration + seed data khi khởi động** — bạn KHÔNG cần chạy `dotnet ef database update` riêng.
4. Build + chạy `ai-service` (port 8001).

Kiểm tra:

```bash
docker compose ps            # cả 3 service phải "healthy"/"running"
curl http://localhost:8000/api/v1/health      # pos-api
curl http://localhost:8001/health             # ai-service
```

Nếu `pos-api` chưa sẵn sàng ngay (nó chờ DB healthy + migration): đợi ~15–30 giây rồi curl lại. Xem log bằng `docker compose logs -f pos-api`.

---

## 3. Seed data (có sẵn sau lần đầu chạy)

- **User demo:**
  - `admin` / `admin123` — role **Manager** (toàn quyền)
  - `cau1` / `cau11234` — role **Cashier**
- **Danh mục:** 8 nhóm hàng (Nước giải khát, Bánh kẹo, ...).
- **Đơn vị:** cái, lon, lốc, thùng, chai, túi, hộp, gói.
- **Phụ phí:** VAT 10%, Phí đóng gói 5.000đ, Phụ thu Tết (giai hạn).
- **Sản phẩm:** 200 SP giả (barcode `000000001`…`000000200`), kèm stock + ledger entry ban đầu.
- **Khách demo:** 1 (Khách VIP Minh, SĐT 0901234567).

---

## 4. Chạy ở chế độ Dev (tuỳ chọn — để debug với breakpoint)

Dùng khi bạn muốn F5 vào code .NET / Python. Postgres vẫn chạy bằng Docker:

```bash
# Terminal 1 — chỉ chạy Postgres
docker compose up postgres -d

# Terminal 2 — pos-api (từ thư mục pos-api/src/PosApi)
cd pos-api/src/PosApi
dotnet run
# → tự migrate + seed, listen http://localhost:8000
#   (appsettings.json đã trỏ Host=localhost;Port=5432)

# Terminal 3 — ai-service (từ thư mục ai-service)
cd ai-service
python -m venv .venv
.venv\Scripts\activate
pip install -e .          # cài dependencies theo pyproject.toml
uvicorn app.main:app --host 127.0.0.1 --port 8001
```

> Chạy cả chế độ dev lẫn Docker cùng lúc sẽ **tranh port 5432/8000/8001** — chỉ chạy 1 chế độ, `docker compose down` trước khi chuyển.

---

## 5. Verification — Lệnh xác nhận mọi thứ chạy

```bash
# 1) Đăng nhập → lấy token
curl -X POST http://localhost:8000/api/v1/auth/login ^
  -H "Content-Type: application/json" ^
  -d "{\"username\":\"admin\",\"password\":\"admin123\"}"
# → { "accessToken": "eyJ...", "username": "admin", "role": "Manager", ... }

# 2) Tra mã vạch (endpoint quan trọng nhất POS — p99 < 50ms)
curl http://localhost:8000/api/v1/products/barcode/000000001
# → { "id": 1, "name": "Coca-Cola 330ml", "retailPrice": ..., "stock": ... }

# 3) Tìm kiếm sản phẩm (CHÚ Ý: phân biệt hoa/thường — gõ "Coca" không phải "coca")
curl "http://localhost:8000/api/v1/products?q=Coca&pageSize=5"

# 4) Health tổng thể
curl http://localhost:8000/api/v1/health/ready
# → { "status": "ready", "database": true, "aiService": true }

# 5) Tạo đơn bán hàng — cần token từ bước 1
curl -X POST http://localhost:8000/api/v1/orders ^
  -H "Authorization: Bearer ***" ^
  -H "Content-Type: application/json" ^
  -H "Idempotency-Key: a1b2c3d4" ^
  -d "{\"customerId\":null,\"paymentMethod\":\"cash\",\"paidAmount\":100000,\"discount\":0,\"surcharges\":[],\"items\":[{\"productId\":1,\"unitId\":null,\"qty\":2,\"unitPrice\":14373}]}"
# → { "id": 1, "code": "INV-20260924-0001", "total": 28746, "change": 71254, ... }
```

Cần token cho endpoint bảo vệ: thêm header `Authorization: Bearer <accessToken>`.

Swagger UI (pos-api): `http://localhost:8000/swagger` — dùng thử API trực tiếp bằng trình duyệt.
ai-service: `http://localhost:8001/docs`.

---

## 6. Chức năng các API

Trạng thái: ✅ = **đã implement** — chạy được ngay | 🟡 = contract đã chốt, implement theo sprint.

### pos-api (port 8000)

**M1. AUTH — ✅**
| Method | Path | Chức năng |
|---|---|---|
| POST | `/api/v1/auth/register` | Tự đăng ký, mặc định role Cashier |
| POST | `/api/v1/auth/login` | Đăng nhập → trả `accessToken` (JWT, 8h) |
| POST | `/api/v1/auth/forgot-password` | Cấp mã reset (Phase 0 trả thẳng trong response — demo) |
| POST | `/api/v1/auth/reset-password` | Đổi mật khẩu bằng mã (valid 30 phút) |
| GET | `/api/v1/auth/me` | Thông tin user hiện tại (cần token) |

Role: `Cashier` (bán hàng, tạo đơn, trả hàng) | `Manager` (+báo cáo, quản lý danh mục/giá).

**M2. HEALTH — ✅**
| Method | Path | Chức năng |
|---|---|---|
| GET | `/api/v1/health` | Trạng thái service, không cần auth |
| GET | `/api/v1/health/ready` | Kiểm tra DB + ai-service (dùng cho monitor/load balancer) |

**M3. CATALOG — ✅ endpoint mẫu & tra cứu**
| Method | Path | Chức năng |
|---|---|---|
| GET | `/api/v1/products/barcode/{code}` | **Tra mã vạch khi thu ngân scan** — p99 < 50ms, trả tên, giá lẻ/sỉ/thành viên, tồn, đơn vị gốc, cảnh báo dưới định mức |
| GET | `/api/v1/products?q=&categoryId=&page=&pageSize=` | Tìm kiếm tên/barcode + lọc nhóm + phân trang |
| 🟡 | `/api/v1/catalog/products|categories|surcharges` | CRUD quản trị (Manager only) |

**M4. ORDERS (bán hàng) — ✅**
| Method | Path | Chức năng |
|---|---|---|
| POST | `/api/v1/orders` | **Tạo đơn tại quầy**. Mọi giá trị được server **tính lại**, không tin client. Header `Idempotency-Key` (tùy chọn): retry cùng key (cùng user, 24h) → trả lại response lần chạy đầu, không tạo đơn mới |
| 🟡 | `GET /api/v1/orders?from=&to=&cashierId=&status=` | Lịch sử đơn (Manager) |

Engine tính giá (1 transaction, `SELECT ... FOR UPDATE` khóa dòng product trừ kho atomic khi order song song):
- **Giá tầng**: khách hàng hoạt động + `memberPrice > 0` → giá thành viên, không thì giá lẻ. Giá trong DB là **giá gom VAT** → cột `Vat` chỉ tách hiển thị.
- **Đơn vị**: `unitId` khác gốc → quy qty & unitPrice về đơn vị gốc theo `conversionRate`.
- **Chiết khấu / phụ phí**: `discount` chặn âm & chặn vượt subtotal; nhận phụ phí loại `Auto` đang trong giai hạn.
- **Thanh toán**: `cash` → `paid ≥ total`, trả `change`, đơn `completed`.
- **Mã đơn** `INV-yyyymmdd-nnnn` từ bảng `invoice_day_counter` (`INSERT ... ON CONFLICT ... RETURNING`, row lock).
- **Kho**: thiếu tồn → 400 `StockShortageException` (không tạo đơn).
- **Audit**: ghi `ORDER_CREATED` vào bảng log append-only.

**Hold / Resume giỏ hàng (F7) — ✅**
| Method | Path | Chức năng |
|---|---|---|
| POST | `/api/v1/carts/hold` | Lưu snapshot giỏ (items + subtotal) — **không** tạo đơn, **không** trừ kho |
| GET | `/api/v1/carts/hold` | Danh sách giỏ hold của user hiện tại |
| POST | `/api/v1/carts/{id}/resume` | Trả giỏ về + kiểm tra lại tồn kho |
| DELETE | `/api/v1/carts/{id}` | Hủy giỏ hold |

**M5. RETURNS (trả hàng) — 🟡**
- `POST /api/v1/returns` — trả hàng theo đơn.

**M6. INVENTORY (kho) — ✅**
- Tra cứu tồn kho, lịch sử biến động (`StockLedger` append-only).

**M7. STOCK-IN & SUPPLIER INVOICES (Nhập kho & OCR) — ✅**
| Method | Path | Chức năng |
|---|---|---|
| POST | `/api/v1/stock-ins` | Upload ảnh hóa đơn (`.jpg`, `.jpeg`, `.png`, `.pdf`) → OCR extract (mock/ai-service), lưu `SupplierInvoice` (`PendingReview`, Manager only) |
| GET | `/api/v1/stock-ins/{id}` | Xem chi tiết hóa đơn, ảnh và kết quả OCR |
| PUT | `/api/v1/stock-ins/{id}/items` | Chỉnh sửa danh sách mặt hàng của hóa đơn pending |
| POST | `/api/v1/stock-ins/{id}/confirm` | Xác nhận nhập kho, khóa dòng (`FOR UPDATE`), tạo mã phiếu `PHNK-yyyyMMdd-nnnn`, cộng tồn kho sản phẩm, ghi `StockLedger` (`RefType.Purchase`), cập nhật trạng thái hóa đơn `Confirmed`, ghi audit log `STOCK_IN_CONFIRMED`, hỗ trợ `X-Idempotency-Key` |
| DELETE | `/api/v1/stock-ins/{id}` | Từ chối hóa đơn (`Rejected`) |

**M8. CRM (khách hàng) — ✅**
| Method | Path | Chức năng |
|---|---|---|
| GET | `/api/v1/customers/by-phone/{phone}` | **Tra khách theo SĐT** (F2: hiện tổng nợ + điểm + tier) |
| POST | `/api/v1/customers` | Tạo khách mới + tài khoản loyalty |
| GET | `/api/v1/customers?q=&page=&pageSize=` | Tìm theo tên/SĐT + phân trang, kèm tổng nợ |

### ai-service (port 8001)
| Method | Path | Chức năng |
|---|---|---|
| POST | `/api/v1/ai/invoices/extract` | Nhận ảnh hóa đơn (multipart) → OCR trích items |
| POST | `/api/v1/ai/copilot/ask` | Copilot hỏi đáp tiếng Việt |

---

## 7. Lỗi thường gặp

| Triệu chứng | Nguyên nhân | Cách xử lý |
|---|---|---|
| `docker compose up` fail, port 5432/8000/8001 bị chiếm | Ứng dụng khác đang dùng port | Tắt app đó, hoặc sửa port mapping trong `docker-compose.yml` |
| `pos-api` crash, `Cannot write DateTime with Kind=Unspecified` | Code cũ tạo `DateTime` không gắn UTC | Đã fix trong repo hiện tại — pull code mới nhất |
| `dotnet build` fail với `access to ... is denied` | Process build server cũ giữ lock | `dotnet build-server shutdown` rồi build lại |
| Search không ra kết quả | **Case-sensitive**: `q=coca` ≠ `q=Coca` | Gõ đúng hoa/thường |
| `login` trả 401/400 | Sai username/password | Dùng tài khoản seed `admin/admin123` |
| Muốn **reset DB về seed ban đầu** | — | `docker compose down -v` (xoá volume) rồi `docker compose up --build -d` |

---

## 8. Cấu trúc thư mục

```
DigiSME_AI_Project/
├── docker-compose.yml          # Postgres + pos-api + ai-service
├── db/init/001_roles.sql       # Tạo role pos_user / ai_reader (chạy lần đầu)
├── docs/api-contract.md        # 📄 API contract chi tiết
├── pos-api/
│   ├── src/PosApi/
│   │   ├── Program.cs          # Bootstrap: DI, JWT, auto-migrate + seed
│   │   ├── Domain/             # Entities (21 bảng) — code-first
│   │   ├── Data/               # AppDbContext + DbInitializer (seed)
│   │   ├── Migrations/         # EF Core migrations
│   │   ├── Controllers/        # Auth, Health, Products, Orders, Carts, Customers, StockIns
│   │   ├── Dto/                # Request/response contracts
│   │   └── Services/           # TicketService, OrderService, CartService, StockInService, PosLogService
│   └── src/PosApi/Dockerfile
└── ai-service/
    ├── pyproject.toml          # FastAPI + uvicorn + psycopg (Python 3.11+)
    ├── app/
    │   ├── main.py             # FastAPI app, cors, routers
    │   └── routers/            # health, invoices (OCR), copilot, jobs
    └── Dockerfile
```

## 9. Tiến độ Sprint

- **Sprint 1 — ✅ HOÀN THÀNH** — Đặt hàng (`POST /orders` với engine tính giá, idempotency, row locking, trừ kho atomic) · hold/resume cart · tra/tạo khách (F2) · audit log.
- **Sprint 2 — ✅ M7 STOCK-IN HOÀN THÀNH** — Nhập kho & quản lý hóa đơn nhà cung cấp từ OCR (`POST /api/v1/stock-ins`, confirm, items update, reject, ledger purchase).
- **Sprint 3 (Tiếp theo)** — Báo cáo nâng cao, VietQR động, ca làm việc (shifts), SignalR realtime notifications.
