using PosApi.Domain;

namespace PosApi.Dto;

// ── Auth ──
public record RegisterRequest(string Username, string FullName, string Email, string Password);
public record LoginRequest(string Username, string Password);
public record ForgotPasswordRequest(string Email);
public record ResetPasswordRequest(string Email, string Code, string NewPassword);

public record AuthResponse(string AccessToken, string Username, string FullName, UserRole Role, long ExpiresAtUnix);
public record ForgotPasswordResponse(string Code, int ValidMinutes);

// ── Products (mẫu Sprint 1) ──
public record ProductLookupResponse(
    int Id, string Name, string Barcode, int CategoryId, string CategoryName,
    decimal RetailPrice, decimal WholesalePrice, decimal MemberPrice,
    int Stock, string BaseUnit, bool BelowMinStock);

public record PagedProductResponse(int Page, int PageSize, int Total, int TotalPages, List<ProductBrief> Items);
public record ProductBrief(int Id, string Name, string Barcode, string CategoryName, decimal RetailPrice, int Stock, string BaseUnit);
