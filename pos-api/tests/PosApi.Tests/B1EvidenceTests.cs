using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;
using Moq;

namespace PosApi.Tests;

public class B1EvidenceTests
{
    private readonly DatabaseFixture _fixture = new();

    [Fact]
    public void LedgerRefType_Adjustment_Equals_Three()
    {
        Assert.Equal(3, (int)LedgerRefType.Adjustment);
    }

    [Fact]
    public async Task InventoryService_AdjustStock_UpdatesWarehouseStockAndLedger_AndUnblocksStockIn()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<InventoryService>>();
        var stockInLogger = new Mock<ILogger<StockInService>>();
        var inventoryService = new InventoryService(context, null, logger.Object);
        var stockInService = new StockInService(context, stockInLogger.Object);

        // Setup warehouse
        var warehouse = new Warehouse { Code = "WH-TEST-ADJ", Name = "Kho test adjust", IsDefault = true, IsActive = true };
        context.Warehouses.Add(warehouse);

        // Setup category & product
        var category = new Category { Name = "Cat " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product
        {
            Name = "Adjust Product",
            Barcode = "ADJ001-" + Guid.NewGuid().ToString("N").Substring(0, 4),
            CategoryId = category.Id,
            CurrentStock = 0,
            IsActive = true
        };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        // 1. Adjust stock (+10)
        var adjustReq = new InventoryAdjustRequest(10, "Kiểm kê đầu kỳ", warehouse.Id);
        await inventoryService.AdjustStockAsync(1, product.Id, adjustReq);

        // Assert Product CurrentStock and WarehouseStock
        var updatedProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(10, updatedProduct!.CurrentStock);

        var whStock = await context.WarehouseStocks
            .FirstOrDefaultAsync(s => s.WarehouseId == warehouse.Id && s.ProductId == product.Id);
        Assert.NotNull(whStock);
        Assert.Equal(10, whStock!.Stock);

        // Assert StockLedger has WarehouseId
        var ledger = await context.StockLedger
            .FirstOrDefaultAsync(l => l.ProductId == product.Id && l.WarehouseId == warehouse.Id);
        Assert.NotNull(ledger);
        Assert.Equal(10, ledger!.Qty);
        Assert.Equal(LedgerRefType.Adjustment, ledger.RefType);

        // 2. Confirm stock-in for product should NOT be blocked by Q14 guard
        var invoice = new SupplierInvoice
        {
            ImagePath = "inv.jpg",
            Status = SupplierInvoiceStatus.PendingReview,
            CreatedBy = 1
        };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();

        var stockInReq = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 5, 100) },
            "Supplier A",
            "Note"
        );

        var stockInRes = await stockInService.ConfirmStockInAsync(1, stockInReq, "idem-adj-1");
        Assert.NotNull(stockInRes);
        Assert.Equal(15, (await context.Products.FindAsync(product.Id))!.CurrentStock);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task AdjustStock_WarehouseLevelValidation_RejectsNegativeWarehouseStock()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var inventoryService = new InventoryService(context);

        var whA = new Warehouse { Code = "WH-A", Name = "Warehouse A", IsDefault = true, IsActive = true };
        var whB = new Warehouse { Code = "WH-B", Name = "Warehouse B", IsDefault = false, IsActive = true };
        context.Warehouses.AddRange(whA, whB);
        var category = new Category { Name = "Cat " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod Adj", Barcode = "ADJ-" + Guid.NewGuid().ToString("N").Substring(0, 4), CategoryId = category.Id, CurrentStock = 13, IsActive = true };
        context.Products.Add(product);

        context.WarehouseStocks.AddRange(
            new WarehouseStock { WarehouseId = whA.Id, ProductId = product.Id, Stock = 3 },
            new WarehouseStock { WarehouseId = whB.Id, ProductId = product.Id, Stock = 10 }
        );
        await context.SaveChangesAsync();

        // 1. Adjust -5 at Warehouse A (where stock is 3) -> should throw BusinessRuleException
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(async () =>
        {
            await inventoryService.AdjustStockAsync(1, product.Id, new InventoryAdjustRequest(-5, "Test negative at A", whA.Id));
        });
        Assert.Contains("âm", ex.Message);

        // 2. Adjust negative at a warehouse that has no row yet -> should throw BusinessRuleException and not create row
        var whC = new Warehouse { Code = "WH-C", Name = "Warehouse C", IsDefault = false, IsActive = true };
        context.Warehouses.Add(whC);
        await context.SaveChangesAsync();

        var ex2 = await Assert.ThrowsAsync<BusinessRuleException>(async () =>
        {
            await inventoryService.AdjustStockAsync(1, product.Id, new InventoryAdjustRequest(-2, "Test negative at empty C", whC.Id));
        });
        Assert.Contains("không có tồn", ex2.Message);

        var noRow = await context.WarehouseStocks.FirstOrDefaultAsync(s => s.WarehouseId == whC.Id && s.ProductId == product.Id);
        Assert.Null(noRow);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Migration_Q14_Reconciliation_RealDbTest()
    {
        var dbName = $"digism_pos_test_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var connStr = $"Host=localhost;Port=5432;Database={dbName};Username=postgres;Password=postgres";
        var masterConnStr = "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";

        using (var conn = new Npgsql.NpgsqlConnection(masterConnStr))
        {
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{dbName}\";";
            await cmd.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(connStr)
                .Options;

            using (var context = new AppDbContext(options))
            {
                var migrator = context.Database.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();

                // Migrate to target migration before Q14
                await migrator.MigrateAsync("20261113155155_AddIsBackfilledToStockIns");

                // Seed raw SQL data
                await context.Database.ExecuteSqlRawAsync(@"
                    INSERT INTO ""Warehouses"" (""Code"", ""Name"", ""IsDefault"", ""IsActive"", ""CreatedAt"")
                    VALUES ('WH-DEFAULT', 'Kho mặc định', true, true, NOW());

                    INSERT INTO ""Categories"" (""Name"", ""IsActive"")
                    VALUES ('Cat Mig', true);

                    -- (i) Product CurrentStock=10, no WarehouseStock
                    INSERT INTO ""Products"" (""Name"", ""CategoryId"", ""Barcode"", ""CostPrice"", ""RetailPrice"", ""WholesalePrice"", ""MemberPrice"", ""MinStock"", ""CurrentStock"", ""IsWeighed"", ""IsActive"", ""CreatedAt"")
                    VALUES ('Prod I', 1, 'BAR-I', 10, 20, 18, 15, 1, 10, false, true, NOW());

                    -- (ii) Matched product (CurrentStock = 15, Total WarehouseStock = 15)
                    INSERT INTO ""Products"" (""Name"", ""CategoryId"", ""Barcode"", ""CostPrice"", ""RetailPrice"", ""WholesalePrice"", ""MemberPrice"", ""MinStock"", ""CurrentStock"", ""IsWeighed"", ""IsActive"", ""CreatedAt"")
                    VALUES ('Prod II', 1, 'BAR-II', 10, 20, 18, 15, 1, 15, false, true, NOW());

                    -- (iii) Product CurrentStock < Total WarehouseStock (CurrentStock = 5, Total WarehouseStock = 20)
                    INSERT INTO ""Products"" (""Name"", ""CategoryId"", ""Barcode"", ""CostPrice"", ""RetailPrice"", ""WholesalePrice"", ""MemberPrice"", ""MinStock"", ""CurrentStock"", ""IsWeighed"", ""IsActive"", ""CreatedAt"")
                    VALUES ('Prod III', 1, 'BAR-III', 10, 20, 18, 15, 1, 5, false, true, NOW());

                    -- (iv) Unrelated valid zero-stock WarehouseStock row
                    INSERT INTO ""Products"" (""Name"", ""CategoryId"", ""Barcode"", ""CostPrice"", ""RetailPrice"", ""WholesalePrice"", ""MemberPrice"", ""MinStock"", ""CurrentStock"", ""IsWeighed"", ""IsActive"", ""CreatedAt"")
                    VALUES ('Prod IV', 1, 'BAR-IV', 10, 20, 18, 15, 1, 0, false, true, NOW());

                    -- (v) Inactive product with mismatch (IsActive = false, CurrentStock = 10, no WarehouseStock)
                    INSERT INTO ""Products"" (""Name"", ""CategoryId"", ""Barcode"", ""CostPrice"", ""RetailPrice"", ""WholesalePrice"", ""MemberPrice"", ""MinStock"", ""CurrentStock"", ""IsWeighed"", ""IsActive"", ""CreatedAt"")
                    VALUES ('Prod V', 1, 'BAR-V', 10, 20, 18, 15, 1, 10, false, false, NOW());

                    -- Setup warehouse stocks for (ii), (iii), (iv)
                    INSERT INTO ""WarehouseStocks"" (""WarehouseId"", ""ProductId"", ""Stock"", ""UpdatedAt"")
                    VALUES
                    (1, 2, 15, NOW()),
                    (1, 3, 20, NOW()),
                    (1, 4, 0, NOW());
                ");

                // Migrate up to latest (triggers Q14 migration)
                await migrator.MigrateAsync();

                // Assertions:
                var stockI = await context.WarehouseStocks.FirstOrDefaultAsync(s => s.WarehouseId == 1 && s.ProductId == 1);
                Assert.NotNull(stockI);
                Assert.Equal(10, stockI!.Stock);

                var ledgerI = await context.StockLedger.Where(l => l.ProductId == 1 && l.RefId == "Q14-RECONCILE").ToListAsync();
                Assert.Single(ledgerI);
                Assert.Equal(10, ledgerI[0].Qty);

                var ledgerII = await context.StockLedger.Where(l => l.ProductId == 2 && l.RefId == "Q14-RECONCILE").ToListAsync();
                Assert.Empty(ledgerII);

                var issueIII = await context.InventoryReconcileIssues.FirstOrDefaultAsync(i => i.ProductId == 3);
                Assert.NotNull(issueIII);
                Assert.Equal(5, issueIII!.CurrentStock);
                Assert.Equal(20, issueIII!.TotalWarehouseStock);

                var stockIV = await context.WarehouseStocks.FirstOrDefaultAsync(s => s.WarehouseId == 1 && s.ProductId == 4);
                Assert.NotNull(stockIV);
                Assert.Equal(0, stockIV!.Stock);

                var stockV = await context.WarehouseStocks.FirstOrDefaultAsync(s => s.WarehouseId == 1 && s.ProductId == 5);
                Assert.Null(stockV); // inactive product ignored

                // Idempotency check: migrate again, no duplicate ledger entries
                await migrator.MigrateAsync();
                Assert.Equal(1, await context.StockLedger.CountAsync(l => l.ProductId == 1 && l.RefId == "Q14-RECONCILE"));

                // Down migration
                await migrator.MigrateAsync("20261113155155_AddIsBackfilledToStockIns");
                Assert.Null(await context.WarehouseStocks.FirstOrDefaultAsync(s => s.WarehouseId == 1 && s.ProductId == 1));
                Assert.NotNull(await context.WarehouseStocks.FirstOrDefaultAsync(s => s.WarehouseId == 1 && s.ProductId == 4)); // row (iv) remains
                Assert.Empty(await context.StockLedger.Where(l => l.RefId == "Q14-RECONCILE").ToListAsync());

                // Up again
                await migrator.MigrateAsync();
                Assert.Equal(10, (await context.WarehouseStocks.FirstOrDefaultAsync(s => s.WarehouseId == 1 && s.ProductId == 1))!.Stock);
            }
        }
        finally
        {
            using (var conn = new Npgsql.NpgsqlConnection(masterConnStr))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE);";
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    [Fact]
    public async Task Q13_TwoStockIns_DifferentPrices()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            {"Inventory:UpdateCostOnStockIn", "true"}
        }).Build();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object, config);

        var warehouse = new Warehouse { Code = "WH-Q13-1", Name = "Warehouse Q13", IsDefault = true, IsActive = true };
        context.Warehouses.Add(warehouse);
        var category = new Category { Name = "Cat Q13 " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod Q13", Barcode = "Q13-001-" + Guid.NewGuid().ToString("N").Substring(0, 4), CategoryId = category.Id, CostPrice = 100, CurrentStock = 0, IsActive = true };
        context.Products.Add(product);

        var inv1 = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        var inv2 = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.AddRange(inv1, inv2);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        await stockInService.ConfirmStockInAsync(1, new ConfirmStockInRequest(inv1.Id, new List<StockInItemRequest> { new(product.Id, 10, 100) }, "S1", "N1"), "k1");
        Assert.Equal(100, (await context.Products.FindAsync(product.Id))!.CostPrice);

        await stockInService.ConfirmStockInAsync(1, new ConfirmStockInRequest(inv2.Id, new List<StockInItemRequest> { new(product.Id, 10, 200) }, "S2", "N2"), "k2");
        Assert.Equal(150, (await context.Products.FindAsync(product.Id))!.CostPrice);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Q13_OldStock_Zero()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            {"Inventory:UpdateCostOnStockIn", "true"}
        }).Build();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object, config);

        var warehouse = new Warehouse { Code = "WH-Q13-2", Name = "Warehouse Q13-2", IsDefault = true, IsActive = true };
        context.Warehouses.Add(warehouse);
        var category = new Category { Name = "Cat Q13-2 " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod Zero", Barcode = "Q13-002-" + Guid.NewGuid().ToString("N").Substring(0, 4), CategoryId = category.Id, CostPrice = 50, CurrentStock = 0, IsActive = true };
        context.Products.Add(product);
        var inv = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(inv);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        await stockInService.ConfirmStockInAsync(1, new ConfirmStockInRequest(inv.Id, new List<StockInItemRequest> { new(product.Id, 5, 300) }, "S", "N"), "k3");
        Assert.Equal(300, (await context.Products.FindAsync(product.Id))!.CostPrice);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Q13_TwoLines_SameProduct_InOneStockIn()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            {"Inventory:UpdateCostOnStockIn", "true"}
        }).Build();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object, config);

        var warehouse = new Warehouse { Code = "WH-Q13-3", Name = "Warehouse Q13-3", IsDefault = true, IsActive = true };
        context.Warehouses.Add(warehouse);
        var category = new Category { Name = "Cat Q13-3 " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod Multi", Barcode = "Q13-003-" + Guid.NewGuid().ToString("N").Substring(0, 4), CategoryId = category.Id, CostPrice = 100, CurrentStock = 0, IsActive = true };
        context.Products.Add(product);
        var inv = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(inv);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(
            inv.Id,
            new List<StockInItemRequest> { new(product.Id, 5, 100), new(product.Id, 5, 200) },
            "Supplier",
            "Multi lines"
        );
        var res = await stockInService.ConfirmStockInAsync(1, req, "k4");

        var stockIn = await context.StockIns.Include(s => s.Items).FirstOrDefaultAsync(s => s.Code == res.Code);
        Assert.NotNull(stockIn);
        Assert.Equal(2, stockIn!.Items.Count);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Q13_Cancel_NoSubsequent_RestoresCostBefore()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            {"Inventory:UpdateCostOnStockIn", "true"}
        }).Build();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object, config);

        var warehouse = new Warehouse { Code = "WH-Q13-4", Name = "Warehouse Q13-4", IsDefault = true, IsActive = true };
        context.Warehouses.Add(warehouse);
        var category = new Category { Name = "Cat Q13-4 " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod Cancel 1", Barcode = "Q13-004-" + Guid.NewGuid().ToString("N").Substring(0, 4), CategoryId = category.Id, CostPrice = 100, CurrentStock = 0, IsActive = true };
        context.Products.Add(product);
        var inv = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(inv);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var res = await stockInService.ConfirmStockInAsync(1, new ConfirmStockInRequest(inv.Id, new List<StockInItemRequest> { new(product.Id, 10, 200) }, "S", "N"), "k5");
        Assert.Equal(200, (await context.Products.FindAsync(product.Id))!.CostPrice);

        await stockInService.CancelStockInAsync(1, res.Id);
        Assert.Equal(100, (await context.Products.FindAsync(product.Id))!.CostPrice);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Q13_Cancel_WithSubsequent_RetainsCost_LogsWarning()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            {"Inventory:UpdateCostOnStockIn", "true"}
        }).Build();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object, config);

        var warehouse = new Warehouse { Code = "WH-Q13-5", Name = "Warehouse Q13-5", IsDefault = true, IsActive = true };
        context.Warehouses.Add(warehouse);
        var category = new Category { Name = "Cat Q13-5 " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod Cancel 2", Barcode = "Q13-005-" + Guid.NewGuid().ToString("N").Substring(0, 4), CategoryId = category.Id, CostPrice = 100, CurrentStock = 0, IsActive = true };
        context.Products.Add(product);
        var inv1 = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        var inv2 = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.AddRange(inv1, inv2);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var res1 = await stockInService.ConfirmStockInAsync(1, new ConfirmStockInRequest(inv1.Id, new List<StockInItemRequest> { new(product.Id, 10, 200) }, "S1", "N1"), "k6");
        product.CostPrice = 250;
        await context.SaveChangesAsync();

        await stockInService.CancelStockInAsync(1, res1.Id);
        Assert.Equal(250, (await context.Products.FindAsync(product.Id))!.CostPrice);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Q13_FlagOff_CostPriceUnchanged_CostBeforeEqualsCostAfter()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            {"Inventory:UpdateCostOnStockIn", "false"}
        }).Build();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object, config);

        var warehouse = new Warehouse { Code = "WH-Q13-6", Name = "Warehouse Q13-6", IsDefault = true, IsActive = true };
        context.Warehouses.Add(warehouse);
        var category = new Category { Name = "Cat Q13-6 " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod Flag Off", Barcode = "Q13-006-" + Guid.NewGuid().ToString("N").Substring(0, 4), CategoryId = category.Id, CostPrice = 100, CurrentStock = 0, IsActive = true };
        context.Products.Add(product);
        var inv = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(inv);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var res = await stockInService.ConfirmStockInAsync(1, new ConfirmStockInRequest(inv.Id, new List<StockInItemRequest> { new(product.Id, 10, 300) }, "S", "N"), "k7");

        Assert.Equal(100, (await context.Products.FindAsync(product.Id))!.CostPrice);

        var stockInItem = await context.StockInItems.Include(i => i.StockIn).FirstOrDefaultAsync(i => i.StockIn.Code == res.Code);
        Assert.NotNull(stockInItem);
        Assert.Equal(100, stockInItem!.CostBefore);
        Assert.Equal(100, stockInItem!.CostAfter);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Q13_FlagOn_UpdatesCostPrice()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            {"Inventory:UpdateCostOnStockIn", "true"}
        }).Build();
        var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object, config);

        var warehouse = new Warehouse { Code = "WH-Q13-7", Name = "Warehouse Q13-7", IsDefault = true, IsActive = true };
        context.Warehouses.Add(warehouse);
        var category = new Category { Name = "Cat Q13-7 " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod Flag On", Barcode = "Q13-007-" + Guid.NewGuid().ToString("N").Substring(0, 4), CategoryId = category.Id, CostPrice = 100, CurrentStock = 0, IsActive = true };
        context.Products.Add(product);
        var inv = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(inv);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var res = await stockInService.ConfirmStockInAsync(1, new ConfirmStockInRequest(inv.Id, new List<StockInItemRequest> { new(product.Id, 10, 300) }, "S", "N"), "k8");

        Assert.Equal(300, (await context.Products.FindAsync(product.Id))!.CostPrice);

        await tx.RollbackAsync();
    }
}
