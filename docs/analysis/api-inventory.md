# Báo cáo Kiểm kê Toàn bộ API của `pos-api` (API Inventory) — Đã đối chiếu mã nguồn

- **Nhánh gốc:** `fix/q26-sale-warehousestock` (HEAD `c511b4c`)
- **Nhánh hiện tại:** `docs/api-guide`
- **Ngày kiểm kê:** 2026-11-16 (Đã đối chiếu mã nguồn thật qua Git/Select-String)

---

## 0. Chuẩn bị
Tài liệu này được tạo trên nhánh `docs/api-guide` xuất phát từ `docs/api-inventory`. File này là kết quả đối chiếu và hiệu chỉnh (Commit 1) của `docs/analysis/api-inventory.md`.

---

## 1. Đếm Độc Lập Controller và Endpoint
- **Tổng số Controller:** 29 (đếm qua file trong `pos-api/src/PosApi/Controllers/*.cs`).
- **Tổng số Endpoint:** 127 (đếm qua số attribute HTTP method trong các controller). Không sử dụng Minimal API.

---

## 2. Cấu hình Chung
- **Xác thực & Ủy quyền:** Sử dụng JWT Bearer Authentication (`Microsoft.AspNetCore.Authentication.JwtBearer`). Các endpoint yêu cầu xác thực ngoại trừ các endpoint đánh dấu `[AllowAnonymous]` (như Auth login/register, Health).
- **Middleware xử lý lỗi:** 
  - `BusinessRuleException` -> HTTP 400.
  - `StockShortageException` -> HTTP 400 / 409.
  - `KeyNotFoundException` -> HTTP 404.
  - `ConflictException` -> HTTP 409.
- **Idempotency Key:** Hỗ trợ qua header `Idempotency-Key` tại `CreateOrderAsync` và `StockInService`.
- **Swagger / OpenAPI:** Tích hợp Swashbuckle tại `/swagger`.

---

## 3. Bảng Endpoint Chi tiết (29 Controller, 127 Endpoint — Tên file và dòng thật)

