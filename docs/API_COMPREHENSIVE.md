# DigiSME AI POS — Tài liệu Tổng hợp API Toàn hệ thống (Groups 1–6)

> Tài liệu chính thức ghi nhận toàn bộ các API Backend ASP.NET Core 8 (`pos-api`, port 8000) đã được hiện thực hóa và kiểm thử thành công (133/133+ unit tests).
> Định dạng request/response: JSON UTF-8. Lỗi chung: `{"error": "thông điệp tiếng Việt"}` kèm HTTP Status Code.
> Xác thực: Header `Authorization: Bearer <token>` (lấy từ `POST /api/v1/auth/login`).
> Quy ước tiền tệ: Số nguyên VNĐ (làm tròn số nguyên đồng bằng `Math.Round(v, 0, MidpointRounding.AwayFromZero)`).

---

## 1. Phân hệ Auth & Health (Cơ sở)
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| POST | `/api/v1/auth/register` | Không | Đăng ký tài khoản (mặc định Cashier) |
| POST | `/api/v1/auth/login` | Không | Đăng nhập hệ thống (trả về JWT token) |
| POST | `/api/v1/auth/forgot-password` | Không | Cấp mã reset mật khẩu |
| POST | `/api/v1/auth/reset-password` | Không | Đổi mật khẩu mới bằng mã reset |
| GET | `/api/v1/auth/me` | Bearer | Lấy thông tin user hiện tại |
| GET | `/api/v1/health` | Không | Kiểm tra trạng thái hệ thống |
| GET | `/api/v1/health/ready` | Không | Kiểm tra sẵn sàng (Database, AI service) |

---

## 2. Phân hệ Catalog & Products (Sản phẩm & Danh mục)
| Method | Path | Auth | Mô tả |
|---|---|---|---|
| GET | `/api/v1/products` | Bearer | Tìm kiếm, phân trang sản phẩm (`?q=&categoryId=&page=&pageSize=`) |
| GET | `/api/v1/products/barcode/{code}` | Bearer | Tra cứu sản phẩm bằng mã vạch khi quét (p99 < 50ms) |

---

## 3. Phân hệ Orders & Carts (Đơn bán hàng & Giỏ hàng chờ)
| Method | Path | Role | Mô tả |
|---|---|---|---|
| POST | `/api/v1/orders` | Cashier/Manager | Tạo đơn bán hàng tại quầy (trừ kho, ghi sổ ledger, tích/tiêu điểm loyalty, thu nợ cũ, chiết khấu VIP, giao hàng). Hỗ trợ `Idempotency-Key` |
| POST | `/api/v1/carts/hold` | Cashier/Manager | Lưu snapshot giỏ hàng chờ (F7) |
| GET | `/api/v1/carts/hold` | Cashier/Manager | Danh sách giỏ chờ của user |
| POST | `/api/v1/carts/{id}/resume` | Cashier/Manager | Phục hồi giỏ chờ |
| DELETE | `/api/v1/carts/{id}` | Cashier/Manager | Hủy giỏ chờ |

---

## 4. Phân hệ Returns (Trả hàng)
| Method | Path | Role | Mô tả |
|---|---|---|---|
| POST | `/api/v1/returns` | Manager | Lập phiếu trả hàng, hoàn kho, hoàn tiền, thu hồi điểm loyalty tương ứng (`Idempotency-Key` hỗ trợ) |
| GET | `/api/v1/returns` | Manager | Danh sách lịch sử trả hàng (`?page=&pageSize=`) |
| GET | `/api/v1/returns/{id}` | Manager | Chi tiết phiếu trả hàng |

---

## 5. Phân hệ CRM & Debts (Khách hàng & Công nợ khách)
| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET/POST | `/api/v1/customers` | Bearer | CRUD khách hàng, điểm loyalty, hạng thành viên, tổng nợ |
| GET/POST | `/api/v1/debts` | Manager | Quản lý công nợ khách hàng |
| POST | `/api/v1/debts/{id}/pay` | Manager | Thanh toán/thu nợ khách hàng |

---

## 6. Phân hệ Inventory & Stock-In (Kho & Nhập kho)
| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET | `/api/v1/inventory` | Manager | Danh sách tồn kho sản phẩm |
| GET | `/api/v1/inventory/low-stock` | Manager | Danh sách sản phẩm dưới định mức tồn tối thiểu |
| GET | `/api/v1/inventory/{productId}/history` | Manager | Lịch sử xuất/nhập/điều chỉnh kho (Stock Ledger) |
| POST | `/api/v1/inventory/{productId}/adjust` | Manager | Điều chỉnh kho thủ công (khóa dòng `FOR UPDATE`) |
| POST | `/api/v1/stock-ins/confirm` | Manager | Xác nhận nhập kho từ nhà cung cấp (`Idempotency-Key`, `RequestHash`) |
| POST | `/api/v1/stock-ins/upload` | Manager | Upload hóa đơn OCR nhập kho |

---

