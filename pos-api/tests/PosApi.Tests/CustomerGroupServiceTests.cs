using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class CustomerGroupServiceTests
{
    private readonly DatabaseFixture _fixture = new();

    [Fact]
    public async Task CreateCustomerGroup_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var req = new CustomerGroupCreateRequest("CG-01", "Nhóm VIP 1", false, 10.0m, null);

            var result = await service.CreateCustomerGroupAsync(1, req);

            Assert.NotNull(result);
            Assert.Equal("CG-01", result.Code);
            Assert.Equal("Nhóm VIP 1", result.Name);
            Assert.False(result.IsSupplier);
            Assert.Equal(10.0m, result.DefaultDiscountRate);
            Assert.True(result.IsActive);
        }
    }

    [Fact]
    public async Task CreateCustomerGroup_DuplicateCode_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var req = new CustomerGroupCreateRequest("CG-DUP", "Nhóm 1", false, 5m, null);
            await service.CreateCustomerGroupAsync(1, req);

            var req2 = new CustomerGroupCreateRequest("CG-DUP", "Nhóm 2", false, 5m, null);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateCustomerGroupAsync(1, req2));
            Assert.Equal("Mã nhóm đã tồn tại", ex.Message);
        }
    }

    [Fact]
    public async Task CreateCustomerGroup_InvalidDiscountRate_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var req1 = new CustomerGroupCreateRequest("CG-DISC1", "Nhóm A", false, 105m, null);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateCustomerGroupAsync(1, req1));

            var req2 = new CustomerGroupCreateRequest("CG-DISC2", "Nhóm B", false, -1m, null);
            await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateCustomerGroupAsync(1, req2));
        }
    }

    [Fact]
    public async Task CreateCustomerGroup_ParentTypeMismatch_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var parent = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("SUP-P", "Nhóm NCC Cha", true, 0m, null));

            var req = new CustomerGroupCreateRequest("CUST-C", "Nhóm Khách Con", false, 5m, parent.Id);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateCustomerGroupAsync(1, req));
            Assert.Equal("Nhóm cha phải cùng loại (khách hàng hoặc nhà cung cấp)", ex.Message);
        }
    }

    [Fact]
    public async Task UpdateCustomerGroup_SelfParent_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var group = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CG-SELF", "Nhóm Self", false, 0m, null));

            var updateReq = new CustomerGroupUpdateRequest("Nhóm Self", 0m, group.Id, false);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateCustomerGroupAsync(1, group.Id, updateReq));
            Assert.Equal("Nhóm không thể là cha của chính mình", ex.Message);
        }
    }

    [Fact]
    public async Task UpdateCustomerGroup_Cycle3Levels_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var a = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CG-A", "Nhóm A", false, 0m, null));
            var b = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CG-B", "Nhóm B", false, 0m, a.Id));
            var c = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CG-C", "Nhóm C", false, 0m, b.Id));

            var updateReq = new CustomerGroupUpdateRequest("Nhóm A", 0m, c.Id, false);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateCustomerGroupAsync(1, a.Id, updateReq));
            Assert.Equal("Phát hiện vòng lặp phân cấp nhóm", ex.Message);
        }
    }

    [Fact]
    public async Task CreateCustomerGroup_ExceedMaxDepth_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var l1 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("L1", "Level 1", false, 0m, null));
            var l2 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("L2", "Level 2", false, 0m, l1.Id));
            var l3 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("L3", "Level 3", false, 0m, l2.Id));
            var l4 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("L4", "Level 4", false, 0m, l3.Id));
            var l5 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("L5", "Level 5", false, 0m, l4.Id));

            var req = new CustomerGroupCreateRequest("L6", "Level 6", false, 0m, l5.Id);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateCustomerGroupAsync(1, req));
            Assert.Equal("Phân cấp nhóm vượt quá giới hạn độ sâu cho phép (tối đa 5 tầng)", ex.Message);
        }
    }

    [Fact]
    public async Task DeleteCustomerGroup_EmptyGroup_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var group = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CG-DEL", "Nhóm Xóa", false, 0m, null));

            var success = await service.DeleteCustomerGroupAsync(1, group.Id);
            Assert.True(success);

            var list = await service.GetCustomerGroupsAsync(null, null, 1, 10, includeInactive: false);
            Assert.DoesNotContain(list.Items, x => x.Id == group.Id);
        }
    }

    [Fact]
    public async Task DeleteCustomerGroup_WithChildGroups_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var parent = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CG-PAR", "Nhóm Cha", false, 0m, null));
            await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CG-CHI", "Nhóm Con", false, 0m, parent.Id));

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.DeleteCustomerGroupAsync(1, parent.Id));
            Assert.Equal("Không thể xóa nhóm khi vẫn còn nhóm con hoạt động", ex.Message);
        }
    }

    [Fact]
    public async Task DeleteCustomerGroup_WithActiveMembers_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var cgService = new CustomerGroupService(context);
            var supplierService = new SupplierService(context);

            var group = await cgService.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CG-MEM", "Nhóm Mem", true, 0m, null));
            await supplierService.CreateSupplierAsync(1, new SupplierCreateRequest(null, "NCC Member", null, null, null, group.Id));

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => cgService.DeleteCustomerGroupAsync(1, group.Id));
            Assert.Equal("Không thể xóa nhóm khi vẫn còn thành viên hoạt động", ex.Message);
        }
    }

    [Fact]
    public async Task UpdateCustomerGroup_ChangeIsSupplierWithChildren_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var parent = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CG-P", "Nhóm P", false, 0m, null));
            await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CG-CH", "Nhóm CH", false, 0m, parent.Id));

            var updateReq = new CustomerGroupUpdateRequest("Nhóm P", 0m, null, true);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateCustomerGroupAsync(1, parent.Id, updateReq));
            Assert.Equal("Không thể đổi loại nhóm khi nhóm đã có nhóm con hoặc thành viên hoạt động", ex.Message);
        }
    }

    [Fact]
    public async Task UpdateCustomerGroup_ChangeIsSupplierWithMembers_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var cgService = new CustomerGroupService(context);
            var supplierService = new SupplierService(context);

            var group = await cgService.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CG-M", "Nhóm M", true, 0m, null));
            await supplierService.CreateSupplierAsync(1, new SupplierCreateRequest(null, "NCC M", null, null, null, group.Id));

            var updateReq = new CustomerGroupUpdateRequest("Nhóm M", 0m, null, false);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => cgService.UpdateCustomerGroupAsync(1, group.Id, updateReq));
            Assert.Equal("Không thể đổi loại nhóm khi nhóm đã có nhóm con hoặc thành viên hoạt động", ex.Message);
        }
    }

    [Fact]
    public async Task CreateSupplier_AssignToCustomerGroup_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var cgService = new CustomerGroupService(context);
            var supplierService = new SupplierService(context);

            var custGroup = await cgService.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CUST-G", "Nhóm Khách", false, 0m, null));

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                supplierService.CreateSupplierAsync(1, new SupplierCreateRequest(null, "NCC Assign Fail", null, null, null, custGroup.Id)));
            Assert.Equal("Nhóm nhà cung cấp không tồn tại hoặc không hợp lệ", ex.Message);
        }
    }

    [Fact]
    public async Task UpdateSupplier_AssignToCustomerGroup_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var cgService = new CustomerGroupService(context);
            var supplierService = new SupplierService(context);

            var custGroup = await cgService.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CUST-G", "Nhóm Khách", false, 0m, null));
            var supplier = await supplierService.CreateSupplierAsync(1, new SupplierCreateRequest(null, "NCC Test", null, null, null, null));

            var updateReq = new SupplierUpdateRequest("NCC Test", null, null, null, custGroup.Id);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => supplierService.UpdateSupplierAsync(1, supplier.Id, updateReq));
            Assert.Equal("Nhóm nhà cung cấp không tồn tại hoặc không hợp lệ", ex.Message);
        }
    }

    [Fact]
    public async Task CreateCustomerGroup_DuplicateCodeCase_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("cg-case", "Nhóm Case", false, 0m, null));

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CG-CASE", "Nhóm Case 2", false, 0m, null)));
            Assert.Equal("Mã nhóm đã tồn tại", ex.Message);
        }
    }

    [Fact]
    public async Task CreateCustomerGroup_DbUniqueViolation_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            context.CustomerGroups.Add(new CustomerGroup { Code = "CG-DB", Name = "DB Unique", IsSupplier = false, IsActive = true });
            await context.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() =>
                service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("CG-DB", "Another Name", false, 0m, null)));
            Assert.Equal("Mã nhóm đã tồn tại", ex.Message);
        }
    }

    [Fact]
    public async Task GetCustomerGroupById_NonExistent_ReturnsNull()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var result = await service.GetCustomerGroupByIdAsync(999999);
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task UpdateCustomerGroup_ExceedMaxDepthWithSubtree_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var l1 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("D1", "Deep 1", false, 0m, null));
            var l2 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("D2", "Deep 2", false, 0m, l1.Id));
            var l3 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("D3", "Deep 3", false, 0m, l2.Id));
            var l4 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("D4", "Deep 4", false, 0m, l3.Id));

            var subRoot = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("S1", "Sub 1", false, 0m, null));
            await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("S2", "Sub 2", false, 0m, subRoot.Id));

            var updateReq = new CustomerGroupUpdateRequest("Sub 1", 0m, l4.Id, false);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.UpdateCustomerGroupAsync(1, subRoot.Id, updateReq));
            Assert.Equal("Phân cấp nhóm vượt quá giới hạn độ sâu cho phép (tối đa 5 tầng)", ex.Message);
        }
    }

    [Fact]
    public async Task UpdateCustomerGroup_ValidMove_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var l1 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("VM1", "VM 1", false, 0m, null));
            var l2 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("VM2", "VM 2", false, 0m, null));

            var updateReq = new CustomerGroupUpdateRequest("VM 2", 0m, l1.Id, false);
            var updated = await service.UpdateCustomerGroupAsync(1, l2.Id, updateReq);
            Assert.NotNull(updated);
            Assert.Equal(l1.Id, updated.ParentId);
        }
    }

    [Fact]
    public async Task CreateCustomerGroup_CodeExceedsMaxLength_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var longCode = new string('A', 51);
            var req = new CustomerGroupCreateRequest(longCode, "Name", false, 0m, null);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateCustomerGroupAsync(1, req));
            Assert.Equal("Mã nhóm tối đa 50 ký tự", ex.Message);
        }
    }

    [Fact]
    public async Task CreateCustomerGroup_NameExceedsMaxLength_Throws400()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var longName = new string('A', 201);
            var req = new CustomerGroupCreateRequest("CG-LEN", longName, false, 0m, null);
            var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateCustomerGroupAsync(1, req));
            Assert.Equal("Tên nhóm tối đa 200 ký tự", ex.Message);
        }
    }

    [Fact]
    public async Task CreateCustomerGroup_AtMaxDepth5_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var l1 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("B1", "Boundary 1", false, 0m, null));
            var l2 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("B2", "Boundary 2", false, 0m, l1.Id));
            var l3 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("B3", "Boundary 3", false, 0m, l2.Id));
            var l4 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("B4", "Boundary 4", false, 0m, l3.Id));
            var l5 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("B5", "Boundary 5", false, 0m, l4.Id));

            Assert.NotNull(l5);
        }
    }

    [Fact]
    public async Task UpdateCustomerGroup_MoveSubtreeToExactDepth5_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var service = new CustomerGroupService(context);
            var l1 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("T1", "Target 1", false, 0m, null));
            var l2 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("T2", "Target 2", false, 0m, l1.Id));
            var l3 = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("T3", "Target 3", false, 0m, l2.Id));

            var subRoot = await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("SR1", "Sub Root", false, 0m, null));
            await service.CreateCustomerGroupAsync(1, new CustomerGroupCreateRequest("SC1", "Sub Child", false, 0m, subRoot.Id));

            var updateReq = new CustomerGroupUpdateRequest("Sub Root", 0m, l3.Id, false);
            var updated = await service.UpdateCustomerGroupAsync(1, subRoot.Id, updateReq);
            Assert.NotNull(updated);
            Assert.Equal(l3.Id, updated.ParentId);
        }
    }
}
