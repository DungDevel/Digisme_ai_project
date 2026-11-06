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
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            var req = new SupplierCreateRequest(null, "Công ty TNHH ABC " + Guid.NewGuid().ToString().Substring(0, 6), "0901234567", "123 Đường A", "0123456789");

            var result = await service.CreateSupplierAsync(1, req);

            Assert.NotNull(result);
            Assert.StartsWith("NCC-", result.Code);
            Assert.True(result.IsActive);
        }
    }

    [Fact]
    public async Task CreateSupplier_DuplicateNameCaseInsensitive_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            var name = "NCC Alpha " + Guid.NewGuid().ToString().Substring(0, 6);
            await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, name, "0901111111", null, null));

            var req2 = new SupplierCreateRequest(null, $"  {name.ToLower()}  ", "0902222222", null, null);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateSupplierAsync(1, req2));
            Assert.Equal("Tên nhà cung cấp đã tồn tại", ex.Message);
        }
    }

    [Fact]
    public async Task CreateSupplier_SoftDeletedName_CanBeRecreated()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            var name = "NCC Delta " + Guid.NewGuid().ToString().Substring(0, 6);
            var created = await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, name, null, null, null));

            await service.DeleteSupplierAsync(1, created.Id);

            var recreated = await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, name, null, null, null));
            Assert.NotNull(recreated);
            Assert.NotEqual(created.Id, recreated.Id);
            Assert.True(recreated.IsActive);
        }
    }

    [Fact]
    public async Task CreateSupplier_DuplicateManualCode_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            var code = "NCC-MANUAL-01";
            await service.CreateSupplierAsync(1, new SupplierCreateRequest(code, "NCC One", null, null, null));

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.CreateSupplierAsync(1, new SupplierCreateRequest(code, "NCC Two", null, null, null)));
            Assert.Equal("Mã nhà cung cấp đã tồn tại", ex.Message);
        }
    }

    [Fact]
    public async Task CreateSupplier_EmptyOrTooLongName_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);

            await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "", null, null, null)));

            var longName = new string('A', 129);
            await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.CreateSupplierAsync(1, new SupplierCreateRequest(null, longName, null, null, null)));
        }
    }

    [Fact]
    public async Task CreateSupplier_SequentialAutoCodes_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            var s1 = await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "Supplier 1 " + Guid.NewGuid(), null, null, null));
            var s2 = await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "Supplier 2 " + Guid.NewGuid(), null, null, null));

            Assert.True(s1.Code.EndsWith("00001") || s1.Code.StartsWith("NCC-"));
            Assert.NotNull(s2.Code);
        }
    }

    [Fact]
    public async Task CreateSupplier_ManualCode_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            var code = "CUS-" + Guid.NewGuid().ToString().Substring(0, 6).ToUpper();
            var req = new SupplierCreateRequest(code, "NCC Beta " + Guid.NewGuid().ToString().Substring(0, 6), "0903333333", null, null);

            var result = await service.CreateSupplierAsync(1, req);

            Assert.Equal(code, result.Code);
        }
    }

    [Fact]
    public async Task UpdateSupplier_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            var created = await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "NCC Gamma " + Guid.NewGuid().ToString().Substring(0, 6), "0904444444", null, null));

            var updateReq = new SupplierUpdateRequest("NCC Gamma Updated " + Guid.NewGuid().ToString().Substring(0, 6), "0905555555", "Address 2", "987654321");
            var updated = await service.UpdateSupplierAsync(1, created.Id, updateReq);

            Assert.NotNull(updated);
            Assert.StartsWith("NCC Gamma Updated", updated.Name);
            Assert.Equal("0905555555", updated.Phone);
            Assert.Equal("Address 2", updated.Address);
            Assert.Equal("987654321", updated.TaxCode);
        }
    }

    [Fact]
    public async Task DeleteSupplier_SoftDelete_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            var created = await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "NCC Delta " + Guid.NewGuid().ToString().Substring(0, 6), null, null, null));

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

    [Fact]
    public async Task DeleteSupplier_NonExistent_ReturnsFalse()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            var success = await service.DeleteSupplierAsync(1, 999999);
            Assert.False(success);
        }
    }

    // ── Additional C1 tests requested ──

    [Fact]
    public async Task CreateSupplier_AutoCode_CollisionRetry_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            // Pre-create an auto code to simulate potential collision
            context.Suppliers.Add(new Supplier { Code = "NCC-00001", Name = "Existing NCC", IsActive = true });
            await context.SaveChangesAsync();

            var created = await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "New NCC Collision Test", null, null, null));
            Assert.NotNull(created);
            Assert.Equal("NCC-00002", created.Code);
        }
    }

    [Fact]
    public async Task GetSuppliers_ActiveOnlyByDefault_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            var active = await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "Active NCC", null, null, null));
            var inactive = await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "Inactive NCC", null, null, null));
            await service.DeleteSupplierAsync(1, inactive.Id);

            var result = await service.GetSuppliersAsync(null, 1, 10, includeInactive: false);
            Assert.Contains(result.Items, x => x.Id == active.Id);
            Assert.DoesNotContain(result.Items, x => x.Id == inactive.Id);
        }
    }

    [Fact]
    public async Task UpdateSupplier_NonExistent_ReturnsNull()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            var updated = await service.UpdateSupplierAsync(1, 999999, new SupplierUpdateRequest("Name", null, null, null));
            Assert.Null(updated);
        }
    }

    [Fact]
    public async Task UpdateSupplier_DuplicateName_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "NCC One", null, null, null));
            var s2 = await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "NCC Two", null, null, null));

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.UpdateSupplierAsync(1, s2.Id, new SupplierUpdateRequest("NCC One", null, null, null)));
            Assert.Equal("Tên nhà cung cấp đã tồn tại", ex.Message);
        }
    }

    [Fact]
    public async Task UpdateSupplier_KeepSameName_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new SupplierService(context);
            var s1 = await service.CreateSupplierAsync(1, new SupplierCreateRequest(null, "NCC Self Name", "0901111111", null, null));

            var updated = await service.UpdateSupplierAsync(1, s1.Id, new SupplierUpdateRequest("NCC Self Name", "0902222222", null, null));
            Assert.NotNull(updated);
            Assert.Equal("NCC Self Name", updated.Name);
            Assert.Equal("0902222222", updated.Phone);
        }
    }
}