## 7. Phân hệ Shifts & Payments / VietQR (Ca làm việc & Thanh toán QR)
| Method | Path | Role | Mô tả |
|---|---|---|---|
| POST | `/api/v1/shifts/open` | Cashier/Manager | Mở ca làm việc (tiền mặt ban đầu) |
| POST | `/api/v1/shifts/close` | Cashier/Manager | Chốt ca làm việc (kiểm kê tiền mặt, tính chênh lệch) |
| POST | `/api/v1/payments/vietqr` | Cashier/Manager | Tạo mã VietQR động QuickLink cho đơn hàng |
| POST | `/api/v1/payments/{id}/confirm` | Cashier/Manager | Xác nhận thanh toán điện tử / chuyển khoản |

---

## 8. Phân hệ ADR 0001 — Supplier & CustomerGroup (Nhà cung cấp & Nhóm)
| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET/POST/PUT/DELETE | `/api/v1/suppliers` | Manager | Quản lý danh mục nhà cung cấp (tách bạch hoàn toàn khỏi khách hàng) |
| GET/POST/PUT/DELETE | `/api/v1/customer-groups` | Manager | Quản lý nhóm khách hàng và nhóm nhà cung cấp (phân cấp cha-con, chiết khấu mặc định) |

---

## 9. Phân hệ F&B — Phân hệ D (Nhà hàng & Quán cafe)
| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET/POST/PUT/DELETE | `/api/v1/areas` | Manager | Quản lý khu vực (tầng, phòng, khu vực sân vườn) |
| GET/POST/PUT/DELETE | `/api/v1/tables` | Cashier/Manager | Quản lý bàn (gắn khu vực, trạng thái trống/đang dùng, chuyển bàn, gộp bàn) |
| GET/POST/PUT/DELETE | `/api/v1/rooms` | Cashier/Manager | Quản lý phòng hát / phòng VIP tính giờ (check-in, check-out tính tiền theo giờ) |
| GET/POST/PUT | `/api/v1/products/{id}/recipe` | Manager | Định lượng BOM nguyên liệu cho sản phẩm (tự động trừ kho nguyên liệu khi bán) |

---

## 10. Phân hệ Multi-warehouse (Đa kho)
| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET/POST/PUT/DELETE | `/api/v1/warehouses` | Manager | Quản lý danh sách kho (kho tổng, kho cửa hàng, kho chi nhánh) |
| GET | `/api/v1/warehouse-stocks` | Manager | Tồn kho chi tiết theo từng kho |
| GET/POST | `/api/v1/warehouse-transfers` | Manager | Lập và xác nhận phiếu chuyển kho giữa các kho (đồng bộ 2 đầu) |
| GET/POST | `/api/v1/stock-takes` | Manager | Lập và chốt phiếu kiểm kê kho, tự động tạo bút toán điều chỉnh chênh lệch |
| GET/POST | `/api/v1/supplier-returns` | Manager | Lập và xác nhận phiếu trả hàng nhà cung cấp gắn kho và NCC |

---

## 11. Phân hệ Tài chính (Finance & Sổ quỹ)
| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET/POST | `/api/v1/cash-vouchers` | Manager | Lập và duyệt Phiếu thu (`PT`) / Phiếu chi (`PC`), gắn phiên làm việc & đối tác |
| GET | `/api/v1/cash-fund-book` | Manager | Xem sổ quỹ tiền mặt / chuyển khoản (Số dư đầu kỳ, tổng thu, tổng chi, số dư cuối kỳ) |
| GET | `/api/v1/general-journal` | Manager | Sổ nhật ký chung tổng hợp toàn bộ chứng từ tài chính, bán hàng, xuất kho theo thời gian |
| GET/POST | `/api/v1/supplier-debts` | Manager | Quản lý công nợ nhà cung cấp và thực hiện thanh toán nợ NCC (`/api/v1/supplier-debts/{id}/pay`) |

---

## 12. Phân hệ Reports Đợt 2 (Báo cáo nâng cao, Thuế & Sổ quỹ)
| Method | Path | Role | Mô tả |
|---|---|---|---|
| GET | `/api/v1/reports/sales` | Manager | Báo cáo tổng quan doanh số theo kỳ |
| GET | `/api/v1/reports/top-products` | Manager | Báo cáo top sản phẩm bán chạy theo doanh thu |
| GET | `/api/v1/reports/payment-methods` | Manager | Báo cáo doanh thu theo phương thức thanh toán |
| GET | `/api/v1/reports/tax-declaration` | Manager | **Kê khai thuế:** Tổng doanh thu, thuế GTGT đầu ra và tổng giá trị mua vào, thuế GTGT đầu vào |
| GET | `/api/v1/reports/advanced-dashboard` | Manager | **Dashboard nâng cao:** Doanh thu, Giá vốn (COGS), Lãi gộp, Tỷ suất lợi nhuận gộp (%), tồn kho dưới định mức, số dư quỹ |
| GET | `/api/v1/reports/slow-moving` | Manager | **Hàng tồn lâu / bán chậm:** Liệt kê sản phẩm tồn kho nhưng không phát sinh bán trong kỳ |
| GET | `/api/v1/reports/cash-flow` | Manager | **Sổ quỹ & Dòng tiền:** Tổng hợp số dư, luồng thu chi và danh sách chi tiết phiếu thu/chi trong kỳ |
