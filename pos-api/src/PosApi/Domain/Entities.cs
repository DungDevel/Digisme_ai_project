namespace PosApi.Domain;

// ─────────────────────────── Enums ───────────────────────────

public enum UserRole { Cashier = 0, Manager = 1 }

public enum OrderStatus { Pending = 0, Completed = 1, Held = 2, Cancelled = 3, PartiallyReturned = 4 }

public enum PaymentMethod { Cash = 0, Debt = 1, VietQr = 2, BankTransfer = 3 }

public enum PaymentStatus { Pending = 0, Paid = 1, Failed = 2, Expired = 3 }

public enum SurchargeType { Percent = 0, Fixed = 1 }

public enum SurchargeApply { Auto = 0, Manual = 1 }

public enum DebtStatus { Open = 0, PartiallyPaid = 1, Settled = 2 }

public enum ShiftStatus { Open = 0, Closed = 1 }

public enum SupplierInvoiceStatus { PendingReview = 0, Confirmed = 1, Rejected = 2 }

public enum LedgerRefType { Sale = 0, Return = 1, Purchase = 2, Adjustment = 3, OcrStockIn = 4 }

// ─────────────────────────── Auth ───────────────────────────

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;      // unique
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;          // unique
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Cashier;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Shift> Shifts { get; set; } = new();
    public List<Order> Orders { get; set; } = new();
}

public class PasswordResetCode
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ─────────────────────────── Catalog ───────────────────────────

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<Product> Products { get; set; } = new();
}

public class Unit
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;   // lon, lốc, thùng...
}

public class ProductUnit
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int UnitId { get; set; }
    public Unit Unit { get; set; } = null!;
    /// Quy đổi về đơn vị gốc: 1 unit này = X đơn vị gốc (gốc = 1)
    public decimal ConversionRate { get; set; } = 1m;
    public bool IsBase { get; set; }
    /// Barcode riêng cho đơn vị đóng gói (lốc/thùng); null nếu scan về đơn vị gốc
    public string? Barcode { get; set; }
}

public class Surcharge
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;      // VAT 10%, phí đóng gói, phụ thu Tết...
    public SurchargeType Type { get; set; }
    public decimal Value { get; set; }                    // % hoặc VNĐ
    public SurchargeApply ApplyMode { get; set; }
    public DateTime? ActiveFrom { get; set; }
    public DateTime? ActiveUntil { get; set; }            // cho phụ thu lễ/tết
    public bool IsActive { get; set; } = true;
}

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string Barcode { get; set; } = string.Empty;   // unique
    public decimal CostPrice { get; set; }
    public decimal RetailPrice { get; set; }
    public decimal WholesalePrice { get; set; }
    public decimal MemberPrice { get; set; }
    /// decimal(18,3) — hỗ trợ hàng cân (kg: 0.250)
    public decimal MinStock { get; set; }
    /// Cache hiện giá — nguồn sự thật là StockLedger. decimal(18,3)
    public decimal CurrentStock { get; set; }
    /// true nếu bán lẻ theo cân (tối đa 3 chữ số thập phân)
    public bool IsWeighed { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public List<ProductUnit> Units { get; set; } = new();
}

// ─────────────────────────── Customer / CRM ───────────────────────────

public class Customer
{
    public int Id { get; set; }
    public string Phone { get; set; } = string.Empty;     // unique
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public LoyaltyAccount? Loyalty { get; set; }
    public List<Debt> Debts { get; set; } = new();
}

public class LoyaltyAccount
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public int Points { get; set; }
    public string Tier { get; set; } = "Silver";          // Silver / Gold / Platinum
    public decimal LifetimeSpend { get; set; }
}

public class Debt
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public DebtStatus Status { get; set; } = DebtStatus.Open;
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ─────────────────────────── Shift ───────────────────────────

public class Shift
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public ShiftStatus Status { get; set; } = ShiftStatus.Open;
    public decimal OpeningCash { get; set; }
    public decimal? ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Diff { get; set; }
    public List<Order> Orders { get; set; } = new();
}

// ─────────────────────────── Orders ───────────────────────────

