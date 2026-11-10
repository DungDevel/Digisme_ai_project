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
public record ProductCreateRequest(string Name, int CategoryId, string Barcode, decimal CostPrice, decimal RetailPrice, decimal WholesalePrice, decimal MemberPrice, decimal MinStock, bool IsWeighed, string? ImageUrl);
public record ProductUpdateRequest(string Name, int CategoryId, decimal CostPrice, decimal RetailPrice, decimal WholesalePrice, decimal MemberPrice, decimal MinStock, bool IsWeighed, string? ImageUrl, bool IsActive);
public record ProductLookupResponse(
    int Id, string Name, string Barcode, int CategoryId, string CategoryName,
    decimal RetailPrice, decimal WholesalePrice, decimal MemberPrice,
    decimal Stock, string BaseUnit, bool BelowMinStock, bool IsWeighed, string? ImageUrl);

public record PagedProductResponse(int Page, int PageSize, int Total, int TotalPages, List<ProductBrief> Items);
public record ProductBrief(int Id, string Name, string Barcode, string CategoryName, decimal RetailPrice, decimal Stock, string BaseUnit, string? ImageUrl);

// ── Orders (Sprint 1) ──
public record OrderSurchargeRequest(int SurchargeId, decimal? OverrideValue);
public record OrderItemRequest(int ProductId, int? UnitId, decimal Qty, decimal? UnitPrice);
public record CreateOrderRequest(int? CustomerId, string PaymentMethod, decimal PaidAmount, decimal Discount,
    List<OrderSurchargeRequest> Surcharges, List<OrderItemRequest> Items, string? DeliveryMethod = "instore", decimal? ShippingFee = 0m);
public record OrderUpdateRequest(int? CustomerId, string? Note, List<OrderItemRequest> Items, decimal? Discount);
public record OrderImageUploadRequest(string ImageType, IFormFile File);
public record OrderImageResponse(int Id, string ImageUrl, string ImageType, DateTime CreatedAt);
public record OrderLineResponse(int Id, int ProductId, string ProductName, int? UnitId, string UnitName,
    decimal Qty, decimal UnitPrice, decimal LineTotal, bool IsWeighed);
/// <summary>
/// OrderCreatedResponse — Nhóm 1 (Đơn Bán Hàng chiều sâu) bổ sung:
///  VipDiscount        — tổng giảm giá VIP (đã cộng vào Discount)
///  PointsRedeemed     — điểm khách đã tiêu (0 nếu không tiêu)
///  PointsReduction    — tiền giảm tương ứng (1 điểm = 1.000đ)
///  DebtCollected     — tổng công nợ cũ thu tại đơn
///  DeliveryMethod    — instore | shopdelivery | carrier
///  ShippingFee       — phí giao tính server (0 với instore)
/// </summary>
public record DebtPayResult(int DebtId, decimal AmountPaid, decimal RemainingAmount, string Status);

/// <summary>
/// OrderCreatedResponse — Nhóm 1 (Đơn Bán Hàng chiều sâu) bổ sung:
///  VipDiscount        — tổng giảm giá VIP (đã cộng vào Discount)
///  PointsRedeemed     — điểm khách đã tiêu (0 nếu không tiêu)
///  PointsReduction    — tiền giảm tương ứng (1 điểm = 1.000đ)
///  DebtCollected     — tổng công nợ cũ thu tại đơn
///  DeliveryMethod    — instore | shopdelivery | carrier
///  ShippingFee       — phí giao tính server (0 với instore)
/// </summary>
public record OrderCreatedResponse(
    int Id, string Code, decimal Subtotal, decimal Discount, decimal SurchargeTotal,
    decimal Vat, decimal Total, string PaymentMethod, decimal PaidAmount, decimal Change, decimal AmountOwed,
    string Status, int? LoyaltyEarned, DateTime CompletedAt, List<OrderLineResponse> Items, List<string> StockWarnings,
    decimal VipDiscount = 0m, int? PointsRedeemed = 0, decimal PointsReduction = 0m, decimal DebtCollected = 0m,
    List<DebtPayResult>? DebtPayments = null,
    string DeliveryMethod = "instore", decimal ShippingFee = 0m)
{
    public List<DebtPayResult> DebtPayments { get; init; } = DebtPayments ?? new();
}

