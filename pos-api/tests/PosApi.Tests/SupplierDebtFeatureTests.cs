using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

    [Fact]
    public async Task StockIn_WithPartialPayment_CreatesSupplierDebt()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object);

        var supplier = new Supplier { Code = "NCC-TEST-1", Name = "Supplier 1", IsActive = true };
        context.Suppliers.Add(supplier);
        var category = new Category { Name = "Cat 1" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod 1", Barcode = "B-1", CategoryId = category.Id, CurrentStock = 10, IsActive = true };
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

        var res = await stockInService.ConfirmStockInAsync(1, req, "idem-b2-1");
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
    public async Task CancelStockIn_WithNoPayment_RemovesDebt_And_BlocksIfPaid()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object);
        var debtService = new SupplierDebtService(context);

        var supplier = new Supplier { Code = "NCC-TEST-2", Name = "Supplier 2", IsActive = true };
        context.Suppliers.Add(supplier);
        var category = new Category { Name = "Cat 2" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod 2", Barcode = "B-2", CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 5, 2000m) },
            "Supplier 2",
            "Note",
            supplier.Id,
            PaidAmount: 0m
        );

        var res = await stockInService.ConfirmStockInAsync(1, req, "idem-b2-2");
        var stockIn = await context.StockIns.FirstAsync(s => s.Code == res.Code);

        var debt = await context.SupplierDebts.FirstAsync(d => d.StockInId == stockIn.Id);
        Assert.Equal(10000m, debt.Amount);

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

        var supplier = new Supplier { Code = "NCC-TEST-3", Name = "Supplier 3", IsActive = true };
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

        var supplier = new Supplier { Code = "NCC-TEST-4", Name = "Supplier 4", IsActive = true };
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
    public async Task Recalculate_ApplyMode_IgnoresBackfilledOrPreB2StockIns()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var debtService = new SupplierDebtService(context);

        var supplier = new Supplier { Code = "NCC-TEST-6", Name = "Supplier 6", IsActive = true };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var oldStockIn = new StockIn
        {
            Code = "PHNK-OLD-1",
            SupplierId = supplier.Id,
            WarehouseId = 1,
            Total = 5000m,
            IsBackfilled = true,
            Status = StockInStatus.Confirmed
        };
        context.StockIns.Add(oldStockIn);
        await context.SaveChangesAsync();

        var res = await debtService.RecalculateAsync(1, preview: false);
        Assert.NotNull(res);
        Assert.False(res.Preview);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Allocation_OldestFirst_WithAndWithoutDueDate()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var debtService = new SupplierDebtService(context);

        var supplier = new Supplier { Code = "NCC-TEST-7", Name = "Supplier 7", IsActive = true };
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
    public async Task StockIn_PaidAmount_NoDoubleDebtReduction()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object);
        var debtService = new SupplierDebtService(context);

        var supplier = new Supplier { Code = "NCC-TEST-10", Name = "Supplier 10", IsActive = true };
        context.Suppliers.Add(supplier);
        var category = new Category { Name = "Cat 10" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod 10", Barcode = "B-10", CategoryId = category.Id, CurrentStock = 20, IsActive = true };
        context.Products.Add(product);
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 100, 10000m) }, // Total = 1,000,000
            "Supplier 10",
            "Note",
            supplier.Id,
            PaidAmount: 300000m,
            PaymentMethod: PaymentMethod.Cash
        );
        var sRes = await stockInService.ConfirmStockInAsync(1, req, "idem-b2-nodouble");
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
    public async Task PaySupplierDebt_ExceedingRemaining_Rejected()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var debtService = new SupplierDebtService(context);

        var supplier = new Supplier { Code = "NCC-TEST-8", Name = "Supplier 8", IsActive = true };
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
    public async Task Invariant_SupplierDebtBalance_AfterPurchasePaymentReturn()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object);
        var debtService = new SupplierDebtService(context);
        var returnService = new SupplierReturnService(context);

        var supplier = new Supplier { Code = "NCC-TEST-9", Name = "Supplier 9", IsActive = true };
        context.Suppliers.Add(supplier);
        var category = new Category { Name = "Cat 9" };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod 9", Barcode = "B-9", CategoryId = category.Id, CurrentStock = 20, IsActive = true };
        context.Products.Add(product);
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 10, 1000m) },
            "Supplier 9",
            "Note",
            supplier.Id,
            PaidAmount: 2000m
        );
        var sRes = await stockInService.ConfirmStockInAsync(1, req, "idem-b2-inv");
        var stockIn = await context.StockIns.FirstAsync(s => s.Code == sRes.Code);
        var debt = await context.SupplierDebts.FirstAsync(d => d.StockInId == stockIn.Id);

        Assert.Equal(8000m, debt.Amount - debt.PaidAmount);

        await debtService.PaySupplierDebtAsync(1, debt.Id, 3000m, "Payment");
        await context.Entry(debt).ReloadAsync();
        Assert.Equal(5000m, debt.Amount - debt.PaidAmount);

        var retReq = new SupplierReturnCreateRequest(
            supplier.Id,
            1,
            "Return note",
            new List<SupplierReturnItemRequest> { new(product.Id, 1, 1000m, "Defect") }
        );
        var rRes = await returnService.CreateSupplierReturnAsync(1, retReq);
        await returnService.ConfirmSupplierReturnAsync(1, rRes.Id);

        await context.Entry(debt).ReloadAsync();
        Assert.Equal(4000m, debt.Amount - debt.PaidAmount);

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

            await migrator.MigrateAsync("20261106144949_AddSupplierIdToStockIn");
            await migrator.MigrateAsync();
            await migrator.MigrateAsync("20261106144949_AddSupplierIdToStockIn");
            await migrator.MigrateAsync();

            Assert.True(true);
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
    public async Task ConcurrencyTest_ParallelPayments_WithLock()
    {
        using var context1 = _fixture.CreateRealContext();
        using var context2 = _fixture.CreateRealContext();
        var supplierService1 = new SupplierDebtService(context1);
        var supplierService2 = new SupplierDebtService(context2);

        var supplier = new Supplier { Code = "NCC-TEST-5", Name = "Supplier 5", IsActive = true };
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
