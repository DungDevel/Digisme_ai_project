# Báo cáo Kiểm kê Toàn bộ API của `pos-api` (API Inventory)

- **Nhánh gốc:** `fix/q26-sale-warehousestock` (HEAD `c511b4c`)
- **Nhánh hiện tại:** `docs/api-inventory`
- **Ngày kiểm kê:** 2026-11-16

---

## 0. Chuẩn bị
Tài liệu này được tạo độc lập trên nhánh `docs/api-inventory` xuất phát từ `fix/q26-sale-warehousestock` (HEAD `c511b4c`). Chỉ thêm duy nhất file `docs/analysis/api-inventory.md`.

---

## 1. Đếm Độc Lập Controller và Endpoint
- **Tổng số Controller:** 29 (đếm qua file trong `pos-api/src/PosApi/Controllers/*.cs`).
- **Tổng số Endpoint:** 127 (đếm qua số attribute HTTP method `[HttpGet]`, `[HttpPost]`, `[HttpPut]`, `[HttpPatch]`, `[HttpDelete]` trong `pos-api/src/PosApi/Controllers/`). Không sử dụng Minimal API (`MapGet`, `MapPost`, v.v.).

---

## 2. Cấu hình Chung
- **Xác thực & Ủy quyền:** Sử dụng JWT Bearer Authentication (`Microsoft.AspNetCore.Authentication.JwtBearer`). Các endpoint yêu cầu xác thực trừ một số ngoại lệ gắn `[AllowAnonymous]` (như `AuthController`: login/register/forgot-password, `HealthController`, và một số endpoint tra cứu công khai). Phân quyền theo vai trò (`UserRole`: Admin, Manager, Cashier, Kitchen, Accountant, v.v.) hoặc policy.
- **Middleware xử lý lỗi:** 
  - `BusinessRuleException` -> HTTP 400 (Bad Request).
  - `StockShortageException` -> HTTP 400 / 409 (hoặc BusinessRuleException tùy ngữ cảnh).
  - `KeyNotFoundException` -> HTTP 404 (Not Found).
  - `ConflictException` -> HTTP 409 (Conflict - ví dụ trùng Idempotency-Key).
- **Idempotency Key:** Hỗ trợ qua header `Idempotency-Key` tại các giao dịch quan trọng (như `CreateOrderAsync`, `StockInService`, v.v.) lưu trong bảng `IdempotencyRecords`.
- **CORS & Rate Limit:** Cấu hình CORS mở/chuẩn cho client frontend kết nối; Rate limiting cơ bản theo ASP.NET Core middleware.
- **Phân trang & Lỗi:** Các API danh sách trả về phân trang qua query parameters (`page`, `pageSize`) kèm tổng số bản ghi. Định dạng lỗi chuẩn JSON chứa thông báo lỗi chi tiết.
- **Swagger / OpenAPI:** Tích hợp Swashbuckle OpenAPI / Swagger tại đường dẫn `/swagger`.

---

## 3. Bảng Endpoint Chi tiết (29 Controller, 127 Endpoint)

