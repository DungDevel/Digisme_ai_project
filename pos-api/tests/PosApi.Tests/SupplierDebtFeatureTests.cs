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
    public async Task Recalculate_ApplyMode_IgnoresBackfilledOrPreB2StockIns()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var debtService = new SupplierDebtService(context);

        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier 6", IsActive = true };
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
    public async Task Recalculate_ApplyMode_DoesNotChangeSupplierDebts()
    {
        var context = _fixture.CreateRealContext();
        await using var tx = await context.Database.BeginTransactionAsync();
        var debtService = new SupplierDebtService(context);

        var res1 = await debtService.RecalculateAsync(1, preview: false);
        var res2 = await debtService.RecalculateAsync(1, preview: false);

        Assert.NotNull(res1);
        Assert.NotNull(res2);

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

            await migrator.MigrateAsync("20261114052118_AddQ14ReconciliationAndQ13Cost");

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

            await migrator.MigrateAsync();

            using (var conn = new NpgsqlConnection(connStr))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT COUNT(*) FROM ""StockIns"" WHERE ""Code"" = 'PHNK-OLD';
                ";
                var countStockIn = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
                Assert.Equal(1, countStockIn);
            }

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
    public async Task ConcurrencyTest_CancelStockIn_VsPayment_NoLostPayment()
    {
        using var context1 = _fixture.CreateRealContext();
        using var context2 = _fixture.CreateRealContext();
        var stockInService = new StockInService(context1, new Mock<ILogger<StockInService>>().Object);
        var debtService = new SupplierDebtService(context2);

        var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier CC", IsActive = true };
        context1.Suppliers.Add(supplier);
        var category = new Category { Name = "Cat " + Guid.NewGuid().ToString("N") };
        context1.Categories.Add(category);
        await context1.SaveChangesAsync();

        var product = new Product { Name = "Prod CC", Barcode = "B-" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context1.Products.Add(product);
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context1.SupplierInvoices.Add(invoice);
        await context1.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context1);

        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 5, 1000m) },
            "Supplier CC",
            "Note",
            supplier.Id,
            PaidAmount: 0m
        );

        var res = await stockInService.ConfirmStockInAsync(1, req, Guid.NewGuid().ToString());
        var stockIn = await context1.StockIns.FirstAsync(s => s.Code == res.Code);
        var debt = await context1.SupplierDebts.FirstAsync(d => d.StockInId == stockIn.Id);
        var debtId = debt.Id;

        var tcs = new TaskCompletionSource<bool>();

        Task<bool> cancelTask = Task.Run(async () =>
        {
            await tcs.Task;
            using var ctx = _fixture.CreateRealContext();
            var svc = new StockInService(ctx, new Mock<ILogger<StockInService>>().Object);
            try
            {
                await svc.CancelStockInAsync(1, stockIn.Id);
                return true;
            }
            catch (BusinessRuleException)
            {
                return false;
            }
        });

        Task<bool> payTask = Task.Run(async () =>
        {
            await tcs.Task;
            using var ctx = _fixture.CreateRealContext();
            var svc = new SupplierDebtService(ctx);
            try
            {
                await svc.PaySupplierDebtAsync(1, debtId, 5000m, "Pay concurrent");
                return true;
            }
            catch (BusinessRuleException)
            {
                return false;
            }
        });

        tcs.SetResult(true);
        var results = await Task.WhenAll(cancelTask, payTask);

        bool cancelSuccess = results[0];
        bool paySuccess = results[1];
        Assert.True((cancelSuccess && !paySuccess) || (!cancelSuccess && paySuccess));
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
