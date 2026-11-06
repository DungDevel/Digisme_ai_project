using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class SupplierServiceTests
{
    private readonly DatabaseFixture _fixture = new();

    [Fact]
    public async Task CreateSupplier_AutoCode_Success()
    {
        _fixture.ClearData();
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (tx)
        {
            var service = new SupplierService(context);
            var req = new SupplierCreateRequest(null, "Công ty TNHH ABC", "0901234567", "123 Đường A", "0123456789");

            var result = await service.CreateSupplierAsync(1, req);

            Assert.NotNull(result);
            Assert.StartsWith("NCC-", result.Code);
            Assert.Equal("Công ty TNHH ABC", result.Name);
            Assert.True(result.IsActive);
        }
    }

    [Fact]
    public async Task CreateSupplier_DuplicateNameActive_ThrowsException()
    {
        _fixture.ClearData();
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (tx)
        {
            var service = new SupplierService(context);
            await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "NCC Alpha", "0901111111", null, null));

            var req2 = new SupplierCreateRequest(null, "  ncc alpha  ", "0902222222", null, null);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateSupplierAsync(1, req2));
            Assert.Equal("Tên nhà cung cấp đã tồn tại", ex.Message);
        }
    }

    [Fact]
    public async Task CreateSupplier_ManualCode_Success()
    {
        _fixture.ClearData();
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (tx)
        {
            var service = new SupplierService(context);
            var req = new SupplierCreateRequest("CUSTOM-01", "NCC Beta", "0903333333", null, null);

            var result = await service.CreateSupplierAsync(1, req);

            Assert.Equal("CUSTOM-01", result.Code);
            Assert.Equal("NCC Beta", result.Name);
        }
    }

    [Fact]
    public async Task UpdateSupplier_Success()
    {
        _fixture.ClearData();
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (tx)
        {
            var service = new SupplierService(context);
            var created = await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "NCC Gamma", "0904444444", null, null));

            var updateReq = new SupplierUpdateRequest("NCC Gamma Updated", "0905555555", "Address 2", "987654321");
            var updated = await service.UpdateSupplierAsync(1, created.Id, updateReq);

            Assert.Equal("NCC Gamma Updated", updated.Name);
            Assert.Equal("0905555555", updated.Phone);
            Assert.Equal("Address 2", updated.Address);
            Assert.Equal("987654321", updated.TaxCode);
        }
    }

    [Fact]
    public async Task DeleteSupplier_SoftDelete_Success()
    {
        _fixture.ClearData();
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (tx)
        {
            var service = new SupplierService(context);
            var created = await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "NCC Delta", null, null, null));

            var success = await service.DeleteSupplierAsync(1, created.Id);
            Assert.True(success);

            var list = await service.GetSuppliersAsync(null, 1, 10, includeInactive: false);
            Assert.DoesNotContain(list.Items, x => x.Id == created.Id);

            var listWithInactive = await service.GetSuppliersAsync(null, 1, 10, includeInactive: true);
            var deletedItem = listWithInactive.Items.FirstOrDefault(x => x.Id == created.Id);
            Assert.NotNull(deletedItem);
            Assert.False(deletedItem.IsActive);
        }
    }
}
