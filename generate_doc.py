import docx
from docx.shared import Inches, Pt, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

def create_api_doc():
    doc = docx.Document()

    # Page setup - Margins
    sections = doc.sections
    for section in sections:
        section.top_margin = Inches(1.0)
        section.bottom_margin = Inches(1.0)
        section.left_margin = Inches(1.0)
        section.right_margin = Inches(1.0)

    # Title
    title_p = doc.add_paragraph()
    title_p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    title_run = title_p.add_run("TÀI LIỆU HƯỚNG DẪN CHI TIẾT API — DIGISME POS v1")
    title_run.font.name = "Arial"
    title_run.font.size = Pt(20)
    title_run.font.bold = True
    title_run.font.color.rgb = RGBColor(0, 51, 102)

    sub_p = doc.add_paragraph()
    sub_p.alignment = WD_ALIGN_PARAGRAPH.CENTER
    sub_run = sub_p.add_run("Bản hướng dẫn toàn diện, giải thích nghiệp vụ, cú pháp request/response và khắc phục các vấn đề thường gặp")
    sub_run.font.name = "Arial"
    sub_run.font.size = Pt(11)
    sub_run.font.italic = True
    sub_run.font.color.rgb = RGBColor(100, 100, 100)

    doc.add_paragraph() # Spacing

    def add_heading_1(text):
        p = doc.add_paragraph()
        run = p.add_run(text)
        run.font.name = "Arial"
        run.font.size = Pt(14)
        run.font.bold = True
        run.font.color.rgb = RGBColor(0, 51, 102)
        p.paragraph_format.space_before = Pt(16)
        p.paragraph_format.space_after = Pt(6)
        return p

    def add_heading_2(text):
        p = doc.add_paragraph()
        run = p.add_run(text)
        run.font.name = "Arial"
        run.font.size = Pt(12)
        run.font.bold = True
        run.font.color.rgb = RGBColor(51, 102, 153)
        p.paragraph_format.space_before = Pt(12)
        p.paragraph_format.space_after = Pt(4)
        return p

    def add_body(text, bold_prefix=None):
        p = doc.add_paragraph()
        p.paragraph_format.space_after = Pt(4)
        p.paragraph_format.line_spacing = 1.15
        if bold_prefix:
            r_bold = p.add_run(bold_prefix)
            r_bold.font.name = "Arial"
            r_bold.font.size = Pt(10.5)
            r_bold.font.bold = True
        r_text = p.add_run(text)
        r_text.font.name = "Arial"
        r_text.font.size = Pt(10.5)
        return p

    def add_code_block(code_text):
        p = doc.add_paragraph()
        p.paragraph_format.space_before = Pt(4)
        p.paragraph_format.space_after = Pt(8)
        p.paragraph_format.left_indent = Inches(0.3)
        run = p.add_run(code_text)
        run.font.name = "Consolas"
        run.font.size = Pt(9.5)
        run.font.color.rgb = RGBColor(50, 50, 50)
        return p

    # Introduction & General Rules
    add_heading_1("1. QUY ƯỚC CHUNG & CẤU TRÚC API")
    add_body("Tài liệu này cung cấp hướng dẫn đầy đủ về hệ thống API của DigiSME POS, bao gồm backend ASP.NET Core (pos-api) và FastAPI AI Service.")
    add_body("• Base URL Backend POS: http://localhost:8000", "• ")
    add_body("• Base URL AI Service: http://localhost:8001 (Frontend gọi qua proxy /ai/*)", "• ")
    add_body("• Định dạng dữ liệu: JSON UTF-8 cho tất cả Request và Response.", "• ")
    add_body("• Xác thực (Authentication): Gửi kèm header Authorization: Bearer <token> (lấy từ POST /api/v1/auth/login).", "• ")
    add_body("• Tiền tệ: Số nguyên VNĐ (làm tròn số nguyên đồng bằng chuẩn toán học).", "• ")
    add_body("• Mã lỗi chung: Trả về HTTP Status Code (400, 401, 403, 404, 500) kèm body {'error': 'thông điệp tiếng Việt'}.", "• ")

    # M1. AUTH
    add_heading_1("2. M1. AUTH — XÁC THỰC & PHÂN QUYỀN")
    add_body("Quản lý tài khoản người dùng, đăng nhập và phân quyền (Cashier / Manager).")

    add_heading_2("POST /api/v1/auth/login — Đăng nhập hệ thống")
    add_body("• Mô tả: Xác thực tài khoản thu ngân hoặc quản lý, trả về JWT Token và thông tin user.")
    add_body("• Request Body:")
    add_code_block('{\n  "username": "admin",\n  "password": "admin123"\n}')
    add_body("• Response 200 OK:")
    add_code_block('{\n  "accessToken": "eyJhbGciOiJIUzI1NiIs...",\n  "username": "admin",\n  "fullName": "Quản Lý Hệ Thống",\n  "role": "Manager",\n  "expiresAtUnix": 1758280000\n}')

    add_heading_2("GET /api/v1/auth/me — Lấy thông tin user hiện tại")
    add_body("• Mô tả: Yêu cầu Header Authorization. Trả về thông tin chi tiết của user đang đăng nhập.")

    # M3. CATALOG / PRODUCTS
    add_heading_1("3. M3. CATALOG & PRODUCTS — SẢN PHẨM & MÃ VẠCH")
    add_heading_2("GET /api/v1/products/barcode/{code} — Tra cứu nhanh mã vạch (Barcode)")
    add_body("• Mô tả: Dùng khi thu ngân quét mã vạch sản phẩm tại quầy. Tối ưu tốc độ phản hồi (p99 < 50ms).")
    add_body("• Response 200 OK:")
    add_code_block('{\n  "id": 1,\n  "name": "Coca-Cola 330ml",\n  "barcode": "000000001",\n  "categoryId": 1,\n  "categoryName": "Nước giải khát",\n  "retailPrice": 4000,\n  "wholesalePrice": 3400,\n  "memberPrice": 3600,\n  "stock": 120,\n  "baseUnit": "lon",\n  "belowMinStock": false,\n  "isWeighed": false\n}')

    add_heading_2("GET /api/v1/products — Tìm kiếm sản phẩm")
    add_body("• Mô tả: Tìm kiếm sản phẩm theo từ khóa, danh mục, hỗ trợ phân trang.")
    add_body("• Query Parameters: q (từ khóa), categoryId, page (mặc định 1), pageSize (mặc định 20).")

    # M4. ORDERS
    add_heading_1("4. M4. ORDERS & CARTS — BÁN HÀNG & GIỎ HÀNG CHỜ")
    add_heading_2("POST /api/v1/orders — Tạo đơn hàng tại quầy")
    add_body("• Mô tả: Xử lý giao dịch bán hàng, tự động trừ tồn kho, ghi sổ cái (StockLedger), tích điểm khách hàng và ghi nhận công nợ (nếu bán thiếu). Hỗ trợ header Idempotency-Key chống trùng đơn.")
    add_body("• Request Body:")
    add_code_block('{\n  "customerId": 1,\n  "paymentMethod": "cash",\n  "paidAmount": 100000,\n  "discount": 0,\n  "surcharges": [],\n  "items": [\n    { "productId": 1, "unitId": null, "qty": 2, "unitPrice": 4000 }\n  ]\n}')
    add_body("• Response 201 Created:")
    add_code_block('{\n  "id": 1,\n  "code": "INV-20261107-0001",\n  "subtotal": 8000,\n  "discount": 0,\n  "surchargeTotal": 0,\n  "vat": 800,\n  "total": 8800,\n  "paymentMethod": "cash",\n  "paidAmount": 100000,\n  "change": 91200,\n  "status": "completed",\n  "loyaltyEarned": 8,\n  "completedAt": "2026-11-07T10:00:00Z"\n}')

    add_heading_2("GET /api/v1/orders/{id} — Chi tiết đơn hàng (Lưu ý về orderItemId)")
    add_body("• Mô tả: Lấy chi tiết đơn hàng theo ID. Lưu ý quan trọng: Danh sách items trả về bắt buộc phải có trường id (orderItemId) để phục vụ nghiệp vụ trả hàng (Returns).")

    # M5. RETURNS
    add_heading_1("5. M5. RETURNS — TRẢ HÀNG & HOÀN KHO")
    add_heading_2("POST /api/v1/returns — Tạo phiếu trả hàng")
    add_body("• Mô tả: Cho phép khách trả lại hàng đã mua. Yêu cầu quyền Manager. Hoàn lại tồn kho và ghi sổ cái.")
    add_body("• Request Body:")
    add_code_block('{\n  "orderId": 1,\n  "reason": "Khách đổi trả do lỗi sản phẩm",\n  "items": [\n    { "orderItemId": 1, "qty": 1 }\n  ]\n}')

    # M6. INVENTORY
    add_heading_1("6. M6. INVENTORY — QUẢN LÝ TỒN KHO")
    add_heading_2("GET /api/v1/inventory — Danh sách tồn kho")
    add_body("• Mô tả: Tra cứu số lượng tồn kho hiện tại của tất cả sản phẩm, hỗ trợ lọc theo productId, categoryId và phân trang.")
    add_heading_2("POST /api/v1/inventory/adjust — Điều chỉnh kho thủ công")
    add_body("• Mô tả: Dành cho Manager điều chỉnh tăng/giảm tồn kho khi kiểm kê định kỳ.")
    add_code_block('{\n  "deltaQty": 5,\n  "note": "Kiểm kê định kỳ lệch tăng 5"\n}')

    # M7. STOCK-IN & OCR
    add_heading_1("7. M7. STOCK-IN & SUPPLIER INVOICES — NHẬP KHO & OCR HÓA ĐƠN")
    add_heading_2("POST /api/v1/stock-ins — Tải lên ảnh hóa đơn nhập kho")
    add_body("• Mô tả: Gửi file ảnh hóa đơn (multipart/form-data, key: file) lên hệ thống. AI Service sẽ trích xuất (OCR) các mặt hàng, số lượng, đơn giá.")
    add_heading_2("POST /api/v1/stock-ins/{id}/confirm — Xác nhận nhập kho")
    add_body("• Mô tả: Sau khi kiểm tra và chỉnh sửa items (PUT /api/v1/stock-ins/{id}/items), gọi API confirm để khóa dòng dữ liệu, cộng tồn kho và ghi sổ kho.")

    # M8. CRM
    add_heading_1("8. M8. CRM / CUSTOMERS — KHÁCH HÀNG & TÍCH ĐIỂM")
    add_heading_2("GET /api/v1/customers/by-phone/{phone} — Tra cứu nhanh khách hàng theo SĐT (Phím tắt F2)")
    add_body("• Mô tả: Trả về thông tin khách hàng, hạng thành viên (tier), số điểm tích lũy và tổng dư nợ hiện tại.")

    # M9. DEBTS
    add_heading_1("9. M9. DEBTS — CÔNG NỢ PHẢI THU / PHẢI TRẢ")
    add_heading_2("POST /api/v1/debts/{id}/pay — Thu bớt tiền công nợ")
    add_body("• Mô tả: Dành cho Manager ghi nhận khoản thanh toán công nợ từ khách hàng (hoặc trả nhà cung cấp).")
    add_code_block('{\n  "amount": 50000,\n  "note": "Thu bớt tiền nợ mặt"\n}')

    # SHIFTS
    add_heading_1("10. SHIFTS — QUẢN LÝ CA LÀM VIỆC & CHỐT CA")
    add_heading_2("POST /api/v1/shifts/open — Mở ca làm việc")
    add_body("• Request Body: { 'openingCash': 1000000 } (Tiền mặt ban đầu trong két).")
    add_heading_2("POST /api/v1/shifts/close — Đóng ca & Chốt tiền")
    add_body("• Request Body: { 'countedCash': 4500000 } (Tiền mặt đếm thực tế khi bàn giao ca). Hệ thống tính chênh lệch.")

    # SUPPLIERS
    add_heading_1("11. SUPPLIERS & CUSTOMER GROUPS — NHÀ CUNG CẤP & NHÓM")
    add_heading_2("GET /api/v1/suppliers — Danh sách nhà cung cấp")
    add_body("• Mô tả: Quản lý danh bạ nhà cung cấp, thông tin liên hệ, công nợ.")

    # PAYMENTS
    add_heading_1("12. PAYMENTS & VIETQR — THANH TOÁN QR ĐỘNG")
    add_heading_2("POST /api/v1/payments/vietqr — Tạo mã VietQR động")
    add_body("• Mô tả: Tạo mã QR VietQR gắn trực tiếp với mã đơn hàng và số tiền cụ thể qua dịch vụ img.vietqr.io.")

    # F&B
    add_heading_1("13. F&B MODULE — QUẢN LÝ KHU VỰC, BÀN & ĐỊNH LƯỢNG BOM")
    add_heading_2("GET /api/v1/tables & PATCH /api/v1/tables/{id}/status — Quản lý bàn nhà hàng")
    add_body("• Mô tả: Quản lý trạng thái bàn (available, occupied, reserved, locked), chuyển bàn (POST /api/v1/tables/transfer).")
    add_heading_2("POST /api/v1/products/{productId}/recipes — Định lượng nguyên liệu BOM")
    add_body("• Mô tả: Thiết lập định lượng nguyên liệu tiêu hao cho mỗi món ăn/đồ uống trong phân hệ F&B.")

    # AI & REALTIME
    add_heading_1("14. AI SERVICE & SIGNALR REALTIME")
    add_heading_2("WebSocket /hubs/pos — Nhận thông báo thời gian thực")
    add_body("• Mô tả: Kết nối WebSocket nhận các sự kiện OrderCreated, PaymentConfirmed, LowStockAlert, ShiftOpened...")

    file_path = "docs/DigiSME_POS_API_Complete_Guide.docx"
    doc.save(file_path)
    print(f"Successfully generated {file_path}")

if __name__ == "__main__":
    create_api_doc()
