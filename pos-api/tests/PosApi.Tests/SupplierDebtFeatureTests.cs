using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;

namespace PosApi.Tests;

public class SupplierDebtFeatureTests
{
    private readonly DatabaseFixture _fixture = new();

    private static async Task EnsureWarehouseAsync(AppDbContext context)
    {
        if (!await context.Warehouses.AnyAsync())
        {
            context.Warehouses.Add(new Warehouse { Code = "WH-DEFAULT", Name = "Kho mặc định", IsDefault = true, IsActive = true });
            await context.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task StockIn_WithPartialPayment_CreatesSupplierDebt()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object);

        await EnsureWarehouseAsync(context);
        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier 1", IsActive = true };
        context.Suppliers.Add(supplier);
        var category = new Category { Name = "Cat " + Guid.NewGuid().ToString("N") };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod 1", Barcode = "B-" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var dueDate = DateTime.UtcNow.AddDays(15);
        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 10, 1000m) },
            "Supplier 1",
            "Debt note",
            supplier.Id,
            PaidAmount: 3000m,
            DueDate: dueDate
        );

        var res = await stockInService.ConfirmStockInAsync(1, req, Guid.NewGuid().ToString());
        Assert.NotNull(res);

        var debt = await context.SupplierDebts.FirstOrDefaultAsync(d => d.SupplierId == supplier.Id);
        Assert.NotNull(debt);
        Assert.Equal(7000m, debt!.Amount);
        Assert.Equal(0m, debt.PaidAmount);
        Assert.Equal(DebtStatus.Open, debt.Status);
        Assert.NotNull(debt.StockInId);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CancelStockIn_WithPaidAmountZero_RemovesDebt()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object);

        await EnsureWarehouseAsync(context);
        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier 2A", IsActive = true };
        context.Suppliers.Add(supplier);
        var category = new Category { Name = "Cat " + Guid.NewGuid().ToString("N") };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod 2A", Barcode = "B-" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 5, 2000m) },
            "Supplier 2A",
            "Note",
            supplier.Id,
            PaidAmount: 0m
        );

        var res = await stockInService.ConfirmStockInAsync(1, req, Guid.NewGuid().ToString());
        var stockIn = await context.StockIns.FirstAsync(s => s.Code == res.Code);

        var debt = await context.SupplierDebts.FirstAsync(d => d.StockInId == stockIn.Id);
        Assert.Equal(10000m, debt.Amount);

        await stockInService.CancelStockInAsync(1, stockIn.Id);

        var debtAfter = await context.SupplierDebts.FirstOrDefaultAsync(d => d.StockInId == stockIn.Id);
        Assert.Null(debtAfter);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CancelStockIn_WithPaidAmountGreaterThanZero_Blocked()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object);

        await EnsureWarehouseAsync(context);
        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier 2B", IsActive = true };
        context.Suppliers.Add(supplier);
        var category = new Category { Name = "Cat " + Guid.NewGuid().ToString("N") };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod 2B", Barcode = "B-" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 5, 2000m) },
            "Supplier 2B",
            "Note",
            supplier.Id,
            PaidAmount: 2000m
        );

        var res = await stockInService.ConfirmStockInAsync(1, req, Guid.NewGuid().ToString());
        var stockIn = await context.StockIns.FirstAsync(s => s.Code == res.Code);

        await Assert.ThrowsAsync<BusinessRuleException>(async () =>
        {
            await stockInService.CancelStockInAsync(1, stockIn.Id);
        });

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CancelStockIn_WithAllocatedPayment_Blocked()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object);
        var debtService = new SupplierDebtService(context);

        await EnsureWarehouseAsync(context);
        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier 2C", IsActive = true };
        context.Suppliers.Add(supplier);
        var category = new Category { Name = "Cat " + Guid.NewGuid().ToString("N") };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod 2C", Barcode = "B-" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 5, 2000m) },
            "Supplier 2C",
            "Note",
            supplier.Id,
            PaidAmount: 0m
        );

        var res = await stockInService.ConfirmStockInAsync(1, req, Guid.NewGuid().ToString());
        var stockIn = await context.StockIns.FirstAsync(s => s.Code == res.Code);

        var debt = await context.SupplierDebts.FirstAsync(d => d.StockInId == stockIn.Id);
        await debtService.PaySupplierDebtAsync(1, debt.Id, 4000m, "Partial payment");

        await Assert.ThrowsAsync<BusinessRuleException>(async () =>
        {
            await stockInService.CancelStockInAsync(1, stockIn.Id);
        });

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task PaymentVoucher_AllocatesToOldestDebts_Success()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var voucherService = new CashVoucherService(context);

        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier 3", IsActive = true };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var debt1 = new SupplierDebt { SupplierId = supplier.Id, Amount = 5000m, PaidAmount = 0m, DueDate = DateTime.UtcNow.AddDays(5), CreatedAt = DateTime.UtcNow.AddDays(-2) };
        var debt2 = new SupplierDebt { SupplierId = supplier.Id, Amount = 5000m, PaidAmount = 0m, DueDate = DateTime.UtcNow.AddDays(1), CreatedAt = DateTime.UtcNow.AddDays(-1) };
        context.SupplierDebts.AddRange(debt1, debt2);
        await context.SaveChangesAsync();

        var voucherReq = new CashVoucherCreateRequest(
            CashVoucherType.Payment,
            PartnerType.Supplier,
            supplier.Id,
            6000m,
            PaymentMethod.BankTransfer,
            "Manual",
            null,
            null,
            "Pay supplier"
        );

        var vRes = await voucherService.CreateVoucherAsync(1, voucherReq);
        await voucherService.ConfirmVoucherAsync(1, vRes.Id);

        await context.Entry(debt1).ReloadAsync();
        await context.Entry(debt2).ReloadAsync();

        Assert.Equal(DebtStatus.Settled, debt2.Status);
        Assert.Equal(5000m, debt2.PaidAmount);
        Assert.Equal(DebtStatus.PartiallyPaid, debt1.Status);
        Assert.Equal(1000m, debt1.PaidAmount);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task DeleteSupplier_WithRemainingDebt_ThrowsException()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var supplierService = new SupplierService(context);

        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier 4", IsActive = true };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        context.SupplierDebts.Add(new SupplierDebt { SupplierId = supplier.Id, Amount = 1000m, PaidAmount = 0m });
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(async () =>
        {
            await supplierService.DeleteSupplierAsync(1, supplier.Id);
        });

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task DeleteStockIn_WithLinkedSupplierDebt_ThrowsException()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object);

        await EnsureWarehouseAsync(context);
        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier Del", IsActive = true };
        context.Suppliers.Add(supplier);
        var category = new Category { Name = "Cat " + Guid.NewGuid().ToString("N") };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod Del", Barcode = "B-" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 5, 1000m) },
            "Supplier Del",
            "Note",
            supplier.Id,
            PaidAmount: 0m
        );

        var res = await stockInService.ConfirmStockInAsync(1, req, Guid.NewGuid().ToString());
        var stockIn = await context.StockIns.FirstAsync(s => s.Code == res.Code);

        await Assert.ThrowsAsync<BusinessRuleException>(async () =>
        {
            await stockInService.DeleteStockInAsync(1, stockIn.Id);
        });

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Recalculate_ReportsDiscrepancyForOldStockInWithoutDebt()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var debtService = new SupplierDebtService(context);

        await EnsureWarehouseAsync(context);
        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier Disc", IsActive = true };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var oldStockIn = new StockIn
        {
            Code = "PHNK-OLD-DISC",
            SupplierId = supplier.Id,
            WarehouseId = 1,
            Total = 5000m,
            PaidAmount = 0m,
            Status = StockInStatus.Confirmed
        };
        context.StockIns.Add(oldStockIn);
        await context.SaveChangesAsync();

        var res = await debtService.RecalculateAsync(1, preview: true);
        Assert.NotNull(res);
        var disc = res.Discrepancies.FirstOrDefault(d => d.SupplierId == supplier.Id);
        Assert.NotNull(disc);
        Assert.Equal(5000m, disc!.CalculatedBalance);
        Assert.Equal(0m, disc.RecordedBalance);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Recalculate_ApplyMode_DoesNotChangeSupplierDebts()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var debtService = new SupplierDebtService(context);

        await EnsureWarehouseAsync(context);
        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier 6", IsActive = true };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var debt = new SupplierDebt { SupplierId = supplier.Id, Amount = 5000m, PaidAmount = 1000m, Status = DebtStatus.PartiallyPaid };
        context.SupplierDebts.Add(debt);
        await context.SaveChangesAsync();

        var beforeDebts = await context.SupplierDebts
            .AsNoTracking()
            .Select(d => new { d.Id, d.Amount, d.PaidAmount })
            .OrderBy(d => d.Id)
            .ToListAsync();

        await debtService.RecalculateAsync(1, preview: false);
        await debtService.RecalculateAsync(1, preview: false);

        var afterDebts = await context.SupplierDebts
            .AsNoTracking()
            .Select(d => new { d.Id, d.Amount, d.PaidAmount })
            .OrderBy(d => d.Id)
            .ToListAsync();

        Assert.Equal(beforeDebts.Count, afterDebts.Count);
        for (int i = 0; i < beforeDebts.Count; i++)
        {
            Assert.Equal(beforeDebts[i].Id, afterDebts[i].Id);
            Assert.Equal(beforeDebts[i].Amount, afterDebts[i].Amount);
            Assert.Equal(beforeDebts[i].PaidAmount, afterDebts[i].PaidAmount);
        }

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task SupplierDebt_DuplicateStockInId_ThrowsDbUpdateException()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();

        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier Dup", IsActive = true };
        context.Suppliers.Add(supplier);
        var warehouse = new Warehouse { Code = "WH-" + Guid.NewGuid().ToString("N").Substring(0, 6), Name = "Wh Dup", IsActive = true };
        context.Warehouses.Add(warehouse);
        await context.SaveChangesAsync();

        var stockIn = new StockIn { Code = "PHNK-DUP-1", WarehouseId = warehouse.Id, SupplierId = supplier.Id, Total = 1000m, Status = StockInStatus.Confirmed };
        context.StockIns.Add(stockIn);
        await context.SaveChangesAsync();

        context.SupplierDebts.Add(new SupplierDebt { SupplierId = supplier.Id, Amount = 1000m, PaidAmount = 0m, StockInId = stockIn.Id });
        await context.SaveChangesAsync();

        context.SupplierDebts.Add(new SupplierDebt { SupplierId = supplier.Id, Amount = 2000m, PaidAmount = 0m, StockInId = stockIn.Id });
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await context.SaveChangesAsync();
        });

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Allocation_OldestFirst_WithAndWithoutDueDate()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var debtService = new SupplierDebtService(context);

        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier 7", IsActive = true };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var debt1 = new SupplierDebt { SupplierId = supplier.Id, Amount = 4000m, PaidAmount = 0m, DueDate = null, CreatedAt = DateTime.UtcNow.AddDays(-5) };
        var debt2 = new SupplierDebt { SupplierId = supplier.Id, Amount = 4000m, PaidAmount = 0m, DueDate = DateTime.UtcNow.AddDays(2), CreatedAt = DateTime.UtcNow.AddDays(-1) };
        context.SupplierDebts.AddRange(debt1, debt2);
        await context.SaveChangesAsync();

        await debtService.AllocatePaymentAsync(supplier.Id, 5000m, null, "Test allocation", 1);

        await context.Entry(debt1).ReloadAsync();
        await context.Entry(debt2).ReloadAsync();

        Assert.Equal(DebtStatus.Settled, debt2.Status);
        Assert.Equal(4000m, debt2.PaidAmount);
        Assert.Equal(DebtStatus.PartiallyPaid, debt1.Status);
        Assert.Equal(1000m, debt1.PaidAmount);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task PaySupplierDebt_ExceedingRemaining_Rejected()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var debtService = new SupplierDebtService(context);

        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier 8", IsActive = true };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var debt = new SupplierDebt { SupplierId = supplier.Id, Amount = 2000m, PaidAmount = 0m };
        context.SupplierDebts.Add(debt);
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(async () =>
        {
            await debtService.PaySupplierDebtAsync(1, debt.Id, 3000m, "Too much");
        });

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task StockIn_PaidAmount_NoDoubleDebtReduction()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object);
        var debtService = new SupplierDebtService(context);

        await EnsureWarehouseAsync(context);
        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier 10", IsActive = true };
        context.Suppliers.Add(supplier);
        var category = new Category { Name = "Cat " + Guid.NewGuid().ToString("N") };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod 10", Barcode = "B-" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = category.Id, CurrentStock = 20, IsActive = true };
        context.Products.Add(product);
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 100, 10000m) },
            "Supplier 10",
            "Note",
            supplier.Id,
            PaidAmount: 300000m,
            PaymentMethod: PaymentMethod.Cash
        );
        var sRes = await stockInService.ConfirmStockInAsync(1, req, Guid.NewGuid().ToString());
        var stockIn = await context.StockIns.FirstAsync(s => s.Code == sRes.Code);

        var debt = await context.SupplierDebts.FirstAsync(d => d.StockInId == stockIn.Id);
        Assert.Equal(700000m, debt.Amount);
        Assert.Equal(0m, debt.PaidAmount);

        await debtService.PaySupplierDebtAsync(1, debt.Id, 200000m, "Additional payment");
        await context.Entry(debt).ReloadAsync();

        Assert.Equal(500000m, debt.Amount - debt.PaidAmount);

        var autoVoucher = await context.CashVouchers.FirstOrDefaultAsync(v => v.ReferenceType == "StockIn" && v.ReferenceId == stockIn.Code);
        Assert.NotNull(autoVoucher);
        Assert.Equal(300000m, autoVoucher!.Amount);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task MigrationTest_IMigrator_VerifyOldDataAndRollback()
    {
        var masterConnStr = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
        var dbName = $"b2_mig_test_{Guid.NewGuid():N}";
        var connStr = $"Host=localhost;Port=5432;Database={dbName};Username=pos_user;Password=pos_pass";

        using (var adminConn = new NpgsqlConnection(masterConnStr))
        {
            await adminConn.OpenAsync();
            using var cmd = adminConn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\" OWNER pos_user;";
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connStr).Options;
            using var context = new AppDbContext(options);
            var migrator = context.Database.GetService<IMigrator>();

            // 1. Migrate up to base B1 migration
            await migrator.MigrateAsync("20261114052118_AddQ14ReconciliationAndQ13Cost");

            // Seed old data
            using (var conn = new NpgsqlConnection(connStr))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO ""Warehouses"" (""Code"", ""Name"", ""IsDefault"", ""IsActive"", ""CreatedAt"") VALUES ('WH-OLD', 'Old Wh', true, true, NOW());
                    INSERT INTO ""Suppliers"" (""Code"", ""Name"", ""IsActive"", ""CreatedAt"") VALUES ('NCC-OLD', 'Old Sup', true, NOW());
                    INSERT INTO ""StockIns"" (""Code"", ""WarehouseId"", ""SupplierId"", ""Total"", ""Status"", ""CreatedBy"", ""CreatedAt"")
                    VALUES ('PHNK-OLD', 1, 1, 1000, 1, 1, NOW());
                    INSERT INTO ""SupplierDebts"" (""SupplierId"", ""Amount"", ""PaidAmount"", ""Status"", ""CreatedAt"")
                    VALUES (1, 1000, 0, 0, NOW());
                ";
                await cmd.ExecuteNonQueryAsync();
            }

            // 2. Migrate up (AddSupplierDebtB2)
            await migrator.MigrateAsync();

            // Verify columns exist via separate information_schema queries
            using (var conn = new NpgsqlConnection(connStr))
            {
                await conn.OpenAsync();

                async Task<bool> CheckCol(string table, string col)
                {
                    using var c = conn.CreateCommand();
                    c.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = @t AND column_name = @c";
                    c.Parameters.Add(new NpgsqlParameter("@t", table));
                    c.Parameters.Add(new NpgsqlParameter("@c", col));
                    var cnt = (long)(await c.ExecuteScalarAsync() ?? 0L);
                    return cnt > 0;
                }

                Assert.True(await CheckCol("SupplierDebts", "DueDate"));
                Assert.True(await CheckCol("SupplierDebts", "StockInId"));
                Assert.True(await CheckCol("StockIns", "DueDate"));
                Assert.True(await CheckCol("StockIns", "PaidAmount"));
                Assert.True(await CheckCol("StockIns", "PaymentMethod"));

                // Verify index IX_SupplierDebts_StockInId_Unique exists in pg_indexes
                using var idxCmd = conn.CreateCommand();
                idxCmd.CommandText = "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'IX_SupplierDebts_StockInId_Unique'";
                var idxCount = (long)(await idxCmd.ExecuteScalarAsync() ?? 0L);
                Assert.True(idxCount > 0);

                // Verify old data retained
                using var dataCmd = conn.CreateCommand();
                dataCmd.CommandText = "SELECT COUNT(*) FROM \"StockIns\" WHERE \"Code\" = 'PHNK-OLD'";
                var countStockIn = (long)(await dataCmd.ExecuteScalarAsync() ?? 0L);
                Assert.Equal(1, countStockIn);
            }

            // 3. Migrate down to base B1 migration
            await migrator.MigrateAsync("20261114052118_AddQ14ReconciliationAndQ13Cost");

            // Verify 5 columns disappear, index disappears, old data remains
            using (var conn = new NpgsqlConnection(connStr))
            {
                await conn.OpenAsync();

                async Task<bool> CheckCol(string table, string col)
                {
                    using var c = conn.CreateCommand();
                    c.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = @t AND column_name = @c";
                    c.Parameters.Add(new NpgsqlParameter("@t", table));
                    c.Parameters.Add(new NpgsqlParameter("@c", col));
                    var cnt = (long)(await c.ExecuteScalarAsync() ?? 0L);
                    return cnt > 0;
                }

                Assert.False(await CheckCol("SupplierDebts", "DueDate"));
                Assert.False(await CheckCol("SupplierDebts", "StockInId"));
                Assert.False(await CheckCol("StockIns", "DueDate"));
                Assert.False(await CheckCol("StockIns", "PaidAmount"));
                Assert.False(await CheckCol("StockIns", "PaymentMethod"));

                using var idxCmd = conn.CreateCommand();
                idxCmd.CommandText = "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'IX_SupplierDebts_StockInId_Unique'";
                var idxCount = (long)(await idxCmd.ExecuteScalarAsync() ?? 0L);
                Assert.Equal(0, idxCount);

                using var dataCmd = conn.CreateCommand();
                dataCmd.CommandText = "SELECT COUNT(*) FROM \"StockIns\" WHERE \"Code\" = 'PHNK-OLD'";
                var countStockIn = (long)(await dataCmd.ExecuteScalarAsync() ?? 0L);
                Assert.Equal(1, countStockIn);
            }

            // 4. Migrate up again and assert again
            await migrator.MigrateAsync();
            using (var conn = new NpgsqlConnection(connStr))
            {
                await conn.OpenAsync();

                async Task<bool> CheckCol(string table, string col)
                {
                    using var c = conn.CreateCommand();
                    c.CommandText = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = @t AND column_name = @c";
                    c.Parameters.Add(new NpgsqlParameter("@t", table));
                    c.Parameters.Add(new NpgsqlParameter("@c", col));
                    var cnt = (long)(await c.ExecuteScalarAsync() ?? 0L);
                    return cnt > 0;
                }

                Assert.True(await CheckCol("SupplierDebts", "DueDate"));
                Assert.True(await CheckCol("SupplierDebts", "StockInId"));
                Assert.True(await CheckCol("StockIns", "DueDate"));
                Assert.True(await CheckCol("StockIns", "PaidAmount"));
                Assert.True(await CheckCol("StockIns", "PaymentMethod"));

                using var idxCmd = conn.CreateCommand();
                idxCmd.CommandText = "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'IX_SupplierDebts_StockInId_Unique'";
                var idxCount = (long)(await idxCmd.ExecuteScalarAsync() ?? 0L);
                Assert.True(idxCount > 0);
            }
        }
        finally
        {
            using var adminConn = new NpgsqlConnection(masterConnStr);
            await adminConn.OpenAsync();
            using var cmd = adminConn.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);";
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task ConcurrencyTest_CancelStockIn_VsPayment_NoLostPayment()
    {
        using var contextSetup = _fixture.CreateRealContext();
        await EnsureWarehouseAsync(contextSetup);
        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier CC", IsActive = true };
        contextSetup.Suppliers.Add(supplier);
        var category = new Category { Name = "Cat " + Guid.NewGuid().ToString("N") };
        contextSetup.Categories.Add(category);
        await contextSetup.SaveChangesAsync();

        var product = new Product { Name = "Prod CC", Barcode = "B-" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        contextSetup.Products.Add(product);
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        contextSetup.SupplierInvoices.Add(invoice);
        await contextSetup.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(contextSetup);

        var stockInServiceSetup = new StockInService(contextSetup, new Mock<ILogger<StockInService>>().Object);
        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 5, 1000m) },
            "Supplier CC",
            "Note",
            supplier.Id,
            PaidAmount: 0m
        );
        var res = await stockInServiceSetup.ConfirmStockInAsync(1, req, Guid.NewGuid().ToString());
        var stockInId = (await contextSetup.StockIns.FirstAsync(s => s.Code == res.Code)).Id;
        var debtId = (await contextSetup.SupplierDebts.FirstAsync(d => d.StockInId == stockInId)).Id;

        // Scenario 1: Payment transaction holds row lock (uncommitted), CancelStockIn is invoked concurrently and must block
        {
            using var payContext = _fixture.CreateRealContext();
            await using var payTx = await payContext.Database.BeginTransactionAsync();
            var paySvc = new SupplierDebtService(payContext);

            // Apply payment (acquires FOR UPDATE lock on SupplierDebts row) without committing
            await paySvc.PaySupplierDebtAsync(1, debtId, 2000m, "Pay holder");

            using var cancelContext = _fixture.CreateRealContext();
            var cancelSvc = new StockInService(cancelContext, new Mock<ILogger<StockInService>>().Object);

            var cancelTask = cancelSvc.CancelStockInAsync(1, stockInId);

            // Assert cancelTask is blocked by lock (does not complete within 500ms)
            var completedInTime = await Task.WhenAny(cancelTask, Task.Delay(500)) == cancelTask;
            Assert.False(completedInTime, "CancelStockIn should be blocked by row lock while payment transaction is active");

            // Commit payment transaction, releasing lock
            await payTx.CommitAsync();

            // CancelStockIn unblocks, sees PaidAmount > 0, and throws BusinessRuleException
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(async () => await cancelTask);
            Assert.Contains("phát sinh thanh toán", ex.Message);

            // Verify debt has 2000 paid amount, stockIn status is still Confirmed
            using var verifyContext = _fixture.CreateRealContext();
            var debtAfter = await verifyContext.SupplierDebts.FirstAsync(d => d.Id == debtId);
            Assert.Equal(2000m, debtAfter.PaidAmount);
            var stockInAfter = await verifyContext.StockIns.FirstAsync(s => s.Id == stockInId);
            Assert.Equal(StockInStatus.Confirmed, stockInAfter.Status);
        }

        // Setup another stock-in and debt for Scenario 2
        var invoice2 = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        contextSetup.SupplierInvoices.Add(invoice2);
        await contextSetup.SaveChangesAsync();
        var req2 = new ConfirmStockInRequest(
            invoice2.Id,
            new List<StockInItemRequest> { new(product.Id, 5, 1000m) },
            "Supplier CC 2",
            "Note",
            supplier.Id,
            PaidAmount: 0m
        );
        var res2 = await stockInServiceSetup.ConfirmStockInAsync(1, req2, Guid.NewGuid().ToString());
        var stockInId2 = (await contextSetup.StockIns.FirstAsync(s => s.Code == res2.Code)).Id;
        var debtId2 = (await contextSetup.SupplierDebts.FirstAsync(d => d.StockInId == stockInId2)).Id;

        // Scenario 2: Cancel transaction holds row lock (uncommitted), PaySupplierDebt is invoked concurrently and must block
        {
            using var cancelContext2 = _fixture.CreateRealContext();
            await using var cancelTx2 = await cancelContext2.Database.BeginTransactionAsync();
            var conn = cancelContext2.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            // Acquire lock on SupplierDebts row via raw command under transaction
            using (var lockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                lockCmd!.CommandText = "SELECT \"Id\" FROM \"SupplierDebts\" WHERE \"StockInId\" = @stockInId FOR UPDATE";
                if (cancelContext2.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx)
                    lockCmd.Transaction = npgTx;
                lockCmd.Parameters.Add(new NpgsqlParameter("@stockInId", stockInId2));
                await lockCmd.ExecuteScalarAsync();
            }

            using var payContext2 = _fixture.CreateRealContext();
            var paySvc2 = new SupplierDebtService(payContext2);
            var payTask = paySvc2.PaySupplierDebtAsync(1, debtId2, 1000m, "Pay concurrent blocked");

            // Assert payTask is blocked by lock (does not complete within 500ms)
            var completedInTime = await Task.WhenAny(payTask, Task.Delay(500)) == payTask;
            Assert.False(completedInTime, "PaySupplierDebt should be blocked by row lock while cancel transaction holds lock");

            // Perform cancellation or delete debt in cancelTx2 and commit
            using (var cancelCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                cancelCmd!.CommandText = "DELETE FROM \"SupplierDebts\" WHERE \"StockInId\" = @stockInId";
                if (cancelContext2.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx2)
                    cancelCmd.Transaction = npgTx2;
                cancelCmd.Parameters.Add(new NpgsqlParameter("@stockInId", stockInId2));
                await cancelCmd.ExecuteNonQueryAsync();
            }

            // Commit cancel transaction, releasing lock and removing debt
            await cancelTx2.CommitAsync();

            // payTask unblocks, tries to lock/find debt, and throws KeyNotFoundException
            await Assert.ThrowsAsync<KeyNotFoundException>(async () => await payTask);
        }
    }

    [Fact]
    public async Task ConcurrencyTest_ParallelPayments_WithLock()
    {
        using var context1 = _fixture.CreateRealContext();
        using var context2 = _fixture.CreateRealContext();
        var supplierService1 = new SupplierDebtService(context1);
        var supplierService2 = new SupplierDebtService(context2);

        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier 5", IsActive = true };
        context1.Suppliers.Add(supplier);
        await context1.SaveChangesAsync();

        var debt = new SupplierDebt { SupplierId = supplier.Id, Amount = 10000m, PaidAmount = 0m };
        context1.SupplierDebts.Add(debt);
        await context1.SaveChangesAsync();
        var debtId = debt.Id;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var t1 = supplierService1.PaySupplierDebtAsync(1, debtId, 3000m, "Pay 1");
        var t2 = supplierService2.PaySupplierDebtAsync(1, debtId, 4000m, "Pay 2");

        await Task.WhenAll(t1, t2);
        sw.Stop();

        await context1.Entry(debt).ReloadAsync();
        Assert.Equal(7000m, debt.PaidAmount);
        Assert.True(sw.ElapsedMilliseconds > 0);
    }
}
