using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface ICashVoucherService
{
    Task<PagedCashVoucherResponse> GetVouchersAsync(CashVoucherType? type, PartnerType? partnerType, int? partnerId, CashVoucherStatus? status, int page, int pageSize);
    Task<CashVoucherResponse?> GetVoucherByIdAsync(int id);
    Task<CashVoucherResponse> CreateVoucherAsync(int userId, CashVoucherCreateRequest req);
    Task<CashVoucherResponse> ConfirmVoucherAsync(int userId, int id);
    Task<CashVoucherResponse> CancelVoucherAsync(int userId, int id);
}

public class CashVoucherService : ICashVoucherService
{
    private readonly AppDbContext _db;

    public CashVoucherService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedCashVoucherResponse> GetVouchersAsync(CashVoucherType? type, PartnerType? partnerType, int? partnerId, CashVoucherStatus? status, int page, int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.CashVouchers.AsNoTracking().AsQueryable();

        if (type.HasValue) query = query.Where(x => x.Type == type.Value);
        if (partnerType.HasValue) query = query.Where(x => x.PartnerType == partnerType.Value);
        if (partnerId.HasValue) query = query.Where(x => x.PartnerId == partnerId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        // Resolve partner names
        var customerIds = items.Where(x => x.PartnerType == PartnerType.Customer && x.PartnerId.HasValue).Select(x => x.PartnerId!.Value).Distinct().ToList();
        var supplierIds = items.Where(x => x.PartnerType == PartnerType.Supplier && x.PartnerId.HasValue).Select(x => x.PartnerId!.Value).Distinct().ToList();

        var customers = await _db.Customers.Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name);
        var suppliers = await _db.Suppliers.Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name);

        var responses = items.Select(v =>
        {
            string? partnerName = null;
            if (v.PartnerType == PartnerType.Customer && v.PartnerId.HasValue)
                partnerName = customers.GetValueOrDefault(v.PartnerId.Value);
            else if (v.PartnerType == PartnerType.Supplier && v.PartnerId.HasValue)
                partnerName = suppliers.GetValueOrDefault(v.PartnerId.Value);

            return MapToResponse(v, partnerName);
        }).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        return new PagedCashVoucherResponse(page, pageSize, total, totalPages, responses);
    }

    public async Task<CashVoucherResponse?> GetVoucherByIdAsync(int id)
    {
        var v = await _db.CashVouchers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (v is null) return null;

        string? partnerName = null;
        if (v.PartnerType == PartnerType.Customer && v.PartnerId.HasValue)
            partnerName = await _db.Customers.Where(c => c.Id == v.PartnerId.Value).Select(c => c.Name).FirstOrDefaultAsync();
        else if (v.PartnerType == PartnerType.Supplier && v.PartnerId.HasValue)
            partnerName = await _db.Suppliers.Where(s => s.Id == v.PartnerId.Value).Select(s => s.Name).FirstOrDefaultAsync();

        return MapToResponse(v, partnerName);
    }

    public async Task<CashVoucherResponse> CreateVoucherAsync(int userId, CashVoucherCreateRequest req)
    {
        if (req.Amount <= 0)
            throw new BusinessRuleException("Số tiền giao dịch phải lớn hơn 0");

        var prefix = req.Type == CashVoucherType.Receipt ? "PT" : "PC";
        var code = $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N").Substring(0, 4).ToUpper()}";

        var voucher = new CashVoucher
        {
            Code = code,
            Type = req.Type,
            PartnerType = req.PartnerType,
            PartnerId = req.PartnerId,
            Amount = req.Amount,
            PaymentMethod = req.PaymentMethod,
            ReferenceType = req.ReferenceType?.Trim(),
            ReferenceId = req.ReferenceId?.Trim(),
            ShiftId = req.ShiftId,
            Note = req.Note?.Trim(),
            Status = CashVoucherStatus.Draft,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow
        };

        _db.CashVouchers.Add(voucher);
        PosLogService.Write(_db, "CASH_VOUCHER_CREATED", userId, "cash_voucher", code);
        await _db.SaveChangesAsync();

        return MapToResponse(voucher, null);
    }

    public async Task<CashVoucherResponse> ConfirmVoucherAsync(int userId, int id)
    {
        var voucher = await _db.CashVouchers.FirstOrDefaultAsync(x => x.Id == id);
        if (voucher is null)
            throw new KeyNotFoundException("Không tìm thấy phiếu thu/chi");

        if (voucher.Status != CashVoucherStatus.Draft)
            throw new BusinessRuleException("Phiếu thu/chi không ở trạng thái nháp để có thể duyệt");

        var lastEntry = await _db.CashLedger.OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        var currentBalance = lastEntry?.Balance ?? 0m;

        var delta = voucher.Type == CashVoucherType.Receipt ? voucher.Amount : -voucher.Amount;
        var newBalance = currentBalance + delta;

        _db.CashLedger.Add(new CashLedgerEntry
        {
            Amount = delta,
            Balance = newBalance,
            PaymentMethod = voucher.PaymentMethod,
            RefType = "CashVoucher",
            RefId = voucher.Code,
            Note = voucher.Note ?? (voucher.Type == CashVoucherType.Receipt ? $"Thu tiền {voucher.Code}" : $"Chi tiền {voucher.Code}"),
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow
        });

        voucher.Status = CashVoucherStatus.Confirmed;
        voucher.ConfirmedAt = DateTime.UtcNow;

        PosLogService.Write(_db, "CASH_VOUCHER_CONFIRMED", userId, "cash_voucher", voucher.Code);
        await _db.SaveChangesAsync();

        string? partnerName = null;
        if (voucher.PartnerType == PartnerType.Customer && voucher.PartnerId.HasValue)
            partnerName = await _db.Customers.Where(c => c.Id == voucher.PartnerId.Value).Select(c => c.Name).FirstOrDefaultAsync();
        else if (voucher.PartnerType == PartnerType.Supplier && voucher.PartnerId.HasValue)
            partnerName = await _db.Suppliers.Where(s => s.Id == voucher.PartnerId.Value).Select(s => s.Name).FirstOrDefaultAsync();

        return MapToResponse(voucher, partnerName);
    }

    public async Task<CashVoucherResponse> CancelVoucherAsync(int userId, int id)
    {
        var voucher = await _db.CashVouchers.FirstOrDefaultAsync(x => x.Id == id);
        if (voucher is null)
            throw new KeyNotFoundException("Không tìm thấy phiếu thu/chi");

        if (voucher.Status != CashVoucherStatus.Draft)
            throw new BusinessRuleException("Chỉ có thể hủy phiếu thu/chi ở trạng thái nháp");

        voucher.Status = CashVoucherStatus.Cancelled;
        PosLogService.Write(_db, "CASH_VOUCHER_CANCELLED", userId, "cash_voucher", voucher.Code);
        await _db.SaveChangesAsync();

        return MapToResponse(voucher, null);
    }

    private static CashVoucherResponse MapToResponse(CashVoucher v, string? partnerName) => new(
        v.Id,
        v.Code,
        v.Type,
        v.PartnerType,
        v.PartnerId,
        partnerName,
        v.Amount,
        v.PaymentMethod,
        v.ReferenceType,
        v.ReferenceId,
        v.ShiftId,
        v.Note,
        v.Status,
        v.CreatedBy,
        v.CreatedAt,
        v.ConfirmedAt
    );
}
