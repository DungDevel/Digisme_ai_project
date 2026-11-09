# Dữ liệu mẫu Cơ sở dữ liệu DigiSME AI POS (`digism_pos`)

Thư mục này chứa cơ sở dữ liệu mẫu PostgreSQL phục vụ cho việc phát triển, kiểm thử và chạy thử nghiệm hệ thống **DigiSME AI POS**.

## 1. Nguồn gốc & Phương pháp tạo
- **Công cụ:** Script Python tự động sinh dữ liệu `tools/seed/seeder.py` kết hợp với cấu hình `tools/seed/seed.config.json`.
- **Random Seed:** `20261109` (đảm bảo tính nhất quán và lặp lại của dữ liệu).
- **Khoảng thời gian dữ liệu:** 09/05/2025 – 09/11/2026.

## 2. Thống kê khối lượng dữ liệu (Small_Test Volume)
- **Users:** 12 tài khoản (gồm các phân quyền Admin, Manager, Accountant, Cashier)
- **Categories:** 13 danh mục hàng hóa
- **Products:** 300 sản phẩm (có mã vạch EAN-13, giá vốn, giá bán sỉ/lẻ/thành viên)
- **Customers:** 101 khách hàng (có VIP, số điện thoại định dạng `09xxxxxxxx`)
- **Suppliers:** 5 nhà cung cấp
- **Warehouses:** 1 kho hàng mặc định
- **Orders:** 300 hóa đơn bán hàng (tổng doanh thu ~48.1 triệu VND)
- **OrderItems:** 868 chi tiết hóa đơn
- **StockLedger:** 1,068 bản ghi sổ cái kho
- **PosLogs:** 300 nhật ký hoạt động POS

## 3. Tính an toàn và Dữ liệu giả lập
- **100% Dữ liệu giả (Fake Data):** Không chứa bất kỳ thông tin cá nhân hay dữ liệu thực tế nào. Số điện thoại tuân thủ dải giả lập, email sử dụng miền `example.com`.
- **Mật khẩu test:** Toàn bộ tài khoản có mật khẩu chung là `hashed_password_123` (được băm chuẩn PBKDF2-HMAC-SHA256 với salt `DigiSME.pos.api.salt.v1`).
- **Bảo mật cấu hình:** Không chứa bất kỳ API key, secret, token hay thông tin nhạy cảm thật nào.

## 4. Kiểm tra toàn vẹn (Checksum)
- **Tên file:** `digism_pos_sample.dump` (PostgreSQL Custom Format Archive)
- **Kích thước:** ~234 KB
- **Mã SHA256:** `68a6b497be984089e6469d98527b65499215d5bf98e6a7f54bed12b62213b99e` (xem chi tiết tại `CHECKSUMS.txt`).

## 5. Hướng dẫn Khôi phục (Restore)
Bạn có thể khôi phục dữ liệu vào cơ sở dữ liệu PostgreSQL cục bộ hoặc trong Docker bằng lệnh `pg_restore`:

```bash
# 1. Tạo database mới (nếu chưa có)
createdb -U postgres digism_pos

# 2. Khôi phục từ file dump
pg_restore -U postgres -d digism_pos --clean --if-exists data/digism_pos_sample.dump
```

Hoặc nếu chạy qua Docker container `digism_postgres`:
```bash
docker cp data/digism_pos_sample.dump digism_postgres:/tmp/
docker exec -it digism_postgres pg_restore -U postgres -d digism_pos --clean --if-exists /tmp/digism_pos_sample.dump
```

## 6. Tài khoản đăng nhập thử nghiệm
- **Admin:** `admin_test` / `hashed_password_123`
- **Store Manager:** `manager_test` / `hashed_password_123`
- **Accountant:** `accountant_test` / `hashed_password_123`
- **Cashier:** `cashier_test` / `hashed_password_123`

> **Cảnh báo:** Dữ liệu này chỉ dùng cho mục đích phát triển, kiểm thử (Development/Testing) và giáo dục. Tuyệt đối không sử dụng cho môi trường Production.
