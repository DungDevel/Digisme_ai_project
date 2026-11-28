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
    public async Task ProductService_Create_ZeroStock_MaintainsInvariant()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var mockStorage = new Mock<IFileStorageService>();
            var mockLog = new Mock<ILogger<ProductService>>();
            var service = new ProductService(context, mockStorage.Object, mockLog.Object);

            var cat = new Category { Name = "Cat Q18 Reg " + Guid.NewGuid().ToString("N") };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            var createReq = new ProductCreateRequest(
                Name: "Prod Zero Stock",
                CategoryId: cat.Id,
                Barcode: "B-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                CostPrice: 1000m,
                RetailPrice: 2000m,
                WholesalePrice: 1500m,
                MemberPrice: 1200m,
                MinStock: 2m,
                IsWeighed: false,
                ImageUrl: null
            );

            var productId = await service.CreateAsync(createReq, userId: 1, imageFile: null);
            var product = await context.Products.FindAsync(productId);
            Assert.NotNull(product);
            Assert.Equal(0m, product.CurrentStock);

            var totalWhStock = await context.WarehouseStocks
                .Where(ws => ws.ProductId == productId)
                .SumAsync(ws => (decimal?)ws.Stock) ?? 0m;

            Assert.Equal(product.CurrentStock, totalWhStock);
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

    [Fact]
    public async Task ProductService_UpdateImage_Success_MaintainsOtherFields()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var mockStorage = new Mock<IFileStorageService>();
            mockStorage.Setup(s => s.SaveFileAsync(It.IsAny<IFormFile>(), "products")).ReturnsAsync("uploads/products/new.jpg");
            mockStorage.Setup(s => s.DeleteFileAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

            var mockLog = new Mock<ILogger<ProductService>>();
            var service = new ProductService(context, mockStorage.Object, mockLog.Object);

            var cat = new Category { Name = "Test Cat Image" };
            context.Categories.Add(cat);
            await context.SaveChangesAsync();

            var product = new Product
            {
                Name = "Prod Test Image",
                CategoryId = cat.Id,
                Barcode = "8939999999999",
                CostPrice = 12345m,
                RetailPrice = 20000m,
                WholesalePrice = 18000m,
                MemberPrice = 17000m,
                MinStock = 5m,
                IsActive = true,
                ImageUrl = "uploads/products/old.jpg"
            };
            context.Products.Add(product);
            await context.SaveChangesAsync();
            int pId = product.Id;

            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.FileName).Returns("test.jpg");
            mockFile.Setup(f => f.ContentType).Returns("image/jpeg");
            mockFile.Setup(f => f.Length).Returns(1000);

            var newUrl = await service.UpdateImageAsync(pId, mockFile.Object, userId: 1);
            Assert.Equal("uploads/products/new.jpg", newUrl);

            var updatedProd = await context.Products.FindAsync(pId);
            Assert.Equal("uploads/products/new.jpg", updatedProd!.ImageUrl);
            Assert.Equal(12345m, updatedProd.CostPrice);
            Assert.Equal(5m, updatedProd.MinStock);
            Assert.True(updatedProd.IsActive);
            Assert.Equal(20000m, updatedProd.RetailPrice);

            mockStorage.Verify(s => s.DeleteFileAsync("uploads/products/old.jpg"), Times.Once);
        }
    }

    [Fact]
    public async Task ProductService_UpdateImage_Validations()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var mockStorage = new Mock<IFileStorageService>();
            var mockLog = new Mock<ILogger<ProductService>>();
            var service = new ProductService(context, mockStorage.Object, mockLog.Object);

            // 1. Non-existent product
            var mockFile = new Mock<IFormFile>();
            mockFile.Setup(f => f.Length).Returns(100);
            await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateImageAsync(99999, mockFile.Object, userId: 1));

            // 2. Empty file
            var emptyFile = new Mock<IFormFile>();
            emptyFile.Setup(f => f.Length).Returns(0);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateImageAsync(1, emptyFile.Object, userId: 1));

            // 3. Oversized file > 2MB
            var largeFile = new Mock<IFormFile>();
            largeFile.Setup(f => f.Length).Returns(3 * 1024 * 1024);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateImageAsync(1, largeFile.Object, userId: 1));
        }
    }
}