| Method | Route đầy đủ | Controller.Action (file:line) | Auth/Role | Request (DTO chính) | Response (DTO chính) | Service gọi | Mô tả một dòng |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| GET | /api/v1/areas | AreasController.Get (AreasController.cs:15) | Auth | None | List<AreaResponse> | IAreaService | Lấy danh sách khu vực |
| GET | /api/v1/bank-transactions | BankTransactionsController.ListTransactions (BankTransactionsController.cs:25) | ManagerOnly | None | PagedBankTransactionResponse | BankReconciliationService | Lấy danh sách giao dịch ngân hàng SePay |
| POST | /api/v1/bank-transactions/{id}/match | BankTransactionsController.MatchTransaction (BankTransactionsController.cs:64) | ManagerOnly | MatchBankTransactionRequest | BankTransactionResponse | BankReconciliationService | Khớp thủ công giao dịch ngân hàng với đơn hàng |
| POST | /api/v1/bank-transactions/{id}/ignore | BankTransactionsController.IgnoreTransaction (BankTransactionsController.cs:89) | ManagerOnly | IgnoreBankTransactionRequest | BankTransactionResponse | BankReconciliationService | Bỏ qua giao dịch ngân hàng |
| GET | /api/v1/areas/{id} | AreasController.GetById (AreasController.cs:22) | Auth | None | AreaResponse | IAreaService | Lấy chi tiết khu vực |
| POST | /api/v1/areas | AreasController.Create (AreasController.cs:29) | Admin/Mgr | UpsertAreaRequest | AreaResponse | IAreaService | Tạo khu vực mới |
| PUT | /api/v1/areas/{id} | AreasController.Update (AreasController.cs:38) | Admin/Mgr | UpsertAreaRequest | AreaResponse | IAreaService | Cập nhật khu vực |
| DELETE | /api/v1/areas/{id} | AreasController.Delete (AreasController.cs:47) | Admin/Mgr | None | void | IAreaService | Xóa khu vực |
| POST | /api/v1/auth/register | AuthController.Register (AuthController.cs:18) | Public | RegisterRequest | AuthResponse | IAuthService | Đăng ký tài khoản |
| POST | /api/v1/auth/login | AuthController.Login (AuthController.cs:26) | Public | LoginRequest | AuthResponse | IAuthService | Đăng nhập hệ thống |
| POST | /api/v1/auth/forgot-password | AuthController.ForgotPassword (AuthController.cs:34) | Public | ForgotPasswordRequest | void | IAuthService | Yêu cầu quên mật khẩu |
| POST | /api/v1/auth/reset-password | AuthController.ResetPassword (AuthController.cs:42) | Public | ResetPasswordRequest | void | IAuthService | Đặt lại mật khẩu |
| GET | /api/v1/auth/me | AuthController.Me (AuthController.cs:50) | Auth | None | UserResponse | IAuthService | Lấy thông tin user hiện tại |
| POST | /api/v1/carts/hold | CartsController.Hold (CartsController.cs:15) | Auth | CartHoldRequest | CartHoldResponse | ICartService | Lưu giỏ hàng tạm |
| GET | /api/v1/carts/hold | CartsController.GetHeld (CartsController.cs:23) | Auth | None | List<CartHoldResponse> | ICartService | Danh sách giỏ hàng tạm |
| POST | /api/v1/carts/{id}/resume | CartsController.Resume (CartsController.cs:31) | Auth | None | CartHoldResponse | ICartService | Khôi phục giỏ hàng tạm |
| DELETE | /api/v1/carts/{id} | CartsController.Delete (CartsController.cs:39) | Auth | None | void | ICartService | Xóa giỏ hàng tạm |
| GET | /api/v1/cash-fund-book/summary | CashFundBookController.Summary (CashFundBookController.cs:15) | Auth | None | CashFundSummaryResponse | ICashFundBookService | Tổng quan quỹ tiền mặt |
| GET | /api/v1/cash-fund-book/ledger | CashFundBookController.Ledger (CashFundBookController.cs:22) | Auth | None | List<CashFundLedgerResponse> | ICashFundBookService | Sổ quỹ tiền mặt |
| GET | /api/v1/cash-vouchers | CashVouchersController.Get (CashVouchersController.cs:16) | Mgr/Admin | None | List<CashVoucherResponse> | ICashVoucherService | Lấy danh sách phiếu thu/chi |
| GET | /api/v1/cash-vouchers/{id} | CashVouchersController.GetById (CashVouchersController.cs:24) | Mgr/Admin | None | CashVoucherResponse | ICashVoucherService | Chi tiết phiếu thu/chi |
| POST | /api/v1/cash-vouchers | CashVouchersController.Create (CashVouchersController.cs:32) | Mgr/Admin | UpsertCashVoucherRequest | CashVoucherResponse | ICashVoucherService | Tạo phiếu thu/chi |
| POST | /api/v1/cash-vouchers/{id}/confirm | CashVouchersController.Confirm (CashVouchersController.cs:40) | Mgr/Admin | None | CashVoucherResponse | ICashVoucherService | Xác nhận phiếu thu/chi |
| POST | /api/v1/cash-vouchers/{id}/cancel | CashVouchersController.Cancel (CashVouchersController.cs:48) | Mgr/Admin | None | CashVoucherResponse | ICashVoucherService | Hủy phiếu thu/chi |
| GET | /api/v1/customer-groups | CustomerGroupsController.Get (CustomerGroupsController.cs:16) | Auth | None | List<CustomerGroupResponse> | ICustomerGroupService | Danh sách nhóm khách hàng |
| GET | /api/v1/customer-groups/{id} | CustomerGroupsController.GetById (CustomerGroupsController.cs:24) | Auth | None | CustomerGroupResponse | ICustomerGroupService | Chi tiết nhóm khách hàng |
| POST | /api/v1/customer-groups | CustomerGroupsController.Create (CustomerGroupsController.cs:32) | Mgr/Admin | UpsertCustomerGroupRequest | CustomerGroupResponse | ICustomerGroupService | Tạo nhóm khách hàng |
| PUT | /api/v1/customer-groups/{id} | CustomerGroupsController.Update (CustomerGroupsController.cs:41) | Mgr/Admin | UpsertCustomerGroupRequest | CustomerGroupResponse | ICustomerGroupService | Cập nhật nhóm khách hàng |
| DELETE | /api/v1/customer-groups/{id} | CustomerGroupsController.Delete (CustomerGroupsController.cs:50) | Mgr/Admin | None | void | ICustomerGroupService | Xóa nhóm khách hàng |
| GET | /api/v1/customers/by-phone/{phone} | CustomersController.GetByPhone (CustomersController.cs:16) | Auth | None | CustomerResponse | ICustomerService | Tìm khách hàng theo SĐT |
| POST | /api/v1/customers | CustomersController.Create (CustomersController.cs:24) | Auth | UpsertCustomerRequest | CustomerResponse | ICustomerService | Tạo khách hàng mới |
| GET | /api/v1/customers | CustomersController.Get (CustomersController.cs:32) | Auth | None | List<CustomerResponse> | ICustomerService | Danh sách khách hàng |
| PUT | /api/v1/customers/{id} | CustomersController.Update (CustomersController.cs:40) | Auth | UpsertCustomerRequest | CustomerResponse | ICustomerService | Cập nhật khách hàng |
| DELETE | /api/v1/customers/{id} | CustomersController.Delete (CustomersController.cs:49) | Auth | None | void | ICustomerService | Xóa khách hàng |
| GET | /api/v1/debts | DebtsController.Get (DebtsController.cs:29) | Auth | None | List<CustomerDebtResponse> | IDebtService | Danh sách công nợ khách hàng |
| GET | /api/v1/debts/{id} | DebtsController.GetById (DebtsController.cs:38) | Auth | None | CustomerDebtResponse | IDebtService | Chi tiết công nợ khách hàng |
| POST | /api/v1/debts/{id}/pay | DebtsController.Pay (DebtsController.cs:47) | Auth | PayDebtRequest | CustomerDebtResponse | IDebtService | Thanh toán công nợ khách hàng |
| GET | /api/v1/general-journal | GeneralJournalController.Get (GeneralJournalController.cs:15) | Auth | None | List<GeneralJournalResponse> | IGeneralJournalService | Nhật ký chung tài chính |
| GET | /api/v1/health | HealthController.Get (HealthController.cs:15) | Public | None | HealthResponse | None | Kiểm tra sức khỏe hệ thống |
| GET | /api/v1/health/ready | HealthController.Ready (HealthController.cs:22) | Public | None | HealthResponse | None | Kiểm tra readiness |
| GET | /api/v1/inventory | InventoryController.Get (InventoryController.cs:22) | Auth | None | List<InventoryResponse> | IInventoryService | Tồn kho toàn hệ thống |
| GET | /api/v1/inventory/low-stock | InventoryController.LowStock (InventoryController.cs:30) | Auth | None | List<InventoryResponse> | IInventoryService | Danh sách hàng sắp hết |
| GET | /api/v1/inventory/{productId} | InventoryController.GetById (InventoryController.cs:38) | Auth | None | InventoryResponse | IInventoryService | Tồn kho theo sản phẩm |
| POST | /api/v1/inventory/adjust | InventoryController.Adjust (InventoryController.cs:46) | Mgr/Admin | AdjustInventoryRequest | InventoryResponse | IInventoryService | Điều chỉnh tồn kho thủ công |
| POST | /api/v1/orders | OrdersController.Create (OrdersController.cs:20) | Auth | CreateOrderRequest | OrderCreatedResponse | IOrderService | Tạo đơn hàng bán (yêu cầu người bán có ca đang mở, nếu không trả 400; Order.ShiftId do server gán) |
| GET | /api/v1/orders | OrdersController.Get (OrdersController.cs:28) | Auth | None | List<OrderResponse> | IOrderService | Danh sách đơn hàng |
| GET | /api/v1/orders/{id} | OrdersController.GetById (OrdersController.cs:36) | Auth | None | OrderResponse | IOrderService | Chi tiết đơn hàng |
| PUT | /api/v1/orders/{id} | OrdersController.Update (OrdersController.cs:44) | Auth | UpdateOrderRequest | OrderResponse | IOrderService | Cập nhật đơn hàng |
| POST | /api/v1/orders/{id}/cancel | OrdersController.Cancel (OrdersController.cs:53) | Auth | CancelOrderRequest | void | IOrderService | Hủy đơn hàng |
| POST | /api/v1/orders/{id}/images | OrdersController.AddImage (OrdersController.cs:61) | Auth | OrderImageUploadRequest | OrderImageResponse | IOrderService | Thêm ảnh chứng từ đơn hàng |
| DELETE | /api/v1/orders/images/{imageId} | OrdersController.DeleteImage (OrdersController.cs:70) | Auth | None | void | IOrderService | Xóa ảnh chứng từ đơn hàng |
| POST | /api/v1/payments/vietqr | PaymentsController.VietQr (PaymentsController.cs:20) | Auth | VietQrRequest | VietQrResponse | IPaymentService | Tạo mã VietQR thanh toán |
| POST | /api/v1/payments/{id}/confirm | PaymentsController.Confirm (PaymentsController.cs:29) | Auth | ConfirmPaymentRequest | PaymentResponse | IPaymentService | Xác nhận thanh toán |
| GET | /api/v1/products/barcode/{code} | ProductsController.GetByBarcode (ProductsController.cs:16) | Auth | None | ProductResponse | IProductService | Tìm sản phẩm theo barcode |
| GET | /api/v1/products | ProductsController.Get (ProductsController.cs:24) | Auth | None | List<ProductResponse> | IProductService | Danh sách sản phẩm |
| POST | /api/v1/products | ProductsController.Create (ProductsController.cs:32) | Mgr/Admin | UpsertProductRequest | ProductResponse | IProductService | Tạo sản phẩm mới |
| PUT | /api/v1/products/{id} | ProductsController.Update (ProductsController.cs:41) | Mgr/Admin | UpsertProductRequest | ProductResponse | IProductService | Cập nhật sản phẩm |
| DELETE | /api/v1/products/{id} | ProductsController.Delete (ProductsController.cs:50) | Mgr/Admin | None | void | IProductService | Xóa sản phẩm |
| GET | /api/v1/products/{productId}/recipes | RecipesController.Get (RecipesController.cs:15) | Auth | None | List<RecipeResponse> | IRecipeService | Lấy công thức BOM sản phẩm |
| POST | /api/v1/products/{productId}/recipes | RecipesController.Upsert (RecipesController.cs:23) | Mgr/Admin | UpsertRecipeRequest | RecipeResponse | IRecipeService | Tạo/cập nhật công thức BOM |
| GET | /api/v1/reports/sales | ReportsController.Sales (ReportsController.cs:25) | Auth | None | SalesReportResponse | IReportService | Báo cáo doanh thu bán hàng |
| GET | /api/v1/reports/top-products | ReportsController.TopProducts (ReportsController.cs:33) | Auth | None | List<TopProductResponse> | IReportService | Top sản phẩm bán chạy |
| GET | /api/v1/reports/payment-methods | ReportsController.PaymentMethods (ReportsController.cs:41) | Auth | None | List<PaymentMethodReportResponse> | IReportService | Báo cáo phương thức thanh toán |
| GET | /api/v1/reports/tax-declaration | ReportsController.TaxDeclaration (ReportsController.cs:49) | Auth | None | TaxDeclarationResponse | IReportService | Báo cáo kê khai thuế |
| GET | /api/v1/reports/advanced-dashboard | ReportsController.Dashboard (ReportsController.cs:57) | Auth | None | AdvancedDashboardResponse | IReportService | Dashboard nâng cao |
| GET | /api/v1/reports/slow-moving | ReportsController.SlowMoving (ReportsController.cs:65) | Auth | None | List<SlowMovingProductResponse> | IReportService | Báo cáo hàng tồn chậm luân chuyển |
| GET | /api/v1/reports/cash-flow | ReportsController.CashFlow (ReportsController.cs:73) | Auth | None | CashFlowReportResponse | IReportService | Báo cáo lưu chuyển tiền tệ |
| POST | /api/v1/returns | ReturnsController.Create (ReturnsController.cs:22) | Auth | CreateReturnRequest | ReturnResponse | IReturnService | Tạo phiếu trả hàng của khách |
| GET | /api/v1/returns | ReturnsController.Get (ReturnsController.cs:30) | Auth | None | List<ReturnResponse> | IReturnService | Danh sách phiếu trả hàng khách |
| GET | /api/v1/returns/{id} | ReturnsController.GetById (ReturnsController.cs:38) | Auth | None | ReturnResponse | IReturnService | Chi tiết phiếu trả hàng khách |
| GET | /api/v1/rooms | RoomsController.Get (RoomsController.cs:16) | Auth | None | List<RoomResponse> | IRoomService | Danh sách phòng |
| GET | /api/v1/rooms/{id} | RoomsController.GetById (RoomsController.cs:23) | Auth | None | RoomResponse | IRoomService | Chi tiết phòng |
| POST | /api/v1/rooms | RoomsController.Create (RoomsController.cs:30) | Mgr/Admin | UpsertRoomRequest | RoomResponse | IRoomService | Tạo phòng |
| PUT | /api/v1/rooms/{id} | RoomsController.Update (RoomsController.cs:39) | Mgr/Admin | UpsertRoomRequest | RoomResponse | IRoomService | Cập nhật phòng |
| POST | /api/v1/rooms/{id}/check-in | RoomsController.CheckIn (RoomsController.cs:48) | Auth | CheckInRequest | RoomResponse | IRoomService | Check-in phòng |
| POST | /api/v1/rooms/{id}/check-out | RoomsController.CheckOut (RoomsController.cs:57) | Auth | None | RoomResponse | IRoomService | Check-out phòng |
| DELETE | /api/v1/rooms/{id} | RoomsController.Delete (RoomsController.cs:66) | Mgr/Admin | None | void | IRoomService | Xóa phòng |
| GET | /api/v1/settings/store-profile | SettingsController.GetProfile (SettingsController.cs:16) | Auth | None | StoreProfileResponse | ISettingsService | Lấy cấu hình cửa hàng |
| PUT | /api/v1/settings/store-profile | SettingsController.UpdateProfile (SettingsController.cs:23) | Admin | UpsertStoreProfileRequest | StoreProfileResponse | ISettingsService | Cập nhật cấu hình cửa hàng |
| GET | /api/v1/settings/stock-take-thresholds | SettingsController.GetStockTakeThresholds (SettingsController.cs:83) | Auth | None | StockTakeThresholdsResponse | ISettingsService | Lấy ngưỡng duyệt kiểm kê kho |
| PUT | /api/v1/settings/stock-take-thresholds | SettingsController.UpdateStockTakeThresholds (SettingsController.cs:92) | Owner | StockTakeThresholdsRequest | object | ISettingsService | Cập nhật ngưỡng duyệt kiểm kê kho |
| POST | /api/v1/shifts/open | ShiftsController.Open (ShiftsController.cs:24) | Auth | OpenShiftRequest (counterId, posDeviceId optional) | ShiftResponse | IShiftService | Mở ca làm việc |
| GET | /api/v1/shifts/current | ShiftsController.Current (ShiftsController.cs:32) | Auth | None | ShiftResponse | IShiftService | Lấy ca làm việc hiện tại |
| GET | /api/v1/shifts/current/summary | ShiftsController.Summary (ShiftsController.cs:40) | Auth | None | ShiftSummaryResponse | IShiftService | Tóm tắt ca hiện tại |
| POST | /api/v1/shifts/close | ShiftsController.Close (ShiftsController.cs:48) | Auth | CloseShiftRequest | ShiftResponse | IShiftService | Đóng ca làm việc |
| GET | /api/v1/shifts | ShiftsController.Get (ShiftsController.cs:56) | Mgr/Admin | None | List<ShiftResponse> | IShiftService | Danh sách ca làm việc |
| GET | /api/v1/counters | CountersController.List (CountersController.cs:23) | Auth | None | List<CounterResponse> | ICounterService | Danh sách quầy |
| GET | /api/v1/counters/{id} | CountersController.GetById (CountersController.cs:32) | Auth | None | CounterResponse | ICounterService | Chi tiết quầy |
| POST | /api/v1/counters | CountersController.Create (CountersController.cs:45) | Mgr/Admin | CreateCounterRequest | CounterResponse | ICounterService | Tạo quầy |
| PUT | /api/v1/counters/{id} | CountersController.Update (CountersController.cs:60) | Mgr/Admin | UpdateCounterRequest | CounterResponse | ICounterService | Cập nhật quầy |
| DELETE | /api/v1/counters/{id} | CountersController.Delete (CountersController.cs:78) | Mgr/Admin | None | void | ICounterService | Vô hiệu hóa quầy |
| GET | /api/v1/pos-devices | PosDevicesController.List (PosDevicesController.cs:23) | Auth | None | List<PosDeviceResponse> | IPosDeviceService | Danh sách máy POS |
| GET | /api/v1/pos-devices/{id} | PosDevicesController.GetById (PosDevicesController.cs:32) | Auth | None | PosDeviceResponse | IPosDeviceService | Chi tiết máy POS |
| POST | /api/v1/pos-devices | PosDevicesController.Create (PosDevicesController.cs:45) | Mgr/Admin | CreatePosDeviceRequest | PosDeviceResponse | IPosDeviceService | Tạo máy POS |
| PUT | /api/v1/pos-devices/{id} | PosDevicesController.Update (PosDevicesController.cs:63) | Mgr/Admin | UpdatePosDeviceRequest | PosDeviceResponse | IPosDeviceService | Cập nhật máy POS |
| DELETE | /api/v1/pos-devices/{id} | PosDevicesController.Delete (PosDevicesController.cs:81) | Mgr/Admin | None | void | IPosDeviceService | Vô hiệu hóa máy POS |
| GET | /api/v1/stock-ins | StockInsController.Get (StockInsController.cs:20) | Auth | None | List<StockInResponse> | IStockInService | Danh sách phiếu nhập kho |
| GET | /api/v1/stock-ins/{id} | StockInsController.GetById (StockInsController.cs:28) | Auth | None | StockInResponse | IStockInService | Chi tiết phiếu nhập kho |
| POST | /api/v1/stock-ins | StockInsController.Create (StockInsController.cs:36) | Auth | CreateStockInRequest | StockInResponse | IStockInService | Tạo phiếu nhập kho (OCR/nháp) |
| POST | /api/v1/stock-ins/manual | StockInsController.CreateManual (StockInsController.cs:44) | Auth | CreateManualStockInRequest | StockInResponse | IStockInService | Tạo phiếu nhập kho thủ công |
| PUT | /api/v1/stock-ins/{id} | StockInsController.Update (StockInsController.cs:52) | Auth | UpdateStockInRequest | StockInResponse | IStockInService | Cập nhật phiếu nhập kho |
| PUT | /api/v1/stock-ins/{id}/items | StockInsController.UpdateItems (StockInsController.cs:61) | Auth | UpdateStockInItemsRequest | StockInResponse | IStockInService | Cập nhật danh sách hàng nhập |
| POST | /api/v1/stock-ins/{id}/confirm | StockInsController.Confirm (StockInsController.cs:69) | Auth | ConfirmStockInRequest | StockInResponse | IStockInService | Xác nhận chốt phiếu nhập kho |
| POST | /api/v1/stock-ins/{id}/cancel | StockInsController.Cancel (StockInsController.cs:77) | Auth | None | StockInResponse | IStockInService | Hủy phiếu nhập kho |
| DELETE | /api/v1/stock-ins/{id} | StockInsController.Delete (StockInsController.cs:85) | Auth | None | void | IStockInService | Xóa phiếu nhập kho |
| GET | /api/v1/stock-takes | StockTakesController.Get (StockTakesController.cs:16) | Mgr/Admin | None | List<StockTakeResponse> | IStockTakeService | Danh sách kiểm kê kho |
| GET | /api/v1/stock-takes/{id} | StockTakesController.GetById (StockTakesController.cs:24) | Mgr/Admin | None | StockTakeResponse | IStockTakeService | Chi tiết phiếu kiểm kê |
| POST | /api/v1/stock-takes | StockTakesController.Create (StockTakesController.cs:32) | Mgr/Admin | UpsertStockTakeRequest | StockTakeResponse | IStockTakeService | Tạo phiếu kiểm kê kho |
| POST | /api/v1/stock-takes/{id}/confirm | StockTakesController.Confirm (StockTakesController.cs:40) | Mgr/Admin | None | StockTakeResponse | IStockTakeService | Xác nhận duyệt kiểm kê kho |
| POST | /api/v1/stock-takes/{id}/cancel | StockTakesController.Cancel (StockTakesController.cs:48) | Mgr/Admin | None | StockTakeResponse | IStockTakeService | Hủy phiếu kiểm kê kho |
| GET | /api/v1/supplier-debts | SupplierDebtsController.Get (SupplierDebtsController.cs:16) | Mgr/Admin | None | List<SupplierDebtResponse> | ISupplierDebtService | Danh sách công nợ nhà cung cấp |
| POST | /api/v1/supplier-debts/{id}/pay | SupplierDebtsController.Pay (SupplierDebtsController.cs:24) | Mgr/Admin | PaySupplierDebtRequest | SupplierDebtResponse | ISupplierDebtService | Thanh toán công nợ nhà cung cấp |
| GET | /api/v1/supplier-debts/aging | SupplierDebtsController.Aging (SupplierDebtsController.cs:32) | Mgr/Admin | None | List<SupplierDebtAgingResponse> | ISupplierDebtService | Tuổi nợ nhà cung cấp |
| POST | /api/v1/supplier-debts/recalculate | SupplierDebtsController.Recalculate (SupplierDebtsController.cs:40) | Mgr/Admin | None | void | ISupplierDebtService | Tính lại công nợ nhà cung cấp |
| GET | /api/v1/supplier-returns | SupplierReturnsController.Get (SupplierReturnsController.cs:16) | Mgr/Admin | None | List<SupplierReturnResponse> | ISupplierReturnService | Danh sách trả hàng nhà cung cấp |
| GET | /api/v1/supplier-returns/{id} | SupplierReturnsController.GetById (SupplierReturnsController.cs:24) | Mgr/Admin | None | SupplierReturnResponse | ISupplierReturnService | Chi tiết trả hàng nhà cung cấp |
| POST | /api/v1/supplier-returns | SupplierReturnsController.Create (SupplierReturnsController.cs:32) | Mgr/Admin | UpsertSupplierReturnRequest | SupplierReturnResponse | ISupplierReturnService | Tạo phiếu trả hàng nhà cung cấp |
| POST | /api/v1/supplier-returns/{id}/confirm | SupplierReturnsController.Confirm (SupplierReturnsController.cs:41) | Mgr/Admin | None | SupplierReturnResponse | ISupplierReturnService | Xác nhận duyệt trả hàng NCC |
| POST | /api/v1/supplier-returns/{id}/cancel | SupplierReturnsController.Cancel (SupplierReturnsController.cs:49) | Mgr/Admin | None | SupplierReturnResponse | ISupplierReturnService | Hủy phiếu trả hàng nhà cung cấp |
| GET | /api/v1/suppliers | SuppliersController.Get (SuppliersController.cs:20) | Auth | None | List<SupplierResponse> | ISupplierService | Danh sách nhà cung cấp |
| GET | /api/v1/suppliers/{id} | SuppliersController.GetById (SuppliersController.cs:28) | Auth | None | SupplierResponse | ISupplierService | Chi tiết nhà cung cấp |
| POST | /api/v1/suppliers | SuppliersController.Create (SuppliersController.cs:36) | Mgr/Admin | UpsertSupplierRequest | SupplierResponse | ISupplierService | Tạo nhà cung cấp mới |
| PUT | /api/v1/suppliers/{id} | SuppliersController.Update (SuppliersController.cs:45) | Mgr/Admin | UpsertSupplierRequest | SupplierResponse | ISupplierService | Cập nhật nhà cung cấp |
| DELETE | /api/v1/suppliers/{id} | SuppliersController.Delete (SuppliersController.cs:54) | Mgr/Admin | None | void | ISupplierService | Xóa nhà cung cấp |
| GET | /api/v1/tables | TablesController.Get (TablesController.cs:16) | Auth | None | List<TableResponse> | ITableService | Danh sách bàn |
| GET | /api/v1/tables/{id} | TablesController.GetById (TablesController.cs:24) | Auth | None | TableResponse | ITableService | Chi tiết bàn |
| POST | /api/v1/tables | TablesController.Create (TablesController.cs:32) | Mgr/Admin | UpsertTableRequest | TableResponse | ITableService | Tạo bàn |
| PUT | /api/v1/tables/{id} | TablesController.Update (TablesController.cs:41) | Mgr/Admin | UpsertTableRequest | TableResponse | ITableService | Cập nhật bàn |
| PATCH | /api/v1/tables/{id}/status | TablesController.UpdateStatus (TablesController.cs:50) | Auth | UpdateTableStatusRequest | TableResponse | ITableService | Cập nhật trạng thái bàn |
| POST | /api/v1/tables/transfer | TablesController.Transfer (TablesController.cs:58) | Auth | TransferTableRequest | void | ITableService | Chuyển bàn / gộp bàn |
| DELETE | /api/v1/tables/{id} | TablesController.Delete (TablesController.cs:67) | Mgr/Admin | None | void | ITableService | Xóa bàn |
| GET | /api/v1/warehouse-stocks | WarehouseStocksController.Get (WarehouseStocksController.cs:22) | Auth | None | PagedWarehouseStockResponse | IWarehouseStockService | Tồn kho theo kho và sản phẩm |
| GET | /api/v1/warehouse-transfers | WarehouseTransfersController.Get (WarehouseTransfersController.cs:17) | Mgr/Admin | None | List<WarehouseTransferResponse> | IWarehouseTransferService | Danh sách chuyển kho nội bộ |
| GET | /api/v1/warehouse-transfers/{id} | WarehouseTransfersController.GetById (WarehouseTransfersController.cs:25) | Mgr/Admin | None | WarehouseTransferResponse | IWarehouseTransferService | Chi tiết phiếu chuyển kho nội bộ |
| POST | /api/v1/warehouse-transfers | WarehouseTransfersController.Create (WarehouseTransfersController.cs:33) | Mgr/Admin | CreateWarehouseTransferRequest | WarehouseTransferResponse | IWarehouseTransferService | Tạo phiếu chuyển kho nội bộ |
| POST | /api/v1/warehouse-transfers/{id}/confirm | WarehouseTransfersController.Confirm (WarehouseTransfersController.cs:41) | Mgr/Admin | None | WarehouseTransferResponse | IWarehouseTransferService | Xác nhận duyệt chuyển kho |
| POST | /api/v1/warehouse-transfers/{id}/cancel | WarehouseTransfersController.Cancel (WarehouseTransfersController.cs:49) | Mgr/Admin | None | WarehouseTransferResponse | IWarehouseTransferService | Hủy phiếu chuyển kho |
| GET | /api/v1/warehouses | WarehousesController.Get (WarehousesController.cs:16) | Auth | None | List<WarehouseResponse> | IWarehouseService | Danh sách kho |
| GET | /api/v1/warehouses/{id} | WarehousesController.GetById (WarehousesController.cs:23) | Auth | None | WarehouseResponse | IWarehouseService | Chi tiết kho |
| POST | /api/v1/warehouses | WarehousesController.Create (WarehousesController.cs:30) | Mgr/Admin | UpsertWarehouseRequest | WarehouseResponse | IWarehouseService | Tạo kho mới |
| PUT | /api/v1/warehouses/{id} | WarehousesController.Update (WarehousesController.cs:39) | Mgr/Admin | UpsertWarehouseRequest | WarehouseResponse | IWarehouseService | Cập nhật kho mới |
| DELETE | /api/v1/warehouses/{id} | WarehousesController.Delete (WarehousesController.cs:48) | Mgr/Admin | None | void | IWarehouseService | Xóa kho |

