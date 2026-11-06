using Microsoft.EntityFrameworkCore;
using PosApi.Domain;

namespace PosApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<PasswordResetCode> PasswordResetCodes => Set<PasswordResetCode>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<ProductUnit> ProductUnits => Set<ProductUnit>();
    public DbSet<Surcharge> Surcharges => Set<Surcharge>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<LoyaltyAccount> LoyaltyAccounts => Set<LoyaltyAccount>();
    public DbSet<PointTransaction> PointTransactions => Set<PointTransaction>();
    public DbSet<Debt> Debts => Set<Debt>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<HeldCart> HeldCarts => Set<HeldCart>();
    public DbSet<ReturnRecord> Returns => Set<ReturnRecord>();
    public DbSet<ReturnItem> ReturnItems => Set<ReturnItem>();
    public DbSet<StockLedgerEntry> StockLedger => Set<StockLedgerEntry>();
    public DbSet<PosLog> PosLogs => Set<PosLog>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<StockIn> StockIns => Set<StockIn>();
    public DbSet<StockInItem> StockInItems => Set<StockInItem>();
    public DbSet<StoreSetting> StoreSettings => Set<StoreSetting>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<InvoiceDayCounter> InvoiceDayCounters => Set<InvoiceDayCounter>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<CustomerGroup> CustomerGroups => Set<CustomerGroup>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // ── User ──
        mb.Entity<User>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.FullName).HasMaxLength(128);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Role).HasConversion<int>();
        });

        mb.Entity<PasswordResetCode>(e =>
        {
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
        });

        // ── Catalog ──
        mb.Entity<Category>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(128);
        });

        mb.Entity<Unit>(e =>
        {
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.Name).HasMaxLength(32);
        });

        mb.Entity<ProductUnit>(e =>
        {
            e.HasKey(x => new { x.ProductId, x.UnitId });
            e.HasOne(x => x.Product).WithMany(x => x.Units).HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Unit).WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.ConversionRate).HasPrecision(18, 4);
            e.Property(x => x.Barcode).HasMaxLength(64);
            e.HasIndex(x => x.Barcode).IsUnique().HasDatabaseName("ix_product_units_barcode");
        });

        mb.Entity<Surcharge>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Value).HasPrecision(18, 4);
            e.Property(x => x.Type).HasConversion<int>();
            e.Property(x => x.ApplyMode).HasConversion<int>();
        });

        mb.Entity<Product>(e =>
        {
            e.HasIndex(x => x.Name);
            e.HasIndex(x => x.Barcode).IsUnique().HasDatabaseName("ix_products_barcode");
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Barcode).HasMaxLength(64);
            e.Property(x => x.CostPrice).HasPrecision(18, 2);
            e.Property(x => x.RetailPrice).HasPrecision(18, 2);
            e.Property(x => x.WholesalePrice).HasPrecision(18, 2);
            e.Property(x => x.MemberPrice).HasPrecision(18, 2);
            e.Property(x => x.MinStock).HasPrecision(18, 3);
            e.Property(x => x.CurrentStock).HasPrecision(18, 3);
            e.HasOne(x => x.Category).WithMany(x => x.Products).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Customer / CRM ──
        mb.Entity<Customer>(e =>
        {
            e.HasIndex(x => x.Phone).IsUnique();
            e.Property(x => x.Phone).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(128);
            e.HasOne(x => x.CustomerGroup)
                .WithMany(x => x.Customers)
                .HasForeignKey(x => x.CustomerGroupId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Customer Group ──
        mb.Entity<CustomerGroup>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.DefaultDiscountRate).HasPrecision(5, 2);
            e.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<LoyaltyAccount>(e =>
        {
            e.HasOne(x => x.Customer).WithOne(x => x.Loyalty).HasForeignKey<LoyaltyAccount>(x => x.CustomerId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Tier).HasMaxLength(16);
            e.Property(x => x.LifetimeSpend).HasPrecision(18, 2);
        });

        mb.Entity<PointTransaction>(e =>
        {
            e.HasOne(x => x.LoyaltyAccount).WithMany(x => x.PointTransactions).HasForeignKey(x => x.LoyaltyAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReturnRecord).WithMany().HasForeignKey(x => x.ReturnRecordId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Type).HasConversion<int>();
        });

        mb.Entity<Debt>(e =>
        {
            e.HasOne(x => x.Customer).WithMany(x => x.Debts).HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Status);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.PaidAmount).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<int>();
        });

        // ── Shift ──
        mb.Entity<Shift>(e =>
        {
            e.HasOne(x => x.User).WithMany(x => x.Shifts).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.OpeningCash).HasPrecision(18, 2);
            e.Property(x => x.ExpectedCash).HasPrecision(18, 2);
            e.Property(x => x.CountedCash).HasPrecision(18, 2);
            e.Property(x => x.Diff).HasPrecision(18, 2);
        });

        // ── Orders ──
        mb.Entity<Order>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
            e.Property(x => x.Discount).HasPrecision(18, 2);
            e.Property(x => x.SurchargeTotal).HasPrecision(18, 2);
            e.Property(x => x.Vat).HasPrecision(18, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.PaidAmount).HasPrecision(18, 2);
            e.Property(x => x.Change).HasPrecision(18, 2);
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.PaymentMethod).HasConversion<int>();
            e.HasOne(x => x.Cashier).WithMany(x => x.Orders).HasForeignKey(x => x.CashierId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Shift).WithMany(x => x.Orders).HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<OrderItem>(e =>
        {
            e.HasOne(x => x.Order).WithMany(x => x.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Qty).HasPrecision(18, 3);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.Surcharge).HasPrecision(18, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.ReturnedQty).HasPrecision(18, 3);
        });

        mb.Entity<HeldCart>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.Property(x => x.ItemsJson).HasColumnType("jsonb");
            e.Property(x => x.Subtotal).HasPrecision(18, 2);
        });

        // ── Returns ──
        mb.Entity<ReturnRecord>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.TotalReturned).HasPrecision(18, 2);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<ReturnItem>(e =>
        {
            e.HasOne(x => x.Return).WithMany(x => x.Items).HasForeignKey(x => x.ReturnId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.OrderItem).WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Qty).HasPrecision(18, 3);
            e.Property(x => x.UnitPrice).HasPrecision(18, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);
        });

        // ── Stock ledger (append-only) ──
        mb.Entity<StockLedgerEntry>(e =>
        {
            e.HasIndex(x => new { x.ProductId, x.CreatedAt });
            e.Property(x => x.Qty).HasPrecision(18, 3);
            e.Property(x => x.Balance).HasPrecision(18, 3);
            e.Property(x => x.RefType).HasConversion<int>();
            e.Property(x => x.RefId).HasMaxLength(64);
        });

        // ── Audit (append-only) ──
        mb.Entity<PosLog>(e =>
        {
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.Action);
            e.Property(x => x.Action).HasMaxLength(48);
            e.Property(x => x.EntityType).HasMaxLength(32);
            e.Property(x => x.EntityId).HasMaxLength(64);
        });

        // ── Payments ──
        mb.Entity<Payment>(e =>
        {
            e.HasOne(x => x.Order).WithMany(x => x.Payments).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Method, x.Status });
            e.Property(x => x.Method).HasConversion<int>();
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Reference).HasMaxLength(128);
        });

        // ── AI Vision: supplier invoice ──
        mb.Entity<SupplierInvoice>(e =>
        {
            e.Property(x => x.OcrResultJson).HasColumnType("jsonb");
            e.Property(x => x.OcrConfidence).HasPrecision(5, 4);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasOne(x => x.StockIn).WithOne(x => x.SupplierInvoice).HasForeignKey<StockIn>(x => x.SupplierInvoiceId).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<StockIn>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.Total).HasPrecision(18, 2);
            e.Property(x => x.SupplierName).HasMaxLength(128);
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.SupplierId);
        });

        mb.Entity<StockInItem>(e =>
        {
            e.HasOne(x => x.StockIn).WithMany(x => x.Items).HasForeignKey(x => x.StockInId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Qty).HasPrecision(18, 3);
            e.Property(x => x.UnitCost).HasPrecision(18, 2);
            e.Property(x => x.Total).HasPrecision(18, 2);
        });

        // ── Store setting (dòng duy nhất, Id=1) ──
        mb.Entity<StoreSetting>(e =>
        {
            e.Property(x => x.StoreName).HasMaxLength(128);
            e.Property(x => x.Address).HasMaxLength(256);
            e.Property(x => x.VietQrBankCode).HasMaxLength(16);
            e.Property(x => x.VietQrAccountNo).HasMaxLength(32);
            e.Property(x => x.VietQrAccountName).HasMaxLength(128);
            e.Property(x => x.VatRatePercent).HasPrecision(5, 2);
        });

        // ── Idempotency ──
        mb.Entity<IdempotencyRecord>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(128);
            e.Property(x => x.Endpoint).HasMaxLength(64);
            e.Property(x => x.RequestHash).HasMaxLength(64);
            e.HasIndex(x => new { x.Key, x.UserId }).IsUnique().HasDatabaseName("ux_idempotency_key_user");
            e.HasIndex(x => x.ExpiresAt);
        });

        // ── Invoice day counter ──
        mb.Entity<InvoiceDayCounter>(e =>
        {
            e.ToTable("invoice_day_counter");
            e.HasKey(x => x.Day);
            e.Property(x => x.Day).HasMaxLength(10);
        });

        // ── Supplier ──
        mb.Entity<Supplier>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32).IsRequired();
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(32);
            e.Property(x => x.Address).HasMaxLength(256);
            e.Property(x => x.TaxCode).HasMaxLength(64);
            e.HasOne(x => x.CustomerGroup)
                .WithMany(x => x.Suppliers)
                .HasForeignKey(x => x.CustomerGroupId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    public override void Dispose()
    {
        try
        {
            var conn = Database.GetDbConnection();
            if (conn.State == System.Data.ConnectionState.Open)
                conn.Close();
            conn.Dispose();
        }
        catch {}
        base.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            var conn = Database.GetDbConnection();
            if (conn.State == System.Data.ConnectionState.Open)
                await conn.CloseAsync();
            await conn.DisposeAsync();
        }
        catch {}
        await base.DisposeAsync();
    }
}