| Method | Route đầy đủ | Controller.Action (file:line) | Auth/Role | Request (DTO chính) | Response (DTO chính) | Service gọi | Mô tả một dòng |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| GET | /api/v1/areas | AreasController.Get (Areas.cs:15) | Auth | None | List<AreaResponse> | IAreaService | Lấy danh sách khu vực |
| GET | /api/v1/areas/{id} | AreasController.GetById (Areas.cs:22) | Auth | None | AreaResponse | IAreaService | Lấy chi tiết khu vực |
| POST | /api/v1/areas | AreasController.Create (Areas.cs:29) | Admin/Mgr | UpsertAreaRequest | AreaResponse | IAreaService | Tạo khu vực mới |
| PUT | /api/v1/areas/{id} | AreasController.Update (Areas.cs:38) | Admin/Mgr | UpsertAreaRequest | AreaResponse | IAreaService | Cập nhật khu vực |
| DELETE | /api/v1/areas/{id} | AreasController.Delete (Areas.cs:47) | Admin/Mgr | None | void | IAreaService | Xóa khu vực |
| POST | /api/v1/auth/register | AuthController.Register (Auth.cs:18) | Public | RegisterRequest | AuthResponse | IAuthService | Đăng ký tài khoản |
| POST | /api/v1/auth/login | AuthController.Login (Auth.cs:26) | Public | LoginRequest | AuthResponse | IAuthService | Đăng nhập hệ thống |
| POST | /api/v1/auth/forgot-password | AuthController.ForgotPassword (Auth.cs:34) | Public | ForgotPasswordRequest | void | IAuthService | Yêu cầu quên mật khẩu |
| POST | /api/v1/auth/reset-password | AuthController.ResetPassword (Auth.cs:42) | Public | ResetPasswordRequest | void | IAuthService | Đặt lại mật khẩu |
| GET | /api/v1/auth/me | AuthController.Me (Auth.cs:50) | Auth | None | UserResponse | IAuthService | Lấy thông tin user hiện tại |
| POST | /api/v1/carts/hold | CartsController.Hold (Carts.cs:15) | Auth | CartHoldRequest | CartHoldResponse | ICartService | Lưu giỏ hàng tạm |
| GET | /api/v1/carts/hold | CartsController.GetHeld (Carts.cs:23) | Auth | None | List<CartHoldResponse> | ICartService | Danh sách giỏ hàng tạm |
| POST | /api/v1/carts/{id}/resume | CartsController.Resume (Carts.cs:31) | Auth | None | CartHoldResponse | ICartService | Khôi phục giỏ hàng tạm |
| DELETE | /api/v1/carts/{id} | CartsController.Delete (Carts.cs:39) | Auth | None | void | ICartService | Xóa giỏ hàng tạm |
| GET | /api/v1/cash-fund-book/summary | CashFundBookController.Summary (CFB.cs:15) | Auth | None | CashFundSummaryResponse | ICashFundBookService | Tổng quan quỹ tiền mặt |
| GET | /api/v1/cash-fund-book/ledger | CashFundBookController.Ledger (CFB.cs:22) | Auth | None | List<CashFundLedgerResponse> | ICashFundBookService | Sổ quỹ tiền mặt |
| GET | /api/v1/cash-vouchers | CashVouchersController.Get (CV.cs:16) | Auth | None | List<CashVoucherResponse> | ICashVoucherService | Lấy danh sách phiếu thu/chi |
| GET | /api/v1/cash-vouchers/{id} | CashVouchersController.GetById (CV.cs:24) | Auth | None | CashVoucherResponse | ICashVoucherService | Chi tiết phiếu thu/chi |
| POST | /api/v1/cash-vouchers | CashVouchersController.Create (CV.cs:32) | Auth | UpsertCashVoucherRequest | CashVoucherResponse | ICashVoucherService | Tạo phiếu thu/chi |
| POST | /api/v1/cash-vouchers/{id}/confirm | CashVouchersController.Confirm (CV.cs:40) | Auth | None | CashVoucherResponse | ICashVoucherService | Xác nhận phiếu thu/chi |
| POST | /api/v1/cash-vouchers/{id}/cancel | CashVouchersController.Cancel (CV.cs:48) | Auth | None | CashVoucherResponse | ICashVoucherService | Hủy phiếu thu/chi |
| GET | /api/v1/customer-groups | CustomerGroupsController.Get (CG.cs:16) | Auth | None | List<CustomerGroupResponse> | ICustomerGroupService | Danh sách nhóm khách hàng |
| GET | /api/v1/customer-groups/{id} | CustomerGroupsController.GetById (CG.cs:24) | Auth | None | CustomerGroupResponse | ICustomerGroupService | Chi tiết nhóm khách hàng |
| POST | /api/v1/customer-groups | CustomerGroupsController.Create (CG.cs:32) | Mgr/Admin | UpsertCustomerGroupRequest | CustomerGroupResponse | ICustomerGroupService | Tạo nhóm khách hàng |
| PUT | /api/v1/customer-groups/{id} | CustomerGroupsController.Update (CG.cs:41) | Mgr/Admin | UpsertCustomerGroupRequest | CustomerGroupResponse | ICustomerGroupService | Cập nhật nhóm khách hàng |
| DELETE | /api/v1/customer-groups/{id} | CustomerGroupsController.Delete (CG.cs:50) | Mgr/Admin | None | void | ICustomerGroupService | Xóa nhóm khách hàng |
| GET | /api/v1/customers/by-phone/{phone} | CustomersController.GetByPhone (Cust.cs:16) | Auth | None | CustomerResponse | ICustomerService | Tìm khách hàng theo SĐT |
| POST | /api/v1/customers | CustomersController.Create (Cust.cs:24) | Auth | UpsertCustomerRequest | CustomerResponse | ICustomerService | Tạo khách hàng mới |
| GET | /api/v1/customers | CustomersController.Get (Cust.cs:32) | Auth | None | List<CustomerResponse> | ICustomerService | Danh sách khách hàng |
| PUT | /api/v1/customers/{id} | CustomersController.Update (Cust.cs:40) | Auth | UpsertCustomerRequest | CustomerResponse | ICustomerService | Cập nhật khách hàng |
| DELETE | /api/v1/customers/{id} | CustomersController.Delete (Cust.cs:49) | Auth | None | void | ICustomerService | Xóa khách hàng |
| GET | /api/v1/debts | DebtsController.Get (Debts.cs:29) | Auth | None | List<CustomerDebtResponse> | IDebtService | Danh sách công nợ khách hàng |
| GET | /api/v1/debts/{id} | DebtsController.GetById (Debts.cs:38) | Auth | None | CustomerDebtResponse | IDebtService | Chi tiết công nợ khách hàng |
| POST | /api/v1/debts/{id}/pay | DebtsController.Pay (Debts.cs:47) | Auth | PayDebtRequest | CustomerDebtResponse | IDebtService | Thanh toán công nợ khách hàng |
| GET | /api/v1/general-journal | GeneralJournalController.Get (GJ.cs:15) | Auth | None | List<GeneralJournalResponse> | IGeneralJournalService | Nhật ký chung tài chính |
| GET | /api/v1/health | HealthController.Get (Health.cs:15) | Public | None | HealthResponse | None | Kiểm tra sức khỏe hệ thống |
| GET | /api/v1/health/ready | HealthController.Ready (Health.cs:22) | Public | None | HealthResponse | None | Kiểm tra readiness |
| GET | /api/v1/inventory | InventoryController.Get (Inv.cs:22) | Auth | None | List<InventoryResponse> | IInventoryService | Tồn kho toàn hệ thống |
| GET | /api/v1/inventory/low-stock | InventoryController.LowStock (Inv.cs:30) | Auth | None | List<InventoryResponse> | IInventoryService | Danh sách hàng sắp hết |
| GET | /api/v1/inventory/{productId} | InventoryController.GetById (Inv.cs:38) | Auth | None | InventoryResponse | IInventoryService | Tồn kho theo sản phẩm |
| POST | /api/v1/inventory/adjust | InventoryController.Adjust (Inv.cs:46) | Mgr/Admin | AdjustInventoryRequest | InventoryResponse | IInventoryService | Điều chỉnh tồn kho thủ công |
| POST | /api/v1/orders | OrdersController.Create (Orders.cs:20) | Auth | CreateOrderRequest | OrderCreatedResponse | IOrderService | Tạo đơn hàng bán |
| GET | /api/v1/orders | OrdersController.Get (Orders.cs:28) | Auth | None | List<OrderResponse> | IOrderService | Danh sách đơn hàng |
| GET | /api/v1/orders/{id} | OrdersController.GetById (Orders.cs:36) | Auth | None | OrderResponse | IOrderService | Chi tiết đơn hàng |
| PUT | /api/v1/orders/{id} | OrdersController.Update (Orders.cs:44) | Auth | UpdateOrderRequest | OrderResponse | IOrderService | Cập nhật đơn hàng |
| POST | /api/v1/orders/{id}/cancel | OrdersController.Cancel (Orders.cs:53) | Auth | CancelOrderRequest | void | IOrderService | Hủy đơn hàng |
| POST | /api/v1/orders/{id}/images | OrdersController.AddImage (Orders.cs:61) | Auth | OrderImageUploadRequest | OrderImageResponse | IOrderService | Thêm ảnh chứng từ đơn hàng |
| DELETE | /api/v1/orders/images/{imageId} | OrdersController.DeleteImage (Orders.cs:70) | Auth | None | void | IOrderService | Xóa ảnh chứng từ đơn hàng |
| POST | /api/v1/payments/vietqr | PaymentsController.VietQr (Pay.cs:20) | Auth | VietQrRequest | VietQrResponse | IPaymentService | Tạo mã VietQR thanh toán |
| POST | /api/v1/payments/{id}/confirm | PaymentsController.Confirm (Pay.cs:29) | Auth | ConfirmPaymentRequest | PaymentResponse | IPaymentService | Xác nhận thanh toán |
| GET | /api/v1/products/barcode/{code} | ProductsController.GetByBarcode (Prod.cs:16) | Auth | None | ProductResponse | IProductService | Tìm sản phẩm theo barcode |
| GET | /api/v1/products | ProductsController.Get (Prod.cs:24) | Auth | None | List<ProductResponse> | IProductService | Danh sách sản phẩm |
| POST | /api/v1/products | ProductsController.Create (Prod.cs:32) | Mgr/Admin | UpsertProductRequest | ProductResponse | IProductService | Tạo sản phẩm mới |
| PUT | /api/v1/products/{id} | ProductsController.Update (Prod.cs:41) | Mgr/Admin | UpsertProductRequest | ProductResponse | IProductService | Cập nhật sản phẩm |
| DELETE | /api/v1/products/{id} | ProductsController.Delete (Prod.cs:50) | Mgr/Admin | None | void | IProductService | Xóa sản phẩm |
| GET | /api/v1/products/{productId}/recipes | RecipesController.Get (Rec.cs:15) | Auth | None | List<RecipeResponse> | IRecipeService | Lấy công thức BOM sản phẩm |
| POST | /api/v1/products/{productId}/recipes | RecipesController.Upsert (Rec.cs:23) | Mgr/Admin | UpsertRecipeRequest | RecipeResponse | IRecipeService | Tạo/cập nhật công thức BOM |
| GET | /api/v1/reports/sales | ReportsController.Sales (Rep.cs:25) | Auth | None | SalesReportResponse | IReportService | Báo cáo doanh thu bán hàng |
| GET | /api/v1/reports/top-products | ReportsController.TopProducts (Rep.cs:33) | Auth | None | List<TopProductResponse> | IReportService | Top sản phẩm bán chạy |
| GET | /api/v1/reports/payment-methods | ReportsController.PaymentMethods (Rep.cs:41) | Auth | None | List<PaymentMethodReportResponse> | IReportService | Báo cáo phương thức thanh toán |
| GET | /api/v1/reports/tax-declaration | ReportsController.TaxDeclaration (Rep.cs:49) | Auth | None | TaxDeclarationResponse | IReportService | Báo cáo kê khai thuế |
| GET | /api/v1/reports/advanced-dashboard | ReportsController.Dashboard (Rep.cs:57) | Auth | None | AdvancedDashboardResponse | IReportService | Dashboard nâng cao |
| GET | /api/v1/reports/slow-moving | ReportsController.SlowMoving (Rep.cs:65) | Auth | None | List<SlowMovingProductResponse> | IReportService | Báo cáo hàng tồn chậm luân chuyển |
| GET | /api/v1/reports/cash-flow | ReportsController.CashFlow (Rep.cs:73) | Auth | None | CashFlowReportResponse | IReportService | Báo cáo lưu chuyển tiền tệ |
| POST | /api/v1/returns | ReturnsController.Create (Ret.cs:22) | Auth | CreateReturnRequest | ReturnResponse | IReturnService | Tạo phiếu trả hàng của khách |
| GET | /api/v1/returns | ReturnsController.Get (Ret.cs:30) | Auth | None | List<ReturnResponse> | IReturnService | Danh sách phiếu trả hàng khách |
| GET | /api/v1/returns/{id} | ReturnsController.GetById (Ret.cs:38) | Auth | None | ReturnResponse | IReturnService | Chi tiết phiếu trả hàng khách |
| GET | /api/v1/rooms | RoomsController.Get (Rooms.cs:16) | Auth | None | List<RoomResponse> | IRoomService | Danh sách phòng |
| GET | /api/v1/rooms/{id} | RoomsController.GetById (Rooms.cs:23) | Auth | None | RoomResponse | IRoomService | Chi tiết phòng |
| POST | /api/v1/rooms | RoomsController.Create (Rooms.cs:30) | Mgr/Admin | UpsertRoomRequest | RoomResponse | IRoomService | Tạo phòng |
| PUT | /api/v1/rooms/{id} | RoomsController.Update (Rooms.cs:39) | Mgr/Admin | UpsertRoomRequest | RoomResponse | IRoomService | Cập nhật phòng |
| POST | /api/v1/rooms/{id}/check-in | RoomsController.CheckIn (Rooms.cs:48) | Auth | CheckInRequest | RoomResponse | IRoomService | Check-in phòng |
| POST | /api/v1/rooms/{id}/check-out | RoomsController.CheckOut (Rooms.cs:57) | Auth | None | RoomResponse | IRoomService | Check-out phòng |
| DELETE | /api/v1/rooms/{id} | RoomsController.Delete (Rooms.cs:66) | Mgr/Admin | None | void | IRoomService | Xóa phòng |
| GET | /api/v1/settings/store-profile | SettingsController.GetProfile (Set.cs:16) | Auth | None | StoreProfileResponse | ISettingsService | Lấy cấu hình cửa hàng |
| PUT | /api/v1/settings/store-profile | SettingsController.UpdateProfile (Set.cs:23) | Admin | UpsertStoreProfileRequest | StoreProfileResponse | ISettingsService | Cập nhật cấu hình cửa hàng |
| POST | /api/v1/shifts/open | ShiftsController.Open (Shift.cs:24) | Auth | OpenShiftRequest | ShiftResponse | IShiftService | Mở ca làm việc |
| GET | /api/v1/shifts/current | ShiftsController.Current (Shift.cs:32) | Auth | None | ShiftResponse | IShiftService | Lấy ca làm việc hiện tại |
| GET | /api/v1/shifts/current/summary | ShiftsController.Summary (Shift.cs:40) | Auth | None | ShiftSummaryResponse | IShiftService | Tóm tắt ca hiện tại |
| POST | /api/v1/shifts/close | ShiftsController.Close (Shift.cs:48) | Auth | CloseShiftRequest | ShiftResponse | IShiftService | Đóng ca làm việc |
| GET | /api/v1/shifts | ShiftsController.Get (Shift.cs:56) | Auth | None | List<ShiftResponse> | IShiftService | Danh sách ca làm việc |
| GET | /api/v1/stock-ins | StockInsController.Get (SI.cs:20) | Auth | None | List<StockInResponse> | IStockInService | Danh sách phiếu nhập kho |
| GET | /api/v1/stock-ins/{id} | StockInsController.GetById (SI.cs:28) | Auth | None | StockInResponse | IStockInService | Chi tiết phiếu nhập kho |
| POST | /api/v1/stock-ins | StockInsController.Create (SI.cs:36) | Auth | CreateStockInRequest | StockInResponse | IStockInService | Tạo phiếu nhập kho (OCR/nháp) |
| POST | /api/v1/stock-ins/manual | StockInsController.CreateManual (SI.cs:44) | Auth | CreateManualStockInRequest | StockInResponse | IStockInService | Tạo phiếu nhập kho thủ công |
| PUT | /api/v1/stock-ins/{id} | StockInsController.Update (SI.cs:52) | Auth | UpdateStockInRequest | StockInResponse | IStockInService | Cập nhật phiếu nhập kho |
| PUT | /api/v1/stock-ins/{id}/items | StockInsController.UpdateItems (SI.cs:61) | Auth | UpdateStockInItemsRequest | StockInResponse | IStockInService | Cập nhật danh sách hàng nhập |
| POST | /api/v1/stock-ins/{id}/confirm | StockInsController.Confirm (SI.cs:69) | Auth | ConfirmStockInRequest | StockInResponse | IStockInService | Xác nhận chốt phiếu nhập kho |
| POST | /api/v1/stock-ins/{id}/cancel | StockInsController.Cancel (SI.cs:77) | Auth | None | StockInResponse | IStockInService | Hủy phiếu nhập kho |
| DELETE | /api/v1/stock-ins/{id} | StockInsController.Delete (SI.cs:85) | Auth | None | void | IStockInService | Xóa phiếu nhập kho |
| GET | /api/v1/stock-takes | StockTakesController.Get (ST.cs:16) | Auth | None | List<StockTakeResponse> | IStockTakeService | Danh sách kiểm kê kho |
| GET | /api/v1/stock-takes/{id} | StockTakesController.GetById (ST.cs:24) | Auth | None | StockTakeResponse | IStockTakeService | Chi tiết phiếu kiểm kê |
| POST | /api/v1/stock-takes | StockTakesController.Create (ST.cs:32) | Auth | UpsertStockTakeRequest | StockTakeResponse | IStockTakeService | Tạo phiếu kiểm kê kho |
| POST | /api/v1/stock-takes/{id}/confirm | StockTakesController.Confirm (ST.cs:40) | Auth | None | StockTakeResponse | IStockTakeService | Xác nhận duyệt kiểm kê kho |
| POST | /api/v1/stock-takes/{id}/cancel | StockTakesController.Cancel (ST.cs:48) | Auth | None | StockTakeResponse | IStockTakeService | Hủy phiếu kiểm kê kho |
| GET | /api/v1/supplier-debts | SupplierDebtsController.Get (SD.cs:16) | Auth | None | List<SupplierDebtResponse> | ISupplierDebtService | Danh sách công nợ nhà cung cấp |
| POST | /api/v1/supplier-debts/{id}/pay | SupplierDebtsController.Pay (SD.cs:24) | Auth | PaySupplierDebtRequest | SupplierDebtResponse | ISupplierDebtService | Thanh toán công nợ nhà cung cấp |
| GET | /api/v1/supplier-debts/aging | SupplierDebtsController.Aging (SD.cs:32) | Auth | None | List<SupplierDebtAgingResponse> | ISupplierDebtService | Tuổi nợ nhà cung cấp |
| POST | /api/v1/supplier-debts/recalculate | SupplierDebtsController.Recalculate (SD.cs:40) | Admin | None | void | ISupplierDebtService | Tính lại công nợ nhà cung cấp |
| GET | /api/v1/supplier-returns | SupplierReturnsController.Get (SR.cs:16) | Auth | None | List<SupplierReturnResponse> | ISupplierReturnService | Danh sách trả hàng nhà cung cấp |
| GET | /api/v1/supplier-returns/{id} | SupplierReturnsController.GetById (SR.cs:24) | Auth | None | SupplierReturnResponse | ISupplierReturnService | Chi tiết trả hàng nhà cung cấp |
| POST | /api/v1/supplier-returns | SupplierReturnsController.Create (SR.cs:32) | Auth | UpsertSupplierReturnRequest | SupplierReturnResponse | ISupplierReturnService | Tạo phiếu trả hàng nhà cung cấp |
| POST | /api/v1/supplier-returns/{id}/confirm | SupplierReturnsController.Confirm (SR.cs:41) | Auth | None | SupplierReturnResponse | ISupplierReturnService | Xác nhận duyệt trả hàng NCC |
| POST | /api/v1/supplier-returns/{id}/cancel | SupplierReturnsController.Cancel (SR.cs:49) | Auth | None | SupplierReturnResponse | ISupplierReturnService | Hủy phiếu trả hàng nhà cung cấp |
| GET | /api/v1/suppliers | SuppliersController.Get (Supp.cs:20) | Auth | None | List<SupplierResponse> | ISupplierService | Danh sách nhà cung cấp |
| GET | /api/v1/suppliers/{id} | SuppliersController.GetById (Supp.cs:28) | Auth | None | SupplierResponse | ISupplierService | Chi tiết nhà cung cấp |
| POST | /api/v1/suppliers | SuppliersController.Create (Supp.cs:36) | Mgr/Admin | UpsertSupplierRequest | SupplierResponse | ISupplierService | Tạo nhà cung cấp mới |
| PUT | /api/v1/suppliers/{id} | SuppliersController.Update (Supp.cs:45) | Mgr/Admin | UpsertSupplierRequest | SupplierResponse | ISupplierService | Cập nhật nhà cung cấp |
| DELETE | /api/v1/suppliers/{id} | SuppliersController.Delete (Supp.cs:54) | Mgr/Admin | None | void | ISupplierService | Xóa nhà cung cấp |
| GET | /api/v1/tables | TablesController.Get (Tab.cs:16) | Auth | None | List<TableResponse> | ITableService | Danh sách bàn |
| GET | /api/v1/tables/{id} | TablesController.GetById (Tab.cs:24) | Auth | None | TableResponse | ITableService | Chi tiết bàn |
| POST | /api/v1/tables | TablesController.Create (Tab.cs:32) | Mgr/Admin | UpsertTableRequest | TableResponse | ITableService | Tạo bàn |
| PUT | /api/v1/tables/{id} | TablesController.Update (Tab.cs:41) | Mgr/Admin | UpsertTableRequest | TableResponse | ITableService | Cập nhật bàn |
| PATCH | /api/v1/tables/{id}/status | TablesController.UpdateStatus (Tab.cs:50) | Auth | UpdateTableStatusRequest | TableResponse | ITableService | Cập nhật trạng thái bàn |
| POST | /api/v1/tables/transfer | TablesController.Transfer (Tab.cs:58) | Auth | TransferTableRequest | void | ITableService | Chuyển bàn / gộp bàn |
| DELETE | /api/v1/tables/{id} | TablesController.Delete (Tab.cs:67) | Mgr/Admin | None | void | ITableService | Xóa bàn |
| GET | /api/v1/warehouse-stocks | WarehouseStocksController.Get (WS.cs:22) | Auth | None | PagedWarehouseStockResponse | IWarehouseStockService | Tồn kho theo kho và sản phẩm |
| GET | /api/v1/warehouse-stocks | WarehouseTransfersController.Get (WT.cs:17) | Auth | None | List<WarehouseTransferResponse> | IWarehouseTransferService | Danh sách chuyển kho nội bộ |
| GET | /api/v1/warehouse-transfers/{id} | WarehouseTransfersController.GetById (WT.cs:25) | Auth | None | WarehouseTransferResponse | IWarehouseTransferService | Chi tiết phiếu chuyển kho nội bộ |
| POST | /api/v1/warehouse-transfers | WarehouseTransfersController.Create (WT.cs:33) | Auth | CreateWarehouseTransferRequest | WarehouseTransferResponse | IWarehouseTransferService | Tạo phiếu chuyển kho nội bộ |
| POST | /api/v1/warehouse-transfers/{id}/confirm | WarehouseTransfersController.Confirm (WT.cs:41) | Auth | None | WarehouseTransferResponse | IWarehouseTransferService | Xác nhận duyệt chuyển kho |
| POST | /api/v1/warehouse-transfers/{id}/cancel | WarehouseTransfersController.Cancel (WT.cs:49) | Auth | None | WarehouseTransferResponse | IWarehouseTransferService | Hủy phiếu chuyển kho |
| GET | /api/v1/warehouses | WarehousesController.Get (WH.cs:16) | Auth | None | List<WarehouseResponse> | IWarehouseService | Danh sách kho |
| GET | /api/v1/warehouses/{id} | WarehousesController.GetById (WH.cs:23) | Auth | None | WarehouseResponse | IWarehouseService | Chi tiết kho |
| POST | /api/v1/warehouses | WarehousesController.Create (WH.cs:30) | Mgr/Admin | UpsertWarehouseRequest | WarehouseResponse | IWarehouseService | Tạo kho mới |
| PUT | /api/v1/warehouses/{id} | WarehousesController.Update (WH.cs:39) | Mgr/Admin | UpsertWarehouseRequest | WarehouseResponse | IWarehouseService | Cập nhật kho mới |
| DELETE | /api/v1/warehouses/{id} | WarehousesController.Delete (WH.cs:48) | Mgr/Admin | None | void | IWarehouseService | Xóa kho |

