# DigiSME AI POS System

Hệ thống Phần mềm Bán hàng (POS) tích hợp Trí tuệ Nhân tạo (AI) dành cho doanh nghiệp bán lẻ và F&B (SME).

## 🚀 Tính năng nổi bật theo Sprint
- **Sprint 1: Core POS & Catalog**
  - Quản lý sản phẩm, danh mục, mã vạch (EAN-13), định mức tồn kho (`MinStock`).
  - Quản lý khách hàng (VIP, tích điểm, tìm theo số điện thoại).
  - Động cơ bán hàng (`OrderService`): tính tiền, chiết khấu, VAT, phí giao hàng, chống đơn trùng (`Idempotency-Key`), trừ kho thời gian thực với `SELECT ... FOR UPDATE`.
  - Quản lý ca làm việc (Shift), thu ngân.
- **Sprint 2: Multi-Warehouse, F&B & Advanced Reports**
  - Quản lý nhiều kho (Multi-warehouse), chuyển kho (`WarehouseTransfer`), kiểm kho (`StockTake`), sổ cái kho (`StockLedger`).
  - F&B features: khu vực (Area), bàn (Table), phòng (Room), công thức định lượng sản phẩm (`ProductRecipe`).
  - Báo cáo tài chính, quỹ tiền mặt (`CashFundBook`), sổ cái (`GeneralJournal`), công nợ nhà cung cấp (`SupplierDebt`).
- **Sprint 3: AI Copilot & Advanced Integrations**
  - Tích hợp AI Service (FastAPI / Uvicorn) hỗ trợ tra cứu thông tin, phân tích doanh thu và trợ lý bán hàng thông minh.
  - Hỗ trợ đính kèm hình ảnh giao dịch, hóa đơn (`FileStorageService`).

## 🛠️ Tech Stack & Kiến trúc
- **Backend API:** ASP.NET Core 8.0 (Clean Architecture: Controllers, Services, DTOs, Domain Entities, Entity Framework Core 8).
- **Database:** PostgreSQL 16.
- **AI Service:** Python, FastAPI, Uvicorn, LangChain/OpenAI/Anthropic integration.
- **Environment:** Docker & Docker Compose đa container.

## 📋 Yêu cầu hệ thống
- Docker & Docker Compose (khuyến nghị RAM tối thiểu 4GB).
- .NET 8 SDK (nếu chạy local không qua Docker).
- Python 3.10+ (cho AI service hoặc script seed dữ liệu).

## 🐳 Hướng dẫn Chạy hệ thống bằng Docker
1. Clone repository về máy:
   ```bash
   git clone https://github.com/DungDevel/Digisme_ai_project.git
   cd digisme-ai
   ```
2. Khởi chạy toàn bộ hệ thống (PostgreSQL, POS API, AI Service) bằng Docker Compose:
   ```bash
   docker compose up -d --build
   ```
3. Truy cập tài liệu API (Swagger UI):
   - **[http://localhost:8000/swagger](http://localhost:8000/swagger)**

## 📂 Dữ liệu mẫu (Sample Database)
Dữ liệu mẫu (300 sản phẩm, 300 đơn hàng, khách hàng, kho, v.v.) được cung cấp sẵn tại thư mục `data/`:
- Xem hướng dẫn chi tiết và cách khôi phục tại [data/README.md](data/README.md).
- Mã checksum SHA256 được lưu tại [data/CHECKSUMS.txt](data/CHECKSUMS.txt).

## 🧪 Chạy Unit Tests
```bash
dotnet test pos-api/tests/PosApi.Tests/PosApi.Tests.csproj
```

## 📁 Cấu trúc Thư mục
```text
├── ai-service/          # Python FastAPI AI Service
├── pos-api/             # .NET 8 Backend API & Unit Tests
│   ├── src/PosApi/      # Controllers, Services, Domain, DTOs, Migrations
│   └── tests/           # XUnit Test Projects
├── data/                # Dữ liệu mẫu PostgreSQL & hướng dẫn restore
├── docs/                # Tài liệu kỹ thuật, kiến trúc & đặc tả tính năng
├── tools/               # Script seed dữ liệu tự động (seeder.py)
├── docker-compose.yml   # Cấu hình Docker multi-container
└── README.md
```

## ⚠️ Lưu ý phần cứng & thiết bị ngoại vi
- **Máy quét mã vạch (Barcode Scanner):** Hoạt động ở chế độ bàn phím ảo (HID) hoặc Serial Port.
- **QZ Tray:** Yêu cầu cài đặt QZ Tray khi in hóa đơn phần cứng qua máy in nhiệt (Thermal Printer).
- Dự án sử dụng dữ liệu giả lập cho mục đích phát triển và kiểm thử.
