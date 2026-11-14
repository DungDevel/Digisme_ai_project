using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;

namespace PosApi.Tests;

public class StockInServiceTests
{
    private readonly DatabaseFixture _fixture = new();

    [Fact]
    public async Task ConfirmStockIn_Success_UpdatesStockAndCreatesRecords()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        // Setup: Category
        var category = new Category { Name = "Test Cat " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        // Setup: Product
        var product = new Product
        {
            Name = "Test Product",
            Barcode = "TP001",
            CategoryId = category.Id,
            CurrentStock = 10,
            IsActive = true
        };
        context.Products.Add(product);

        // Setup: SupplierInvoice
        var invoice = new SupplierInvoice
        {
            ImagePath = "test.jpg",
            Status = SupplierInvoiceStatus.PendingReview,
            CreatedBy = 1
        };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 5, 100) },
            "Test Supplier",
            "Test Note"
        );

        var result = await service.ConfirmStockInAsync(1, req, "idem-key-1");

        Assert.NotNull(result);
        Assert.StartsWith("PHNK-", result.Code);

        // Verify Stock
        var updatedProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(15, updatedProduct!.CurrentStock);

        // Verify StockIn
        var stockIn = await context.StockIns.FirstOrDefaultAsync(s => s.Code == result.Code);
        Assert.NotNull(stockIn);
        Assert.Equal(500, stockIn!.Total);
        Assert.Equal(StockInStatus.Confirmed, stockIn.Status);

        // Verify Ledger
        var ledger = await context.StockLedger.FirstOrDefaultAsync(l => l.ProductId == product.Id && l.RefId == result.Code);
        Assert.NotNull(ledger);
        Assert.Equal(5, ledger!.Qty);
        Assert.Equal(LedgerRefType.Purchase, ledger.RefType);

        // Verify Invoice Status
        var updatedInvoice = await context.SupplierInvoices.FindAsync(invoice.Id);
        Assert.Equal(SupplierInvoiceStatus.Confirmed, updatedInvoice!.Status);
        Assert.Equal(stockIn.Id, updatedInvoice.StockInId);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ConfirmStockIn_Idempotency_ReturnsSameResult()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var category = new Category { Name = "Test Cat 2 " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "P", Barcode = "B", CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(invoice.Id, new List<StockInItemRequest> { new(product.Id, 1, 10) }, "S", "N");
        var key = "idem-key-2";

        var stockInsBefore = await context.StockIns.CountAsync();
        var ledgerBefore = await context.StockLedger.CountAsync();

        var res1 = await service.ConfirmStockInAsync(1, req, key);
        var res2 = await service.ConfirmStockInAsync(1, req, key);

        Assert.Equal(res1.Code, res2.Code);
        Assert.Equal(res1.Id, res2.Id);

        // Verify only one StockIn was created and ledger entries didn't duplicate
        var stockInsAfter = await context.StockIns.CountAsync();
        var ledgerAfter = await context.StockLedger.CountAsync();

        Assert.Equal(stockInsBefore + 1, stockInsAfter);
        Assert.Equal(ledgerBefore + 1, ledgerAfter);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task UploadAndGetInvoice_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var fileMock = new Mock<IFormFile>();
        var content = "fake image bytes";
        var fileName = "invoice.jpg";
        var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.Length).Returns(ms.Length);
        fileMock.Setup(f => f.OpenReadStream()).Returns(ms);
        fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, CancellationToken>((stream, token) => ms.CopyTo(stream))
            .Returns(Task.CompletedTask);

        var uploaded = await service.UploadInvoiceAsync(1, fileMock.Object);
        Assert.NotNull(uploaded);
        Assert.Equal(SupplierInvoiceStatus.PendingReview, uploaded.Status);

        var fetched = await service.GetInvoiceByIdAsync(uploaded.Id);
        Assert.NotNull(fetched);
        Assert.Equal(uploaded.Id, fetched.Id);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task RejectInvoice_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        await service.RejectInvoiceAsync(invoice.Id);

        var updated = await context.SupplierInvoices.FindAsync(invoice.Id);
        Assert.Equal(SupplierInvoiceStatus.Rejected, updated!.Status);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ConfirmStockIn_WithValidSupplierId_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var category = new Category { Name = "Cat " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod", Barcode = "P1", CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);

        var supplier = new Supplier { Code = "SUP-001", Name = "Valid Supplier", IsActive = true };
        context.Suppliers.Add(supplier);

        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 5, 100) },
            null,
            "Note",
            supplier.Id
        );

        var result = await service.ConfirmStockInAsync(1, req, "idem-sup-1");
        Assert.NotNull(result);

        var stockIn = await context.StockIns.FirstOrDefaultAsync(s => s.Code == result.Code);
        Assert.NotNull(stockIn);
        Assert.Equal(supplier.Id, stockIn!.SupplierId);
        Assert.Equal("Valid Supplier", stockIn.SupplierName);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ConfirmStockIn_WithInvalidSupplierId_ThrowsKeyNotFoundException()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var category = new Category { Name = "Cat " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Prod", Barcode = "P2", CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(
            invoice.Id,
            new List<StockInItemRequest> { new(product.Id, 5, 100) },
            null,
            "Note",
            99999 // non-existent supplier
        );

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ConfirmStockInAsync(1, req, "idem-sup-2"));

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ManualStockIn_DraftUpdateConfirmCancel_WorkflowSuccess()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var category = new Category { Name = "Cat Manual " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Manual Prod", Barcode = "MP1", CategoryId = category.Id, CurrentStock = 20, IsActive = true };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        // 1. Create Draft
        var createReq = new StockInCreateRequest(
            WarehouseId: null,
            SupplierId: null,
            SupplierName: "Manual Supplier",
            Note: "Initial draft",
            Items: new List<StockInItemRequest> { new(product.Id, 10, 15000m) }
        );

        var draft = await service.CreateStockInDraftAsync(1, createReq);
        Assert.NotNull(draft);
        Assert.Equal(StockInStatus.Draft, draft.Status);
        Assert.Equal(150000m, draft.Total);

        // 2. Update Draft
        var updateReq = new StockInUpdateRequest(
            WarehouseId: null,
            SupplierId: null,
            SupplierName: "Updated Supplier",
            Note: "Updated draft note",
            Items: new List<StockInItemRequest> { new(product.Id, 12, 16000m) }
        );

        var updatedDraft = await service.UpdateStockInDraftAsync(1, draft.Id, updateReq);
        Assert.NotNull(updatedDraft);
        Assert.Equal("Updated Supplier", updatedDraft.SupplierName);
        Assert.Equal(192000m, updatedDraft.Total);

        // 3. Confirm Draft
        var confirmed = await service.ConfirmStockInDraftAsync(1, draft.Id, "idem-draft-confirm");
        Assert.NotNull(confirmed);
        Assert.Equal(StockInStatus.Confirmed, confirmed.Status);

        var prodAfterConfirm = await context.Products.FindAsync(product.Id);
        Assert.Equal(32, prodAfterConfirm!.CurrentStock); // 20 + 12

        // 4. Cancel Confirmed Stock In
        var cancelled = await service.CancelStockInAsync(1, draft.Id);
        Assert.NotNull(cancelled);
        Assert.Equal(StockInStatus.Cancelled, cancelled.Status);

        var prodAfterCancel = await context.Products.FindAsync(product.Id);
        Assert.Equal(20, prodAfterCancel!.CurrentStock); // reverted back to 20

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ConfirmStockIn_MultiWarehouse_IncrementsCorrectWarehouseOnly()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var category = new Category { Name = "Cat WH " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "WH Prod", Barcode = "WHP1", CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);

        var wh1 = new Warehouse { Code = "WH-1", Name = "Kho 1", IsDefault = true, IsActive = true };
        var wh2 = new Warehouse { Code = "WH-2", Name = "Kho 2", IsDefault = false, IsActive = true };
        context.Warehouses.AddRange(wh1, wh2);
        await context.SaveChangesAsync();

        context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh1.Id, ProductId = product.Id, Stock = 5 });
        context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh2.Id, ProductId = product.Id, Stock = 5 });
        await context.SaveChangesAsync();

        var req = new StockInCreateRequest(
            WarehouseId: wh1.Id,
            SupplierId: null,
            SupplierName: "Supplier WH",
            Note: "WH Test",
            Items: new List<StockInItemRequest> { new(product.Id, 10, 1000m) }
        );

        var draft = await service.CreateStockInDraftAsync(1, req);
        await service.ConfirmStockInDraftAsync(1, draft.Id, null);

        var wh1Stock = await context.WarehouseStocks.FirstAsync(s => s.WarehouseId == wh1.Id && s.ProductId == product.Id);
        var wh2Stock = await context.WarehouseStocks.FirstAsync(s => s.WarehouseId == wh2.Id && s.ProductId == product.Id);

        Assert.Equal(15, wh1Stock.Stock); // 5 + 10
        Assert.Equal(5, wh2Stock.Stock);   // unchanged

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task ConfirmStockIn_MidStreamError_RollsBackEverything()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var category = new Category { Name = "Cat Rollback " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Rollback Prod", Barcode = "RB1", CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var stockBefore = product.CurrentStock;
        var ledgerCountBefore = await context.StockLedger.CountAsync();

        var req = new StockInCreateRequest(
            WarehouseId: null,
            SupplierId: null,
            SupplierName: "Supplier RB",
            Note: "Rollback Test",
            Items: new List<StockInItemRequest> { new(999999, 10, 1000m) }
        );

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateStockInDraftAsync(1, req));

        var productAfter = await context.Products.FindAsync(product.Id);
        Assert.Equal(stockBefore, productAfter!.CurrentStock);
        Assert.Equal(ledgerCountBefore, await context.StockLedger.CountAsync());

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CancelStockIn_CreatesReversalLedgerAndRestoresStock()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var category = new Category { Name = "Cat Cancel " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Cancel Prod", Barcode = "CP1", CategoryId = category.Id, CurrentStock = 20, IsActive = true };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new StockInCreateRequest(null, null, "Sup", "Note", new List<StockInItemRequest> { new(product.Id, 5, 1000m) });
        var draft = await service.CreateStockInDraftAsync(1, req);
        await service.ConfirmStockInDraftAsync(1, draft.Id, null);

        Assert.Equal(25, (await context.Products.FindAsync(product.Id))!.CurrentStock);

        await service.CancelStockInAsync(1, draft.Id);

        Assert.Equal(20, (await context.Products.FindAsync(product.Id))!.CurrentStock);
        var reversalLedger = await context.StockLedger.OrderByDescending(l => l.Id).FirstOrDefaultAsync();
        Assert.NotNull(reversalLedger);
        Assert.Equal(-5, reversalLedger!.Qty);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CancelStockIn_BlockedWhenStockWouldBecomeNegative()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var category = new Category { Name = "Cat Negative " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Neg Prod", Barcode = "NP1", CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new StockInCreateRequest(null, null, "Sup", "Note", new List<StockInItemRequest> { new(product.Id, 10, 1000m) });
        var draft = await service.CreateStockInDraftAsync(1, req);
        await service.ConfirmStockInDraftAsync(1, draft.Id, null);

        product.CurrentStock = 2;
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CancelStockInAsync(1, draft.Id));

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CancelStockIn_BlockedForBackfilledOldReceipt()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var category = new Category { Name = "Cat Old " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Old Prod", Barcode = "OP-" + Guid.NewGuid().ToString().Substring(0, 6), CategoryId = category.Id, CurrentStock = 20, IsActive = true };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var wh = await context.Warehouses.FirstOrDefaultAsync();
        if (wh == null)
        {
            wh = new Warehouse { Code = "WH-OLD", Name = "Kho Cũ", IsDefault = true, IsActive = true };
            context.Warehouses.Add(wh);
            await context.SaveChangesAsync();
        }

        var oldStockIn = new StockIn
        {
            Code = "PHNK-OLD-" + Guid.NewGuid().ToString().Substring(0, 6),
            WarehouseId = wh.Id,
            Status = StockInStatus.Confirmed,
            IsBackfilled = true,
            Total = 10000m,
            CreatedBy = 1,
            Items = new List<StockInItem>
            {
                new() { ProductId = product.Id, Qty = 5, UnitCost = 2000m, Total = 10000m }
            }
        };
        context.StockIns.Add(oldStockIn);
        await context.SaveChangesAsync();
        await tx.CommitAsync();

        var (context2, tx2) = _fixture.CreateContextWithTransaction();
        var service2 = new StockInService(context2, logger.Object);

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service2.CancelStockInAsync(1, oldStockIn.Id));
        Assert.Contains("backfill", ex.Message, StringComparison.OrdinalIgnoreCase);

        await tx2.RollbackAsync();
    }

    [Fact]
    public async Task ConfirmStockIn_Successive_AccumulatesStockCorrectly()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var category = new Category { Name = "Cat Concurrent " + Guid.NewGuid() };
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Concurrent Prod", Barcode = "CP2-" + Guid.NewGuid(), CategoryId = category.Id, CurrentStock = 10, IsActive = true };
        context.Products.Add(product);
        await context.SaveChangesAsync();

        var wh = await context.Warehouses.FirstOrDefaultAsync(w => w.IsDefault) ?? await context.Warehouses.FirstOrDefaultAsync();
        if (wh == null)
        {
            wh = new Warehouse { Code = "WH-DEF", Name = "Kho Def", IsDefault = true, IsActive = true };
            context.Warehouses.Add(wh);
        }
        context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m });
        await context.SaveChangesAsync();

        var req1 = new StockInCreateRequest(wh.Id, null, "Sup 1", "Note 1", new List<StockInItemRequest> { new(product.Id, 5, 1000m) });
        var req2 = new StockInCreateRequest(wh.Id, null, "Sup 2", "Note 2", new List<StockInItemRequest> { new(product.Id, 7, 1000m) });

        var draft1 = await service.CreateStockInDraftAsync(1, req1);
        var draft2 = await service.CreateStockInDraftAsync(1, req2);

        await service.ConfirmStockInDraftAsync(1, draft1.Id, null);
        await service.ConfirmStockInDraftAsync(1, draft2.Id, null);

        var finalProduct = await context.Products.FindAsync(product.Id);
        Assert.Equal(22, finalProduct!.CurrentStock); // 10 + 5 + 7 = 22

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Parallel_StockIn_RealCommit_20Iterations()
    {
        int whId, prodId;
        using (var setupCtx = _fixture.CreateRealContext())
        {
            var cat = new Category { Name = "Parallel Cat " + Guid.NewGuid() };
            setupCtx.Categories.Add(cat);
            await setupCtx.SaveChangesAsync();

            var product = new Product { Name = "Parallel Prod " + Guid.NewGuid(), Barcode = "PAR-" + Guid.NewGuid().ToString().Substring(0, 6), CategoryId = cat.Id, CurrentStock = 0m, IsActive = true };
            setupCtx.Products.Add(product);

            var wh = new Warehouse { Code = "WH-PAR-" + Guid.NewGuid().ToString().Substring(0, 4), Name = "Kho Parallel", IsDefault = false, IsActive = true };
            setupCtx.Warehouses.Add(wh);
            await setupCtx.SaveChangesAsync();

            whId = wh.Id;
            prodId = product.Id;
        }

        var tcs = new TaskCompletionSource<bool>();
        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(5, 5);

        for (int i = 0; i < 20; i++)
        {
            int iteration = i;
            tasks.Add(Task.Run(async () =>
            {
                await tcs.Task;
                await semaphore.WaitAsync();
                try
                {
                    using var realCtx = _fixture.CreateRealContext();
                    var strategy = realCtx.Database.CreateExecutionStrategy();
                    await strategy.ExecuteAsync(async () =>
                    {
                        using var tx = await realCtx.Database.BeginTransactionAsync();
                        var logger = new Mock<ILogger<StockInService>>();
                        var service = new StockInService(realCtx, logger.Object);

                        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
                        realCtx.SupplierInvoices.Add(invoice);
                        await realCtx.SaveChangesAsync();

                        var req = new ConfirmStockInRequest(
                            invoice.Id,
                            new List<StockInItemRequest> { new(prodId, 1m, 1000m) },
                            $"Sup {iteration}",
                            "Parallel"
                        );

                        await service.ConfirmStockInAsync(1, req, $"idem-par-{iteration}-{Guid.NewGuid()}");
                        await tx.CommitAsync();
                    });
                }
                finally
                {
                    semaphore.Release();
                }
            }));
        }

        tcs.SetResult(true);
        await Task.WhenAll(tasks);

        using var verifyCtx = _fixture.CreateRealContext();
        var finalProd = await verifyCtx.Products.FindAsync(prodId);
        Assert.Equal(20m, finalProd!.CurrentStock);
        var finalWhStock = await verifyCtx.WarehouseStocks.Where(s => s.ProductId == prodId).SumAsync(s => s.Stock);
        Assert.Equal(20m, finalWhStock);
        var ledgerCount = await verifyCtx.StockLedger.CountAsync(l => l.ProductId == prodId && l.RefType == LedgerRefType.Purchase);
        Assert.Equal(20, ledgerCount);
    }

    [Fact]
    public async Task Migration_And_Backfill_Test()
    {
        using (var ctx = _fixture.CreateRealContext())
        {
            await ctx.Database.ExecuteSqlRawAsync(@"
                TRUNCATE TABLE ""OrderItems"", ""StockInItems"", ""StockIns"", ""WarehouseStocks"", ""Products"", ""Categories"", ""Warehouses"" RESTART IDENTITY CASCADE;
            ");

            var cat = new Category { Name = "Mig Cat " + Guid.NewGuid() };
            ctx.Categories.Add(cat);
            await ctx.SaveChangesAsync();

            var product = new Product { Name = "Mig Prod", Barcode = "MIG-01", CategoryId = cat.Id, CurrentStock = 10m, IsActive = true };
            ctx.Products.Add(product);

            var wh = new Warehouse { Code = "WH-MIG", Name = "Kho Mig", IsDefault = true, IsActive = true };
            ctx.Warehouses.Add(wh);
            await ctx.SaveChangesAsync();

            await ctx.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""StockIns"" (""Code"", ""WarehouseId"", ""Status"", ""IsBackfilled"", ""Total"", ""CreatedBy"", ""CreatedAt"")
                  VALUES ('PHNK-OLD-MIG', {0}, 1, true, 5000, 1, NOW())", wh.Id);

            var stockInId = await ctx.StockIns.Where(s => s.Code == "PHNK-OLD-MIG").Select(s => s.Id).FirstAsync();

            await ctx.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""StockInItems"" (""StockInId"", ""ProductId"", ""Qty"", ""UnitCost"", ""Total"")
                  VALUES ({0}, {1}, 5, 1000, 5000)", stockInId, product.Id);
        }

        using (var ctx2 = _fixture.CreateRealContext())
        {
            var oldStockIn = await ctx2.StockIns.Include(s => s.Items).FirstOrDefaultAsync(s => s.Code == "PHNK-OLD-MIG");
            Assert.NotNull(oldStockIn);
            Assert.True(oldStockIn!.IsBackfilled);
            Assert.Equal(StockInStatus.Confirmed, oldStockIn.Status);
            Assert.NotEqual(0, oldStockIn.WarehouseId);

            await InventoryReconciler.ReconcileAsync(ctx2);

            var logger = new Mock<ILogger<StockInService>>();
            var service = new StockInService(ctx2, logger.Object);
            var prodId = oldStockIn.Items.First().ProductId;
            var req = new StockInCreateRequest(oldStockIn.WarehouseId, null, "New Sup", "New", new List<StockInItemRequest> { new(prodId, 2m, 1000m) });
            var draft = await service.CreateStockInDraftAsync(1, req);
            var confirmed = await service.ConfirmStockInDraftAsync(1, draft.Id, null);

            var newStockIn = await ctx2.StockIns.FirstOrDefaultAsync(s => s.Code == confirmed.Code);
            Assert.NotNull(newStockIn);
            Assert.False(newStockIn!.IsBackfilled);
            Assert.Equal(StockInStatus.Confirmed, newStockIn.Status);
        }
    }


    [Fact]
    public async Task Idempotency_SameKey_DifferentPayload_ThrowsConflict()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var cat = new Category { Name = "Idem Cat " + Guid.NewGuid() };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var p1 = new Product { Name = "P1", Barcode = "IDEM-1", CategoryId = cat.Id, CurrentStock = 10m, IsActive = true };
        var p2 = new Product { Name = "P2", Barcode = "IDEM-2", CategoryId = cat.Id, CurrentStock = 10m, IsActive = true };
        context.Products.AddRange(p1, p2);

        var wh = new Warehouse { Code = "WH-IDEM", Name = "Kho Idem", IsDefault = true, IsActive = true };
        context.Warehouses.Add(wh);
        await context.SaveChangesAsync();

        await InventoryReconciler.ReconcileAsync(context);

        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();

        var key = "same-idem-key";
        var req1 = new ConfirmStockInRequest(invoice.Id, new List<StockInItemRequest> { new(p1.Id, 1m, 1000m) }, "Sup 1", "Note 1");
        var req2 = new ConfirmStockInRequest(invoice.Id, new List<StockInItemRequest> { new(p2.Id, 2m, 1000m) }, "Sup 2", "Note 2");

        await service.ConfirmStockInAsync(1, req1, key);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => service.ConfirmStockInAsync(1, req2, key));
        Assert.Contains("Idempotency-Key", ex.Message);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Q14_BlockingGuard_ThrowsWhenMismatched()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var cat = new Category { Name = "Q14 Cat 1 " + Guid.NewGuid() };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var pOver = new Product { Name = "Over Stock Prod 1", Barcode = "Q14-1", CategoryId = cat.Id, CurrentStock = 10m, IsActive = true };
        var wh = new Warehouse { Code = "WH-Q14-1", Name = "Kho Q14 1", IsDefault = true, IsActive = true };
        context.Products.Add(pOver);
        context.Warehouses.Add(wh);
        await context.SaveChangesAsync();

        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();

        var req = new ConfirmStockInRequest(invoice.Id, new List<StockInItemRequest> { new(pOver.Id, 5m, 1000m) }, "Sup", "Note");
        await Assert.ThrowsAsync<BusinessRuleException>(() => service.ConfirmStockInAsync(1, req, "idem-q14-1"));

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Q14_InventoryReconciler_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object);

        var cat2 = new Category { Name = "Q14 Cat 2 " + Guid.NewGuid() };
        context.Categories.Add(cat2);
        await context.SaveChangesAsync();

        var pOver2 = new Product { Name = "Over Stock Prod 2", Barcode = "Q14-2", CategoryId = cat2.Id, CurrentStock = 10m, IsActive = true };
        var wh2 = new Warehouse { Code = "WH-Q14-2", Name = "Kho Q14 2", IsDefault = true, IsActive = true };
        context.Products.Add(pOver2);
        context.Warehouses.Add(wh2);
        await context.SaveChangesAsync();

        await InventoryReconciler.ReconcileAsync(context, wh2.Id);

        var whStock = await context.WarehouseStocks.FirstOrDefaultAsync(s => s.WarehouseId == wh2.Id && s.ProductId == pOver2.Id);
        Assert.NotNull(whStock);
        Assert.Equal(10m, whStock!.Stock);

        // Idempotency check for reconciler
        await InventoryReconciler.ReconcileAsync(context, wh2.Id);
        var ledgerCount = await context.StockLedger.CountAsync(l => l.RefId != null && l.RefId.StartsWith("Q14-RECONCILE-" + pOver2.Id));
        Assert.Equal(1, ledgerCount);

        var invoice2 = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice2);
        await context.SaveChangesAsync();

        var req2 = new ConfirmStockInRequest(invoice2.Id, new List<StockInItemRequest> { new(pOver2.Id, 5m, 1000m) }, "Sup", "Note");
        var res = await service.ConfirmStockInAsync(1, req2, "idem-q14-2");
        Assert.NotNull(res);

        var finalProd = await context.Products.FindAsync(pOver2.Id);
        var totalWh = await context.WarehouseStocks.Where(s => s.ProductId == pOver2.Id).SumAsync(s => s.Stock);
        Assert.Equal(finalProd!.CurrentStock, totalWh);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Q13_WeightedAverageCost_And_Cancel_Behavior()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "Inventory:UpdateCostOnStockIn", "true" } }).Build();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object, config);

        var cat = new Category { Name = "Q13 Cat " + Guid.NewGuid() };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var product = new Product { Name = "Cost Prod", Barcode = "Q13-1", CategoryId = cat.Id, CurrentStock = 10m, CostPrice = 10000m, IsActive = true };
        var wh = new Warehouse { Code = "WH-Q13", Name = "Kho Q13", IsDefault = true, IsActive = true };
        context.Products.Add(product);
        context.Warehouses.Add(wh);
        await context.SaveChangesAsync();
        context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m });
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();
        await InventoryReconciler.ReconcileAsync(context);

        var req = new ConfirmStockInRequest(invoice.Id, new List<StockInItemRequest> { new(product.Id, 10m, 20000m) }, "Sup", "Note");
        var res = await service.ConfirmStockInAsync(1, req, "idem-q13-1");

        var updatedProd = await context.Products.FindAsync(product.Id);
        Assert.Equal(15000m, updatedProd!.CostPrice);

        var stockInItem = await context.StockInItems.Include(i => i.StockIn).FirstAsync(i => i.StockIn.Code == res.Code);
        Assert.Equal(10000m, stockInItem.CostBefore);
        Assert.Equal(15000m, stockInItem.CostAfter);

        await service.CancelStockInAsync(1, stockInItem.StockInId);
        var restoredProd = await context.Products.FindAsync(product.Id);
        Assert.Equal(10000m, restoredProd!.CostPrice);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Q14_Reconciler_UnderStockGroup_NotModified()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var cat = new Category { Name = "Q14 Under " + Guid.NewGuid() };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var pUnder = new Product { Name = "Under Stock Prod", Barcode = "Q14-U1", CategoryId = cat.Id, CurrentStock = 5m, IsActive = true };
        var wh = new Warehouse { Code = "WH-Q14-U", Name = "Kho Under", IsDefault = true, IsActive = true };
        context.Products.Add(pUnder);
        context.Warehouses.Add(wh);
        await context.SaveChangesAsync();
        context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = pUnder.Id, Stock = 10m });
        await context.SaveChangesAsync();

        await InventoryReconciler.ReconcileAsync(context, wh.Id);

        var whStock = await context.WarehouseStocks.FirstAsync(s => s.WarehouseId == wh.Id && s.ProductId == pUnder.Id);
        Assert.Equal(10m, whStock.Stock);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Q14_Reconciler_ThrowsWhenNoWarehouse()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"Warehouses\" RESTART IDENTITY CASCADE;");

        await Assert.ThrowsAsync<InvalidOperationException>(() => InventoryReconciler.ReconcileAsync(context));

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task Q13_ConfigFlag_Disabled_DoesNotUpdateCostPrice()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { { "Inventory:UpdateCostOnStockIn", "false" } }).Build();
        var logger = new Mock<ILogger<StockInService>>();
        var service = new StockInService(context, logger.Object, config);

        var cat = new Category { Name = "Q13 CFG " + Guid.NewGuid() };
        context.Categories.Add(cat);
        await context.SaveChangesAsync();

        var product = new Product { Name = "CFG Prod", Barcode = "Q13-CFG1", CategoryId = cat.Id, CurrentStock = 10m, CostPrice = 10000m, IsActive = true };
        var wh = new Warehouse { Code = "WH-CFG", Name = "Kho CFG", IsDefault = true, IsActive = true };
        context.Products.Add(product);
        context.Warehouses.Add(wh);
        await context.SaveChangesAsync();
        context.WarehouseStocks.Add(new WarehouseStock { WarehouseId = wh.Id, ProductId = product.Id, Stock = 10m });
        var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
        context.SupplierInvoices.Add(invoice);
        await context.SaveChangesAsync();

        var req = new ConfirmStockInRequest(invoice.Id, new List<StockInItemRequest> { new(product.Id, 10m, 20000m) }, "Sup", "Note");
        await service.ConfirmStockInAsync(1, req, "idem-q13-cfg-off");

        var updatedProd = await context.Products.FindAsync(product.Id);
        Assert.Equal(10000m, updatedProd!.CostPrice);

        await tx.RollbackAsync();
    }
}
