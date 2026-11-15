using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class CrudAndImageTests
{
    private readonly DatabaseFixture _fixture = new();

    [Fact]
    public async Task ProductService_CRUD_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var mockStorage = new Mock<IFileStorageService>();
            var mockLog = new Mock<ILogger<ProductService>>();
            var service = new ProductService(context, mockStorage.Object, mockLog.Object);

            // 1. Create Category first if needed, or use existing category ID 1
            var cat = new Category { Name = "Test Category CRUD" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            // 2. Create Product
            var createReq = new ProductCreateRequest(
                Name: "Coca Cola",
                CategoryId: cat.Id,
                Barcode: "8931234567890",
                CostPrice: 8000m,
                RetailPrice: 10000m,
                WholesalePrice: 9500m,
                MemberPrice: 9000m,
                MinStock: 5m,
                IsWeighed: false,
                ImageUrl: null
            );

            var productId = await service.CreateAsync(createReq, userId: 1, imageFile: null);
            Assert.True(productId > 0);

            // 3. Verify lookup
            var lookup = await service.GetByBarcodeAsync("8931234567890");
            Assert.NotNull(lookup);
            Assert.Equal("Coca Cola", lookup.Name);
            Assert.Equal(10000m, lookup.RetailPrice);

            // 4. Update Product
            var updateReq = new ProductUpdateRequest(
                Name: "Coca Cola Updated",
                CategoryId: cat.Id,
                CostPrice: 8500m,
                RetailPrice: 11000m,
                WholesalePrice: 10500m,
                MemberPrice: 9500m,
                MinStock: 10m,
                IsWeighed: false,
                ImageUrl: null,
                IsActive: true
            );

            await service.UpdateAsync(productId, updateReq, userId: 1, imageFile: null);

            var updatedLookup = await service.GetByBarcodeAsync("8931234567890");
            Assert.Equal("Coca Cola Updated", updatedLookup.Name);
            Assert.Equal(11000m, updatedLookup.RetailPrice);

            // 5. Delete (Soft delete)
            await service.DeleteAsync(productId, userId: 1);
            var deletedLookup = await service.GetByBarcodeAsync("8931234567890");
            Assert.Null(deletedLookup);
        }
    }

    [Fact]
    public async Task CustomerService_CRUD_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerService(context);

            // 1. Create Customer
            var createReq = new CustomerCreateRequest("0901234567", "Nguyen Van A", "123 Le Loi");
            var customerId = await service.CreateAsync(createReq, userId: 1);
            Assert.True(customerId > 0);

            // 2. Get by phone
            var cust = await service.GetByPhoneAsync("0901234567");
            Assert.NotNull(cust);
            Assert.Equal("Nguyen Van A", cust.Name);

            // 3. Update customer
            var updateReq = new CustomerUpdateRequest("Nguyen Van B", "456 Tran Hung Dao", true);
            await service.UpdateAsync(customerId, updateReq, userId: 1);

            var updatedCust = await service.GetByPhoneAsync("0901234567");
            Assert.Equal("Nguyen Van B", updatedCust.Name);
            Assert.Equal("456 Tran Hung Dao", updatedCust.Address);

            // 4. Delete (Soft delete)
            await service.DeleteAsync(customerId, userId: 1);
            var deletedCust = await service.GetByPhoneAsync("0901234567");
            Assert.Null(deletedCust);
        }
    }

    [Fact]
    public async Task FileStorageService_RejectsNonImageFiles()
    {
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.WebRootPath).Returns(Path.GetTempPath());
        var mockLog = new Mock<ILogger<FileStorageService>>();
        var storage = new FileStorageService(mockEnv.Object, mockLog.Object);

        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("malware.exe");
        mockFile.Setup(f => f.ContentType).Returns("application/x-msdownload");
        mockFile.Setup(f => f.Length).Returns(100);

        await Assert.ThrowsAsync<BusinessRuleException>(() => storage.SaveFileAsync(mockFile.Object, "test"));
    }
}