public class Order
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;      // unique, VD: INV-20260919-0001
    public int CashierId { get; set; }
    public User Cashier { get; set; } = null!;
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal SurchargeTotal { get; set; }
    public decimal Vat { get; set; }
    public decimal Total { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal Change { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public List<OrderItem> Items { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int? UnitId { get; set; }          // đơn vị đã bán (khác gốc thì có conversion)
    public decimal Qty { get; set; }          // quy về đơn vị gốc, decimal(18,3)
    public decimal UnitPrice { get; set; }    // đơn giá tại thời điểm bán (đơn vị gốc)
    public decimal Surcharge { get; set; }
    public decimal Total { get; set; }
    public decimal ReturnedQty { get; set; }
    public bool Cancelled { get; set; }
}

public class HeldCart
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? CustomerId { get; set; }
    public string Note { get; set; } = string.Empty;
    /// Snapshot giỏ: [{productId, unitId, qty, unitPrice}]
    public string ItemsJson { get; set; } = "[]";
    public decimal Subtotal { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResumedAt { get; set; }
}

// ─────────────────────────── Returns ───────────────────────────

public class ReturnRecord
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;      // PHTRA-...
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public string? Reason { get; set; }
    public decimal TotalReturned { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ReturnItem> Items { get; set; } = new();
}

public class ReturnItem
{
    public int Id { get; set; }
    public int ReturnId { get; set; }
    public ReturnRecord Return { get; set; } = null!;
    public int OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;
    public decimal Qty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}

// ─────────────────────────── Stock ledger ───────────────────────────

/// Append-only — DB trigger chặn UPDATE/DELETE
public class StockLedgerEntry
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal Qty { get; set; }           // ±, decimal(18,3)
    public decimal Balance { get; set; }       // running balance, decimal(18,3)
    public LedgerRefType RefType { get; set; }
    public string? RefId { get; set; }           // mã order / return / stock-in
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ─────────────────────────── Audit ───────────────────────────

/// Append-only, bất biến (trigger chặn UPDATE/DELETE)
public class PosLog
{
    public long Id { get; set; }
    public string Action { get; set; } = string.Empty;   // ITEM_CANCELED, PRICE_OVERRIDDEN, DRAWER_OPENED_NO_SALE, REPRINT...
    public int? UserId { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? DetailJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ─────────────────────────── Payments (VietQR) ───────────────────────────

public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }        // mã VietQR / nội dung chuyển khoản
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTime? PaidAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ─────────────────────────── AI Vision: supplier invoice OCR ───────────────────────────

public class SupplierInvoice
{
    public int Id { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    /// JSON do FastAPI trả: {items:[{name, unit, qty, unitPrice, confidence}], overallConfidence}
    public string? OcrResultJson { get; set; }
    public decimal? OcrConfidence { get; set; }
    public SupplierInvoiceStatus Status { get; set; } = SupplierInvoiceStatus.PendingReview;
    public int? StockInId { get; set; }
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public StockIn? StockIn { get; set; }
}

public class StockIn
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;      // PHNK-...
    public int SupplierInvoiceId { get; set; }
    public SupplierInvoice SupplierInvoice { get; set; } = null!;
    public string? SupplierName { get; set; }
    public decimal Total { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<StockInItem> Items { get; set; } = new();
}

public class StockInItem
{
    public int Id { get; set; }
    public int StockInId { get; set; }
    public StockIn StockIn { get; set; } = null!;
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal Qty { get; set; }
    public decimal UnitCost { get; set; }
    public decimal Total { get; set; }
}

// ─────────────────────────── Store setting ───────────────────────────

/// Dòng duy nhất (Id=1) — thông tin cửa hàng, KHÔNG hard-code trong API
public class StoreSetting
{
    public int Id { get; set; } = 1;
    public string StoreName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    /// Tài khoản VietQR: ngân hàng + số tài khoản + tên người nhận
    public string VietQrBankCode { get; set; } = string.Empty;
    public string VietQrAccountNo { get; set; } = string.Empty;
    public string VietQrAccountName { get; set; } = string.Empty;
    /// Giá trong DB đã gồm VAT — đây là thuế suất % dùng để tách cột Vat hiển thị
    public decimal VatRatePercent { get; set; } = 10m;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ─────────────────────────── Idempotency ───────────────────────────

/// Chống tạo đơn/trả hàng trùng khi client retry (F9, network retry)
public class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;          // header Idempotency-Key
    public int UserId { get; set; }
    public string Endpoint { get; set; } = string.Empty;     // vd: POST /api/v1/orders
    public string? ResultJson { get; set; }                  // response của lần chạy đầu
    public int StatusCode { get; set; } = 200;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}

// ─────────────────────────── Invoice numbering ───────────────────────────

/// Counter mã đơn INV-yyyymmdd-nnnn theo ngày (UTC).
/// Row lock (upsert ... RETURNING) → nnnn không trùng khi concurrent.
public class InvoiceDayCounter
{
    public string Day { get; set; } = string.Empty;          // yyyy-MM-dd (UTC)
    public int N { get; set; }
}