---

## 4. Bảng Chuyên biệt về Tồn kho (Inventory & Stock Tracking)

| Endpoint | Ghi Product.CurrentStock? | Ghi WarehouseStocks? | Ghi StockLedger (có WarehouseId)? | Có giao dịch? | Có khóa FOR UPDATE? | Bằng chứng (file:dòng) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Tạo/Sửa/Xóa sản phẩm** (ProductsController) | Không (InitialStock = 0) | Không | Không | Không | Không | ProductService.cs:82-112, ProductService.cs:114 |
| **Nhập kho (StockIn) tạo/sửa** (StockInsController) | Không | Không | Không | Không | Không | StockInService.cs:80, StockInService.cs:220 |
| **Nhập kho (StockIn) chốt (Confirm)** (StockInsController) | Có | Có | Có (Có WarehouseId) | Có (ExecutionStrategy + Transaction) | Có (Products + WarehouseStocks) | StockInService.cs:340-480 |
| **Nhập kho (StockIn) hủy** (StockInsController) | Có | Có | Có (Có WarehouseId) | Có | Có | StockInService.cs:760-820 |
| **Bán hàng (CreateOrder)** (OrdersController) | Có (Thành phẩm + BOM nguyên liệu) | Có | Có (Có WarehouseId) | Có (ExecutionStrategy + Transaction) | Có (Products + WarehouseStocks + LoyaltyAccounts) | OrderService.cs:134-560 |
| **Hủy đơn (CancelOrder)** (OrdersController) | Có (Thành phẩm + BOM nguyên liệu) | Có | Có (Có WarehouseId) | Có (ExecutionStrategy + Transaction) | Có (Orders + WarehouseStocks) | OrderService.cs:923-1070 |
| **Trả hàng khách (Returns)** (ReturnsController) | Có | Có | Có (Có WarehouseId) | Có | Có | ReturnService.cs:270-320 |
| **Trả nhà cung cấp (SupplierReturns) tạo** (SupplierReturnsController) | Không | Không | Không | Không | Không | SupplierReturnService.cs:30-100 |
| **Trả nhà cung cấp (SupplierReturns) duyệt (Confirm)** (SupplierReturnsController) | Không | Có | Có (Có WarehouseId) | Có | Không | SupplierReturnService.cs:160-185 |
| **Trả nhà cung cấp (SupplierReturns) hủy** | Không có endpoint | Không có endpoint | Không có endpoint | Không có endpoint | Không có endpoint | Không có endpoint |
| **Kiểm kê / Điều chỉnh kho (Inventory Adjust)** (InventoryController) | Có | Có | Có (Có WarehouseId) | Có | Có | InventoryService.cs:200-220 |
| **Kiểm kê (StockTakes) duyệt (Confirm)** (StockTakesController) | Không (Chỉ ghi WarehouseStocks) | Có | Có (Có WarehouseId) | Không (Transaction ngầm) | Không | StockTakeService.cs:126-176 |
| **Chuyển kho nội bộ** (WarehouseTransfersController) | Không | Có (Trừ kho đi, cộng kho đến) | Có (Có WarehouseId) | Có | Có | WarehouseTransferService.cs:140-190 |
| **Sản xuất / BOM (Recipes)** | Không | Không | Không | Không | Không | RecipesController.cs |
| **InventoryReconciler.ReconcileAsync** | Không | Có (chỉ khi diff > 0, cộng vào kho mặc định) | Có | Không | Có | InventoryReconciler.cs:32-82 |