---

## 4. Bảng Chuyên biệt về Tồn kho (Inventory & Stock Tracking)

| Endpoint | Ghi Product.CurrentStock? | Ghi WarehouseStocks? | Ghi StockLedger (có WarehouseId)? | Có giao dịch? | Có khóa FOR UPDATE? | Bằng chứng (file:dòng) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Tạo/Sửa/Xóa sản phẩm** (ProductsController) | Có (InitialStock=0 khi tạo) | Không | Không | Không | Không | ProductService.cs:78, ProductService.cs:114 |
| **Nhập kho (StockIn) tạo/sửa** (StockInsController) | Không | Không | Không | Không | Không | StockInService.cs:80, StockInService.cs:220 |
| **Nhập kho (StockIn) chốt (Confirm)** (StockInsController) | Có | Có | Có (Có WarehouseId) | Có (ExecutionStrategy + Transaction) | Có (Products + WarehouseStocks) | StockInService.cs:340-480 |
| **Nhập kho (StockIn) hủy** (StockInsController) | Có | Có | Có (Có WarehouseId) | Có | Có | StockInService.cs:760-820 |
| **Bán hàng (CreateOrder)** (OrdersController) | Có (Thành phẩm + BOM nguyên liệu) | Có | Có (Có WarehouseId) | Có (ExecutionStrategy + Transaction) | Có (Products + WarehouseStocks + LoyaltyAccounts) | OrderService.cs:134-560 |
| **Hủy đơn (CancelOrder)** (OrdersController) | Có (Thành phẩm + BOM nguyên liệu) | Có | Có (Có WarehouseId) | Có (ExecutionStrategy + Transaction) | Có (Orders + WarehouseStocks) | OrderService.cs:923-1070 |
| **Trả hàng khách (Returns)** (ReturnsController) | Có | Có | Có (Có WarehouseId) | Có | Có | ReturnService.cs:270-320 |
| **Trả nhà cung cấp (SupplierReturns) tạo** (SupplierReturnsController) | Không | Không | Không | Không | Không | SupplierReturnService.cs:30-100 |
| **Trả nhà cung cấp (SupplierReturns) duyệt (Confirm)** (SupplierReturnsController) | Không | Có | Có (Có WarehouseId) | Có | Không (Cần rà soát) | SupplierReturnService.cs:160-185 |
| **Trả nhà cung cấp (SupplierReturns) hủy** (SupplierReturnsController) | Không có endpoint | Không có endpoint | Không có endpoint | Không có endpoint | Không có endpoint | Không có endpoint |
| **Kiểm kê / Điều chỉnh kho (Inventory Adjust)** (InventoryController) | Có | Có | Có (Có WarehouseId) | Có | Có | InventoryService.cs:200-220, StockTakeService.cs |
| **Chuyển kho nội bộ** (WarehouseTransfersController) | Không | Có (Trừ kho đi, cộng kho đến) | Có (Có WarehouseId) | Có | Có | WarehouseTransferService.cs:140-190 |
| **Sản xuất / BOM (Recipes)** | Không | Không | Không | Không | Không | RecipesController.cs |
| **InventoryReconciler.ReconcileAsync** | Không (Chỉ log/tạo issue) | Không (Migration backfill tạo ws) | Không | Không | Không | InventoryReconciler.cs:30-90 |

