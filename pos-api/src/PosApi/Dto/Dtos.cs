using PosApi.Domain;

namespace PosApi.Dto;

// ── Auth ──
public record RegisterRequest(string Username, string FullName, string Email, string Password);
public record LoginRequest(string Username, string Password);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Code, string NewPassword);

public record AuthResponse(string AccessToken, string Username, string FullName, UserRole Role, long ExpiresAtUnix);
// BE-P0-06: mã reset KHÔNG còn trả trong response — chỉ thông báo + xem mã tại log server (demo, chưa có email gateway)
public record ForgotPasswordResponse(string Message, int ValidMinutes);

// ── Products (mẫu Sprint 1) ──
public record ProductLookupResponse(
    int Id, string Name, string Barcode, int CategoryId, string CategoryName,
    decimal RetailPrice, decimal WholesalePrice, decimal MemberPrice,
    decimal Stock, string BaseUnit, bool BelowMinStock, bool IsWeighed);

public record PagedProductResponse(int Page, int PageSize, int Total, int TotalPages, List<ProductBrief> Items);
public record ProductBrief(int Id, string Name, string Barcode, string CategoryName, decimal RetailPrice, decimal Stock, string BaseUnit);

// ── Orders (Sprint 1) ──
public record OrderSurchargeRequest(int SurchargeId, decimal? OverrideValue);
public record OrderItemRequest(int ProductId, int? UnitId, decimal Qty, decimal? UnitPrice);
public record CreateOrderRequest(int? CustomerId, string PaymentMethod, decimal PaidAmount, decimal Discount,
    List<OrderSurchargeRequest> Surcharges, List<OrderItemRequest> Items);
public record OrderLineResponse(int ProductId, string ProductName, int? UnitId, string UnitName,
    decimal Qty, decimal UnitPrice, decimal LineTotal, bool IsWeighed);
public record OrderCreatedResponse(int Id, string Code, decimal Subtotal, decimal Discount, decimal SurchargeTotal,
    decimal Vat, decimal Total, string PaymentMethod, decimal PaidAmount, decimal Change, decimal AmountOwed,
    string Status, int? LoyaltyEarned, DateTime CompletedAt, List<OrderLineResponse> Items, List<string> StockWarnings);

// ── Hold cart (Sprint 1) ──
public record HoldCartRequest(int? CustomerId, string? Note, List<OrderItemRequest> Items);
public record HeldCartResponse(int Id, int? CustomerId, string Note, decimal Subtotal, DateTime CreatedAt, DateTime ExpiresAt);
public record HeldCartListResponse(List<HeldCartResponse> Items);
public record ResumeCartResponse(int Id, int? CustomerId, string Note, decimal Subtotal, List<OrderItemRequest> Items, bool StockWarning);

// ── Customers (Sprint 1) ──
public record CustomerCreateRequest(string Phone, string Name, string? Address);
public record CustomerResponse(int Id, string Phone, string Name, string? Address, bool IsActive, int Points, string Tier, decimal TotalDebt);
public record PagedCustomerResponse(int Page, int PageSize, int Total, int TotalPages, List<CustomerResponse> Items);

// ── Debts (P0-4: đường trả nợ — Debt không còn kẹt Open vĩnh viễn) ──
public record PayDebtRequest(decimal Amount, string? Note);
public record DebtResponse(int Id, int CustomerId, string CustomerPhone, string CustomerName,
    decimal Amount, decimal PaidAmount, decimal Remaining, string Status, string? Note, DateTime CreatedAt);
public record PagedDebtResponse(int Page, int PageSize, int Total, int TotalPages, List<DebtResponse> Items);
public record PayDebtResponse(int Id, decimal Amount, decimal PaidAmount, decimal Remaining, string Status);

// ── Returns (M5) ──
public record ReturnItemRequest(int OrderItemId, decimal Qty);
public record ReturnRequest(int OrderId, string? Reason, List<ReturnItemRequest> Items);
public record ReturnResponse(string Code, decimal TotalReturned, DateTime CreatedAt);
public record ReturnItemResponse(int Id, string ProductName, decimal Qty, decimal UnitPrice, decimal Total);
public record ReturnDetailResponse(int Id, string Code, int OrderId, string? Reason, decimal TotalReturned, DateTime CreatedAt, List<ReturnItemResponse> Items);
public record PagedReturnResponse(int Page, int PageSize, int Total, int TotalPages, List<ReturnBrief> Items);
public record ReturnBrief(int Id, string Code, int OrderId, decimal TotalReturned, DateTime CreatedAt);

// ── Inventory (M6) ──
public record InventoryResponse(int ProductId, string ProductName, string Barcode, decimal Stock, string Unit, bool IsLowStock);
public record InventoryItemBrief(int ProductId, string ProductName, string Barcode, decimal Stock, string BaseUnit, bool IsLowStock);
public record PagedInventoryResponse(int Page, int PageSize, int Total, int TotalPages, List<InventoryItemBrief> Items);
public record StockHistoryResponse(int ProductId, string ProductName, decimal CurrentStock, List<StockLedgerEntryBrief> History);
public record StockLedgerEntryBrief(int Id, DateTime CreatedAt, decimal Qty, decimal Balance, string RefType, string RefId, string Note);
public record InventoryAdjustRequest(decimal Amount, string Reason);

// ── Stock-In (M7) ──
public record StockInItemRequest(int ProductId, decimal Qty, decimal UnitCost);
public record ConfirmStockInRequest(int InvoiceId, List<StockInItemRequest> Items, string? SupplierName, string? Note);
public record StockInCreatedResponse(int Id, string Code, decimal Total, DateTime CreatedAt);

// ── Shifts (Sprint 3) ──
public record OpenShiftRequest(decimal OpeningCash);
public record CloseShiftRequest(decimal CountedCash);
public record ShiftResponse(int Id, int UserId, string Username, DateTime StartedAt, DateTime? EndedAt, string Status, decimal OpeningCash, decimal ExpectedCash, decimal CountedCash, decimal Diff);
public record PagedShiftResponse(int Page, int PageSize, int Total, int TotalPages, List<ShiftResponse> Items);

// ── Reports (Sprint 3) ──
public record SalesReportResponse(
    DateTime FromDate, DateTime ToDate,
    int TotalOrders,
    decimal Subtotal,
    decimal Discount,
    decimal SurchargeTotal,
    decimal Vat,
    decimal TotalRevenue,
    decimal TotalPaid
);

public record TopProductReportItem(int ProductId, string ProductName, string Barcode, decimal TotalQtySold, decimal TotalRevenue);
public record TopProductsReportResponse(DateTime FromDate, DateTime ToDate, List<TopProductReportItem> Items);

public record PaymentMethodSummary(string PaymentMethod, int OrderCount, decimal TotalAmount);
public record PaymentMethodsReportResponse(DateTime FromDate, DateTime ToDate, List<PaymentMethodSummary> Items);

// ── Payments & VietQR (Sprint 3) ──
public record GenerateVietQrRequest(int OrderId);
public record VietQrResponse(
    int PaymentId,
    int OrderId,
    string OrderCode,
    decimal Amount,
    string BankCode,
    string AccountNo,
    string AccountName,
    string AddInfo,
    string QrImageUrl,
    string Status
);
public record ConfirmPaymentRequest(PaymentStatus Status, string? Reference);
public record PaymentResponse(
    int Id,
    int OrderId,
    string Method,
    decimal Amount,
    string? Reference,
    string Status,
    DateTime? PaidAt,
    DateTime CreatedAt
);