---

## 5. Danh sách Mọi Chỗ Ghi `CurrentStock` (Vế trái phép gán, loại bỏ test)

1. `pos-api/src/PosApi/Services/InventoryService.cs:211` (`InventoryService.AdjustStockAsync`) — **Có** đi kèm ghi `WarehouseStocks` (`InventoryService.cs:202`).
2. `pos-api/src/PosApi/Services/OrderService.cs:436` (`OrderService.CreateOrderAsync`) — **Có** đi kèm ghi `WarehouseStocks` (`OrderService.cs:434`).
3. `pos-api/src/PosApi/Services/OrderService.cs:456` (`OrderService.CreateOrderAsync` - BOM nguyên liệu) — **Có** đi kèm ghi `WarehouseStocks` (`OrderService.cs:465`).
4. `pos-api/src/PosApi/Services/OrderService.cs:1006` (`OrderService.CancelOrderAsync`) — **Có** đi kèm ghi `WarehouseStocks` (`OrderService.cs:1015`).
5. `pos-api/src/PosApi/Services/OrderService.cs:1039` (`OrderService.CancelOrderAsync` - BOM nguyên liệu) — **Có** đi kèm ghi `WarehouseStocks` (`OrderService.cs:1047`).
6. `pos-api/src/PosApi/Services/ReturnService.cs:282` (`ReturnService.CreateReturnAsync`) — **Có** đi kèm ghi `WarehouseStocks` (`ReturnService.cs:294`).
7. `pos-api/src/PosApi/Services/StockInService.cs:454` (`StockInService.ConfirmStockInAsync`) — **Có** đi kèm ghi `WarehouseStocks` (`StockInService.cs:463`).
8. `pos-api/src/PosApi/Services/StockInService.cs:795` (`StockInService.CancelStockInAsync`) — **Có** đi kèm ghi `WarehouseStocks` (`StockInService.cs:798`).