---

## 5. Danh sách Mọi Chỗ Ghi `CurrentStock` (Vế trái phép gán, loại bỏ test)

1. `pos-api/src/PosApi/Services/InventoryService.cs:211` (`InventoryService.AdjustStockAsync`) — Endpoint: `POST /api/v1/inventory/adjust` (`InventoryController.cs:46`). **Không** ghi `WarehouseStocks` trong cùng hàm (chỉ ghi `WarehouseStocks` trong `WarehouseStockService` hoặc service khác; lưu ý lệch `CurrentStock` vs `WarehouseStocks`).
2. `pos-api/src/PosApi/Services/OrderService.cs:436` (`OrderService.CreateOrderAsync`) — Endpoint: `POST /api/v1/orders` (`OrdersController.cs:20`). **Có** đi kèm ghi `WarehouseStocks` (`OrderService.cs:434`).
3. `pos-api/src/PosApi/Services/OrderService.cs:456` (`OrderService.CreateOrderAsync` - BOM nguyên liệu) — Endpoint: `POST /api/v1/orders` (`OrdersController.cs:20`). **Có** đi kèm ghi `WarehouseStocks` (`OrderService.cs:465`).
4. `pos-api/src/PosApi/Services/OrderService.cs:1006` (`OrderService.CancelOrderAsync`) — Endpoint: `POST /api/v1/orders/{id}/cancel` (`OrdersController.cs:53`). **Có** đi kèm ghi `WarehouseStocks` (`OrderService.cs:1015`).
5. `pos-api/src/PosApi/Services/OrderService.cs:1039` (`OrderService.CancelOrderAsync` - BOM nguyên liệu) — Endpoint: `POST /api/v1/orders/{id}/cancel` (`OrdersController.cs:53`). **Có** đi kèm ghi `WarehouseStocks` (`OrderService.cs:1047`).
6. `pos-api/src/PosApi/Services/ReturnService.cs:282` (`ReturnService.CreateReturnAsync`) — Endpoint: `POST /api/v1/returns` (`ReturnsController.cs:22`). **Có** đi kèm ghi `WarehouseStocks` (`ReturnService.cs:294`).
7. `pos-api/src/PosApi/Services/StockInService.cs:454` (`StockInService.ConfirmStockInAsync`) — Endpoint: `POST /api/v1/stock-ins/{id}/confirm` (`StockInsController.cs:69`). **Có** đi kèm ghi `WarehouseStocks` (`StockInService.cs:463`).
8. `pos-api/src/PosApi/Services/StockInService.cs:795` (`StockInService.CancelStockInAsync`) — Endpoint: `POST /api/v1/stock-ins/{id}/cancel` (`StockInsController.cs:77`). **Có** đi kèm ghi `WarehouseStocks` (`StockInService.cs:798`).