public record OrderSummaryResponse(
    int Id,
    string Code,
    int CashierId,
    string CashierName,
    int? CustomerId,
    string? CustomerName,
    int? ShiftId,
    decimal Subtotal,
    decimal Discount,
    decimal SurchargeTotal,
    decimal Vat,
    decimal ShippingFee,
    decimal Total,
    string PaymentMethod,
    decimal PaidAmount,
    decimal Change,
    string Status,
    int ItemCount,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public record PagedOrderResponse(
    int Page,
    int PageSize,
    int Total,
    int TotalPages,
    List<OrderSummaryResponse> Items);

public record OrderItemDetailResponse(
    int OrderItemId,
    int ProductId,
    string ProductName,
    string Barcode,
    int? UnitId,
    string UnitName,
    decimal Qty,
    decimal UnitPrice,
    decimal LineDiscount,
    decimal Total,
    bool IsWeighed);

public record OrderPaymentBriefResponse(
    int Id,
    string Method,
    decimal Amount,
    string Status,
    string? Reference,
    DateTime? PaidAt);

public record OrderDetailResponse(
    int Id,
    string Code,
    int CashierId,
    string CashierName,
    int? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    int? ShiftId,
    decimal Subtotal,
    decimal Discount,
    decimal SurchargeTotal,
    decimal Vat,
    decimal ShippingFee,
    decimal Total,
    string PaymentMethod,
    decimal PaidAmount,
    decimal Change,
    string DeliveryMethod,
    string Status,
    int LoyaltyEarned,
    int LoyaltyRevoked,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    List<OrderItemDetailResponse> Items,
    List<OrderPaymentBriefResponse> Payments);

// ── Hold cart (Sprint 1) ──
public record HoldCartRequest(int? CustomerId, string? Note, List<OrderItemRequest> Items);
public record HeldCartResponse(int Id, int? CustomerId, string Note, decimal Subtotal, DateTime CreatedAt, DateTime ExpiresAt);
public record HeldCartListResponse(List<HeldCartResponse> Items);
public record ResumeCartResponse(int Id, int? CustomerId, string Note, decimal Subtotal, List<OrderItemRequest> Items, bool StockWarning);

// ── Customers (Sprint 1) ──
public record CustomerCreateRequest(string Phone, string Name, string? Address);
public record CustomerUpdateRequest(string Name, string? Address, bool IsActive);
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
public record ConfirmStockInRequest(int InvoiceId, List<StockInItemRequest> Items, string? SupplierName, string? Note, int? SupplierId = null);
public record StockInCreatedResponse(int Id, string Code, decimal Total, DateTime CreatedAt);

// ── Shifts (Sprint 3) ──
public record OpenShiftRequest(decimal OpeningCash);
public record CloseShiftRequest(decimal CountedCash, decimal? CountedTransfer = null, Dictionary<int, int>? Denominations = null, string? Note = null);
public record ShiftResponse(int Id, int UserId, string Username, DateTime StartedAt, DateTime? EndedAt, string Status, decimal OpeningCash, decimal ExpectedCash, decimal CountedCash, decimal Diff, decimal TransferSales, decimal? CountedTransfer, decimal? TransferDiff, string? DenominationsJson, string? Note);
public record PagedShiftResponse(int Page, int PageSize, int Total, int TotalPages, List<ShiftResponse> Items);
public record ShiftOrderBrief(int Id, string Code, decimal Total, string PaymentMethod, DateTime CreatedAt);
public record ShiftSummaryResponse(
    int ShiftId,
    int UserId,
    string Username,
    DateTime StartedAt,
    decimal OpeningCash,
    decimal CashSales,
    decimal TransferSales,
    decimal CashReceipts,
    decimal CashPayments,
    decimal ExpectedCash,
    decimal ExpectedTransfer,
    int TotalOrders,
    List<ShiftOrderBrief> Orders
);

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

// ── Customer Groups & Suppliers (ADR 0001) ──
public record CustomerGroupCreateRequest(string Code, string Name, bool IsSupplier, decimal DefaultDiscountRate, int? ParentId);
public record CustomerGroupUpdateRequest(string Name, decimal DefaultDiscountRate, int? ParentId, bool IsSupplier);
public record CustomerGroupResponse(int Id, string Code, string Name, bool IsSupplier, decimal DefaultDiscountRate, int? ParentId, string? ParentName, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);
public record PagedCustomerGroupResponse(int Page, int PageSize, int Total, int TotalPages, List<CustomerGroupResponse> Items);

public record SupplierCreateRequest(string? Code, string Name, string? Phone, string? Address, string? TaxCode, int? CustomerGroupId = null);
public record SupplierUpdateRequest(string Name, string? Phone, string? Address, string? TaxCode, int? CustomerGroupId = null);
public record SupplierResponse(int Id, string Code, string Name, string? Phone, string? Address, string? TaxCode, int? CustomerGroupId, string? CustomerGroupName, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);
public record PagedSupplierResponse(int Page, int PageSize, int Total, int TotalPages, List<SupplierResponse> Items);

// ── F&B (Phân hệ D) ──
public record AreaCreateRequest(string Code, string Name, string? Description, int SortOrder = 0);
public record AreaUpdateRequest(string Name, string? Description, int SortOrder, bool IsActive);
public record AreaResponse(int Id, string Code, string Name, string? Description, int SortOrder, bool IsActive, int TableCount, DateTime CreatedAt, DateTime? UpdatedAt);
public record PagedAreaResponse(int Page, int PageSize, int Total, int TotalPages, List<AreaResponse> Items);

public record TableCreateRequest(string Code, string Name, int AreaId, int Capacity = 4);
public record TableUpdateRequest(string Name, int AreaId, int Capacity, bool IsActive);
public record TableStatusUpdateRequest(TableStatus Status);
public record TransferTableRequest(int TargetTableId);
public record TableResponse(int Id, string Code, string Name, int AreaId, string AreaName, int Capacity, TableStatus Status, int? CurrentOrderId, string? CurrentOrderCode, bool IsActive);
public record PagedTableResponse(int Page, int PageSize, int Total, int TotalPages, List<TableResponse> Items);

public record RoomCreateRequest(string Code, string Name, decimal HourlyRate);
public record RoomUpdateRequest(string Name, decimal HourlyRate, bool IsActive);
public record RoomCheckInRequest(int? CustomerId);
public record RoomCheckOutResponse(int RoomId, string RoomCode, decimal MinutesUsed, decimal HoursUsed, decimal Amount, int OrderId, string OrderCode);
public record RoomResponse(int Id, string Code, string Name, decimal HourlyRate, RoomStatus Status, DateTime? StartTime, int? CurrentOrderId, string? CurrentOrderCode, bool IsActive);
public record PagedRoomResponse(int Page, int PageSize, int Total, int TotalPages, List<RoomResponse> Items);

public record ProductRecipeItemRequest(int IngredientId, decimal Qty, int? UnitId);
public record ProductRecipeUpsertRequest(List<ProductRecipeItemRequest> Items);
public record ProductRecipeResponse(int Id, int ProductId, string ProductName, int IngredientId, string IngredientName, decimal Qty, int? UnitId, string? UnitName);

// ─────────────────────────── Multi-warehouse DTOs ───────────────────────────

public record WarehouseCreateRequest(string Code, string Name, string? Address, bool IsDefault);
public record WarehouseUpdateRequest(string Name, string? Address, bool IsDefault, bool IsActive);
public record WarehouseResponse(int Id, string Code, string Name, string? Address, bool IsDefault, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt);
public record PagedWarehouseResponse(int Page, int PageSize, int Total, int TotalPages, List<WarehouseResponse> Items);

public record WarehouseStockResponse(long Id, int WarehouseId, string WarehouseName, int ProductId, string ProductBarcode, string ProductName, decimal Stock, DateTime? UpdatedAt);
public record PagedWarehouseStockResponse(int Page, int PageSize, int Total, int TotalPages, List<WarehouseStockResponse> Items);

public record WarehouseTransferItemRequest(int ProductId, decimal Quantity, decimal UnitCost, string? Note);
public record WarehouseTransferCreateRequest(int FromWarehouseId, int ToWarehouseId, string? Note, List<WarehouseTransferItemRequest> Items);
public record WarehouseTransferItemResponse(int Id, int ProductId, string ProductBarcode, string ProductName, decimal Quantity, decimal UnitCost);
public record WarehouseTransferResponse(int Id, string Code, int FromWarehouseId, string FromWarehouseName, int ToWarehouseId, string ToWarehouseName, TransferStatus Status, string? Note, int CreatedBy, DateTime CreatedAt, List<WarehouseTransferItemResponse> Items);
public record PagedWarehouseTransferResponse(int Page, int PageSize, int Total, int TotalPages, List<WarehouseTransferResponse> Items);

public record StockTakeItemRequest(int ProductId, decimal SystemQty, decimal ActualQty, string? Note);
public record StockTakeCreateRequest(int WarehouseId, string? Note, List<StockTakeItemRequest> Items);
public record StockTakeItemResponse(int Id, int ProductId, string ProductBarcode, string ProductName, decimal SystemQty, decimal ActualQty, decimal DeltaQty);
public record StockTakeResponse(int Id, string Code, int WarehouseId, string WarehouseName, StockTakeStatus Status, string? Note, int CreatedBy, DateTime CreatedAt, List<StockTakeItemResponse> Items);
public record PagedStockTakeResponse(int Page, int PageSize, int Total, int TotalPages, List<StockTakeResponse> Items);

public record SupplierReturnItemRequest(int ProductId, decimal Qty, decimal UnitCost, string? Note);
public record SupplierReturnCreateRequest(int SupplierId, int WarehouseId, string? Note, List<SupplierReturnItemRequest> Items);
public record SupplierReturnItemResponse(int Id, int ProductId, string ProductBarcode, string ProductName, decimal Qty, decimal UnitCost, decimal Total);
public record SupplierReturnResponse(int Id, string Code, int SupplierId, string SupplierName, int WarehouseId, string WarehouseName, SupplierReturnStatus Status, decimal Total, string? Note, int CreatedBy, DateTime CreatedAt, List<SupplierReturnItemResponse> Items);
public record PagedSupplierReturnResponse(int Page, int PageSize, int Total, int TotalPages, List<SupplierReturnResponse> Items);

// ─────────────────────────── Finance DTOs ───────────────────────────

public record CashVoucherCreateRequest(CashVoucherType Type, PartnerType PartnerType, int? PartnerId, decimal Amount, PaymentMethod PaymentMethod, string? ReferenceType, string? ReferenceId, int? ShiftId, string? Note);
public record CashVoucherResponse(int Id, string Code, CashVoucherType Type, PartnerType PartnerType, int? PartnerId, string? PartnerName, decimal Amount, PaymentMethod PaymentMethod, string? ReferenceType, string? ReferenceId, int? ShiftId, string? Note, CashVoucherStatus Status, int CreatedBy, DateTime CreatedAt, DateTime? ConfirmedAt);
public record PagedCashVoucherResponse(int Page, int PageSize, int Total, int TotalPages, List<CashVoucherResponse> Items);

public record CashFundBookSummaryResponse(decimal OpeningBalance, decimal TotalReceipts, decimal TotalPayments, decimal ClosingBalance);
public record CashLedgerItemResponse(long Id, decimal Amount, decimal Balance, PaymentMethod PaymentMethod, string RefType, string? RefId, string? Note, int CreatedBy, DateTime CreatedAt);
public record PagedCashLedgerResponse(int Page, int PageSize, int Total, int TotalPages, decimal OpeningBalance, decimal ClosingBalance, List<CashLedgerItemResponse> Items);

public record GeneralJournalItemResponse(string Id, DateTime Timestamp, string VoucherType, string Code, string Description, decimal Amount, string PartnerName, string CreatedBy);
public record PagedGeneralJournalResponse(int Page, int PageSize, int Total, int TotalPages, List<GeneralJournalItemResponse> Items);

public record SupplierDebtResponse(int Id, int SupplierId, string SupplierName, decimal Amount, decimal PaidAmount, decimal RemainingAmount, string Status, string? ReferenceType, string? ReferenceId, string? Note, DateTime CreatedAt);
public record PagedSupplierDebtResponse(int Page, int PageSize, int Total, int TotalPages, List<SupplierDebtResponse> Items);
public record PaySupplierDebtRequest(decimal Amount, string? Note);
public record PaySupplierDebtResponse(int SupplierDebtId, decimal Amount, decimal PaidAmount, decimal RemainingAmount, string Status);

// ─────────────────────────── Reports Đợt 2 DTOs ───────────────────────────

public record TaxDeclarationReportResponse(
    DateTime StartDate,
    DateTime EndDate,
    decimal TotalSalesRevenue,
    decimal TotalOutputVat,
    decimal TotalPurchaseAmount,
    decimal TotalInputVat
);

public record AdvancedDashboardResponse(
    DateTime StartDate,
    DateTime EndDate,
    decimal TotalRevenue,
    decimal TotalCostOfGoodsSold,
    decimal GrossProfit,
    decimal GrossProfitMarginPercent,
    List<PaymentMethodSummary> PaymentMethods,
    List<TopProductReportItem> TopSelling,
    List<InventoryItemBrief> LowStockItems,
    decimal CurrentCashBalance
);

public record SlowMovingProductItem(int ProductId, int CategoryId, string CategoryName, string ProductName, string Barcode, decimal CurrentStock, decimal CostPrice, decimal TotalValue);
public record SlowMovingReportResponse(DateTime StartDate, DateTime EndDate, List<SlowMovingProductItem> Items);

public record CashFlowReportResponse(
    DateTime StartDate,
    DateTime EndDate,
    decimal OpeningBalance,
    decimal TotalReceipts,
    decimal TotalPayments,
    decimal ClosingBalance,
    List<CashVoucherResponse> Vouchers
);