---

## 6. Danh sách Mọi Chỗ Ghi `WarehouseStocks.Stock` (Vế trái phép gán, loại bỏ test)

1. `pos-api/src/PosApi/Services/InventoryService.cs:202` (`InventoryService.AdjustStockAsync`)
2. `pos-api/src/PosApi/Services/OrderService.cs:434` (`OrderService.CreateOrderAsync`)
3. `pos-api/src/PosApi/Services/OrderService.cs:465` (`OrderService.CreateOrderAsync`)
4. `pos-api/src/PosApi/Services/OrderService.cs:1015` (`OrderService.CancelOrderAsync`)
5. `pos-api/src/PosApi/Services/OrderService.cs:1047` (`OrderService.CancelOrderAsync`)
6. `pos-api/src/PosApi/Services/ReturnService.cs:294` (`ReturnService.CreateReturnAsync`)
7. `pos-api/src/PosApi/Services/StockInService.cs:463` (`StockInService.ConfirmStockInAsync`)
8. `pos-api/src/PosApi/Services/StockInService.cs:798` (`StockInService.CancelStockInAsync`)
9. `pos-api/src/PosApi/Services/StockTakeService.cs:156` (`StockTakeService.ConfirmStockTakeAsync`)
10. `pos-api/src/PosApi/Services/SupplierReturnService.cs:169` (`SupplierReturnService.ConfirmSupplierReturnAsync`)
11. `pos-api/src/PosApi/Services/WarehouseTransferService.cs:158`, `169` (`WarehouseTransferService.ConfirmTransferAsync`)

