# DigiSME POS — Backend & AI Service

Bộ backend cho hệ thống POS bán lẻ SME, gồm **2 service**:

| Service | Công nghệ | Port | Vai trò |
|---|---|---|---|
| `pos-api` | ASP.NET Core 8 (C#) | `8000` | Nghiệp vụ chính: auth, bán hàng, kho, nhập kho, trả hàng, công nợ, khách hàng, thu chi |
| `ai-service` | FastAPI (Python 3.11+) | `8001` | OCR hóa đơn nhập kho + Copilot hỏi đáp dữ liệu |
| PostgreSQL 16 | Docker | `5432` | Database chung `digism_pos` (21 bảng, 18 entity) |

> 📄 API contract chi tiết (request/response từng endpoint, các module): [`docs/api-contract.md`](docs/api-contract.md)

---

## 1. Yêu cầu máy

| Công cụ | Phiên bản | Kiểm tra |
|---|---|---|
| **Git for Windows** | bất kỳ | `git --version` |
| **Docker Desktop** | ≥ 24 | `docker --version` + `docker ps` |
| **.NET 8 SDK** | 8.0.x | `dotnet --version` → `8.0.xxx` |
| **Python** | 3.11+ (chỉ cần cho chế độ dev local) | `python --version` |

Cài .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0 (chọn **SDK**).
Docker Desktop: bật feature **"Use the WSL 2 based engine"** (Settings → General).

---

## 2. Chuẩn bị & Chạy Docker

```bash
# 1) Clone hoặc mở thư mục dự án
cd DigiSME_AI_Project

# 2) Bật Docker Desktop

# 3) Chạy toàn bộ stack bằng Docker Compose (khuyến nghị)
docker compose up --build -d
```

`docker compose up` làm 4 việc:
1. Đưa Postgres 16 lên (container `digism_postgres`, DB `digism_pos`, port 5432).
2. Chạy script SQL init tạo role (`db/init/001_roles.sql` — chạy lần đầu tạo volume).
3. Build + chạy `pos-api` (port 8000). **App tự chạy EF migration + seed data khi khởi động**.
4. Build + chạy `ai-service` (port 8001).

Kiểm tra:
```bash
docker compose ps
curl http://localhost:8000/api/v1/health
curl http://localhost:8001/health
```

> ⚠️ **Lưu ý quan trọng về Docker Down & Dữ liệu Database**:
> - Lệnh `docker compose down` dừng các container nhưng **giữ nguyên dữ liệu database** trong Docker volume.
> - **CẢNH BÁO:** Lệnh `docker compose down -v` sẽ **xóa sạch toàn bộ Docker volumes** và dữ liệu database vĩnh viễn. Chỉ dùng `-v` khi muốn reset hoàn toàn dữ liệu về trắng ban đầu.

---

## 3. Tài khoản Demo & Lưu ý Bảo mật

- **Tài khoản seed (dùng cho Development & Testing local):**
  - `admin` / `admin123` — role **Manager** (toàn quyền)
  - `cau1` / `cau11234` — role **Cashier** (thu ngân)
- ⚠️ **CẢNH BÁO BẢO MẬT:** Các tài khoản và mật khẩu mẫu trên **chỉ phục vụ mục đích phát triển và kiểm thử local**. Tuyệt đối **không** sử dụng các tài khoản hoặc mật khẩu mặc định này trên môi trường Production. Phải cấu hình lại mật khẩu mạnh và biến môi trường bảo mật trước khi triển khai thực tế.

---

## 4. Chế độ Dev (tuỳ chọn — debug với breakpoint)

```bash
# Terminal 1 — chạy Postgres
docker compose up postgres -d

# Terminal 2 — pos-api
cd pos-api/src/PosApi
dotnet run

# Terminal 3 — ai-service
cd ai-service
python -m venv .venv
.venv\Scripts\activate
pip install -e .
uvicorn app.main:app --host 127.0.0.1 --port 8001
```

> Tránh chạy đồng thời chế độ dev dotnet/python thủ công và docker compose đầy đủ trên cùng các port (5432/8000/8001) để tránh xung đột port.

---

## 5. Verification — Lệnh xác nhận

```bash
# 1) Đăng nhập lấy token
curl -X POST http://localhost:8000/api/v1/auth/login ^
  -H "Content-Type: application/json" ^
  -d "{\"username\":\"admin\",\"password\":\"admin123\"}"

# 2) Tra mã vạch (p99 < 50ms)
curl http://localhost:8000/api/v1/products/barcode/000000001

# 3) Health ready
curl http://localhost:8000/api/v1/health/ready
```

Swagger UI: `http://localhost:8000/swagger`

---

## 6. Tổng quan các Module & Trạng thái

Toàn bộ các module cốt lõi M1 đến M9 đã được triển khai và kiểm thử thành công:

| Module | Chức năng chính | Trạng thái |
|---|---|---|
| **M1. Auth** | Đăng ký, đăng nhập JWT, đổi mật khẩu, thông tin user | ✅ Hoàn thành |
| **M2. Health** | Liveness & Readiness check (DB & AI service) | ✅ Hoàn thành |
| **M3. Catalog / Products** | Tra cứu mã vạch tốc độ cao, tìm kiếm sản phẩm phân trang | ✅ Hoàn thành |
| **M4. Orders & Carts** | Tạo đơn hàng (tính giá server-side, idempotency, row locking), hold/resume giỏ hàng | ✅ Hoàn thành |
| **M5. Returns** | Trả hàng theo đơn, hoàn kho, ghi sổ ledger | ✅ Hoàn thành |
| **M6. Inventory** | Quản lý tồn kho, cảnh báo low-stock, xem lịch sử StockLedger, điều chỉnh kho | ✅ Hoàn thành |
| **M7. Stock-In & OCR** | Upload hóa đơn, OCR trích xuất, review items, xác nhận nhập kho (`FOR UPDATE`, idempotency) | ✅ Hoàn thành |
| **M8. CRM / Customers** | Tra cứu khách hàng theo SĐT (F2), tạo khách mới, tích điểm loyalty | ✅ Hoàn thành |
| **M9. Debts** | Quản lý công nợ khách hàng, thu nợ một phần hoặc toàn bộ (`FOR UPDATE` transaction) | ✅ Hoàn thành |
| **Shifts** | Quản lý ca làm việc (mở ca, xem ca hiện tại, đóng ca kiểm đếm tiền mặt, chênh lệch) | ✅ Hoàn thành |
| **M10. AI Service** | FastAPI OCR & Copilot hỏi đáp dữ liệu POS | 🟡 Mock / Sẵn sàng |

---

## 7. Cấu trúc thư mục

```
DigiSME_AI_Project/
├── docker-compose.yml          # Postgres + pos-api + ai-service
├── db/init/001_roles.sql       # Khởi tạo role DB (lần đầu)
├── docs/api-contract.md        # API contract chi tiết từng module
├── pos-api/
│   ├── src/PosApi/
│   │   ├── Program.cs          # Bootstrap: DI, JWT, auto-migrate + seed
│   │   ├── Domain/             # Entities (21 bảng)
│   │   ├── Data/               # AppDbContext & DbInitializer
│   │   ├── Controllers/        # Auth, Health, Products, Orders, Carts, Customers, Returns, Inventory, StockIns, Debts
│   │   ├── Dto/                # Request/response contracts
│   │   └── Services/           # OrderService, StockInService, DebtService, InventoryService, ReturnService...
│   └── tests/PosApi.Tests/     # Integration & Unit Tests (29/29 passed)
└── ai-service/
    ├── pyproject.toml          # FastAPI + uvicorn
    └── app/                    # OCR & Copilot routers
```

## 8. Tiến độ & Lộ trình Sprint

- **Sprint 1 — ✅ Hoàn thành** — Đặt hàng (`POST /orders` engine tính giá, idempotency, row locking, trừ kho atomic) · hold/resume cart · tra/tạo khách (F2) · audit log.
- **Sprint 2 — ✅ Hoàn thành** — Nhập kho M7 (`POST /api/v1/stock-ins`, OCR, confirm), Trả hàng M5, Quản lý kho M6, Công nợ M9.
- **Sprint 3 (Hoàn thành)** — Ca làm việc (`/api/v1/shifts`) ✅; Báo cáo nâng cao (`/api/v1/reports/*`) ✅; Thanh toán VietQR động (`/api/v1/payments/*`) ✅.
- **Sprint 4 (Hoàn thành)** — SignalR Realtime Notifications (`/hubs/pos`) ✅ — phát thông báo real-time sau khi transaction commit (Orders, Payments/VietQR, LowStock inventory alerts, Shifts).
