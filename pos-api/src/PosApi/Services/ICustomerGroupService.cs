using PosApi.Dto;

namespace PosApi.Services;

public interface ICustomerGroupService
{
    Task<PagedCustomerGroupResponse> GetCustomerGroupsAsync(string? q, bool? isSupplier, int page, int pageSize, bool includeInactive = false);
    Task<CustomerGroupResponse?> GetCustomerGroupByIdAsync(int id);
    Task<CustomerGroupResponse> CreateCustomerGroupAsync(int userId, CustomerGroupCreateRequest req);
    Task<CustomerGroupResponse> UpdateCustomerGroupAsync(int userId, int id, CustomerGroupUpdateRequest req);
    Task<bool> DeleteCustomerGroupAsync(int userId, int id);
}