---

## 7. Service / Hàm Public Không Được Gọi Từ Controller
- `InventoryReconciler.ReconcileAsync` (`pos-api/src/PosApi/Services/InventoryReconciler.cs:12`).
  - **Bằng chứng:** Không có lời gọi nào trong `src`; chỉ được gọi từ test (`B1EvidenceTests.cs`, `StockInServiceTests.cs`, `OrderServiceTests.cs`, `SupplierDebtFeatureTests.cs`). Migration `AddQ14ReconciliationAndQ13Cost` thực hiện việc tương tự bằng SQL riêng, không gọi hàm này.

---

## 8. Kết luận ngắn cho Q29
1. **Nguồn lệch tồn kho (`CurrentStock` vs `SUM(WarehouseStocks)`)**: 
   - Nguồn 1: Các đơn hàng bán trước bản sửa (`CurrentStock < SUM(WarehouseStocks)`).
   - Nguồn 2: `StockTakeService.ConfirmStockTakeAsync` cập nhật `WarehouseStocks` nhưng không cập nhật `CurrentStock`, hoặc việc hủy đơn cũ không có `WarehouseId` (`OrderService.cs:1032`) chỉ hoàn `CurrentStock` mà không hoàn `WarehouseStocks` (`CurrentStock > SUM(WarehouseStocks)`).
2. **Endpoint bị chặn bán**: `StockInService.ConfirmStockInAsync` (`StockInService.cs:369-371`) ném `BusinessRuleException` chặn chốt phiếu nhập kho nếu `prod.CurrentStock != totalWh`.
3. **Công cụ cân bằng/reset**: Đã có `InventoryReconciler.ReconcileAsync` và migration backfill Q14, nhưng chưa có endpoint quản trị gọi trực tiếp từ UI.
