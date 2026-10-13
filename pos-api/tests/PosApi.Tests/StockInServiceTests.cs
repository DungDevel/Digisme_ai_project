using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
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

        var req = new ConfirmStockInRequest(invoice.Id, new List<StockInItemRequest> { new(product.Id, 1, 10) }, "S", "N");
        var key = "idem-key-2";

        var res1 = await service.ConfirmStockInAsync(1, req, key);
        var res2 = await service.ConfirmStockInAsync(1, req, key);

        Assert.Equal(res1.Code, res2.Code);
        Assert.Equal(res1.Id, res2.Id);

        // Verify only one StockIn was created
        var count = await context.StockIns.CountAsync();
        Assert.Equal(1, count);

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

        await service.RejectInvoiceAsync(invoice.Id);

        var updated = await context.SupplierInvoices.FindAsync(invoice.Id);
        Assert.Equal(SupplierInvoiceStatus.Rejected, updated!.Status);

        await tx.RollbackAsync();
    }
}