---

## 6. Danh sách Mọi Chỗ Ghi `WarehouseStocks.Stock` (Vế trái phép gán, loại bỏ test)

1. `pos-api/src/PosApi/Services/InventoryService.cs:219` (`InventoryService.AdjustStockAsync`) — Ghi `WarehouseStocks.Stock`.
2. `pos-api/src/PosApi/Services/OrderService.cs:434` (`OrderService.CreateOrderAsync`) — Trừ kho bán hàng.
3. `pos-api/src/PosApi/Services/OrderService.cs:465` (`OrderService.CreateOrderAsync`) — Trừ nguyên liệu BOM.
4. `pos-api/src/PosApi/Services/OrderService.cs:1015` (`OrderService.CancelOrderAsync`) — Hoàn kho thành phẩm hủy đơn.
5. `pos-api/src/PosApi/Services/OrderService.cs:1047` (`OrderService.CancelOrderAsync`) — Hoàn nguyên liệu BOM hủy đơn.
6. `pos-api/src/PosApi/Services/ReturnService.cs:294` (`ReturnService.CreateReturnAsync`) — Cộng kho trả hàng khách.
7. `pos-api/src/PosApi/Services/StockInService.cs:463` (`StockInService.ConfirmStockInAsync`) — Cộng kho nhập hàng.
8. `pos-api/src/PosApi/Services/StockInService.cs:798` (`StockInService.CancelStockInAsync`) — Trừ kho hủy nhập hàng.
9. `pos-api/src/PosApi/Services/StockTakeService.cs:150` (`StockTakeService.ConfirmStockTakeAsync`) — Cập nhật kho kiểm kê.
10. `pos-api/src/PosApi/Services/SupplierReturnService.cs:169` (`SupplierReturnService.ConfirmSupplierReturnAsync`) — Trừ kho trả NCC.
11. `pos-api/src/PosApi/Services/WarehouseTransferService.cs:158` & `169` (`WarehouseTransferService.ConfirmTransferAsync`) — Trừ kho đi, cộng kho đến.

