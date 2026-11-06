using PosApi.Dto;

namespace PosApi.Services;

public interface ISupplierService
{
    Task<PagedSupplierResponse> GetSuppliersAsync(string? q, int page, int pageSize, bool includeInactive = false);
    Task<SupplierResponse?> GetSupplierByIdAsync(int id);
    Task<SupplierResponse> CreateSupplierAsync(int userId, SupplierCreateRequest req);
    Task<SupplierResponse> UpdateSupplierAsync(int userId, int id, SupplierUpdateRequest req);
    Task<bool> DeleteSupplierAsync(int userId, int id);
}
