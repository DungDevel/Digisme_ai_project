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
