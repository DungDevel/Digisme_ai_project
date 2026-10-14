# DigiSME POS — Realtime Dashboard & SignalR Client

Ứng dụng React + TypeScript + Vite kết nối SignalR Hub (`/hubs/pos`) của `pos-api` (ASP.NET Core 8) để hiển thị dòng sự kiện bán hàng, thanh toán VietQR, quản lý ca làm việc và cảnh báo tồn kho theo thời gian thực (Sprint 4).

## 🚀 Tính năng chính

1. **Xác thực JWT (`POST /api/v1/auth/login`)**:
   - Hỗ trợ tài khoản seed: `admin` / `admin123` (Manager) và `cau1` / `cau11234` (Cashier).
   - Tự động đính kèm token vào `accessTokenFactory` của SignalR Hub.
2. **SignalR Realtime Hub (`/hubs/pos`)**:
   - Lắng nghe sự kiện `ReceiveNotification`.
   - Hiển thị thông báo tức thì cho các sự kiện: `OrderCreated`, `OrderCompleted`, `PaymentConfirmed`, `LowStockAlert`, `ShiftOpened`, `ShiftClosed`.
   - Trạng thái kết nối real-time (`Connected`, `Connecting`, `Reconnecting`, `Disconnected`).
3. **Công cụ nghiệp vụ & Chẩn đoán**:
   - Kiểm tra trạng thái hệ thống (`/api/v1/health/ready`).
   - Kiểm tra ca làm việc đang mở (`/api/v1/shifts/current`).
   - Tra cứu mã vạch sản phẩm tốc độ cao (`/api/v1/products/barcode/{code}`).

## 🛠️ Hướng dẫn chạy local

```bash
# 1. Cài đặt dependencies
npm install

# 2. Chạy Vite dev server (port 3000, proxy /api và /hubs sang backend port 8000)
npm run dev
```

Truy cập: `http://localhost:3000`