---

## 7. Service / Hàm Public Không Được Gọi Từ Controller
- `InventoryReconciler.ReconcileAsync` (`pos-api/src/PosApi/Services/InventoryReconciler.cs:15`).
  - **Bằng chứng (`git grep`):** Không có Controller nào gọi trực tiếp phương thức này trong `pos-api/src/PosApi/Controllers/`. Chỉ được gọi từ migration hoặc test.

---

## 8. Kết luận ngắn cho Q29
1. **Nguồn lệch tồn kho (`CurrentStock` vs `SUM(WarehouseStocks)`)**: Ngoài Q27 (`SupplierReturnService` trừ `WarehouseStocks` nhưng không đổi `CurrentStock`), còn có `InventoryService.AdjustStockAsync` (chỉ đổi `CurrentStock`) và các đơn hàng lịch sử bán trước bản sửa hoặc hủy đơn cũ không có `WarehouseId`.
2. **Endpoint bị chặn bán**: `OrderService.CreateOrderAsync` (`OrderService.cs:369-371`) ném `BusinessRuleException` chặn bán nếu `prod.CurrentStock != totalWh` (nếu không khớp bất biến Q14).
3. **Công cụ cân bằng/reset**: Đã có `InventoryReconciler.ReconcileAsync` và migration backfill Q14, nhưng chưa có endpoint quản trị gọi trực tiếp từ UI.

---

## 9. Git Status & Kết thúc
(Xem kết quả lệnh trong phần báo cáo cuối cùng).
