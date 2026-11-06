using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface ISupplierDebtService
{
    Task<PagedSupplierDebtResponse> GetSupplierDebtsAsync(int? supplierId, DebtStatus? status, int page, int pageSize);
    Task<PaySupplierDebtResponse> PaySupplierDebtAsync(int userId, int supplierDebtId, decimal amount, string? note);
}

public class SupplierDebtService : ISupplierDebtService
{
    private readonly AppDbContext _db;

    public SupplierDebtService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedSupplierDebtResponse> GetSupplierDebtsAsync(int? supplierId, DebtStatus? status, int page, int pageSize)
    {
        if (pageSize is < 1 or > 100) pageSize = 20;
        if (page < 1) page = 1;

        var query = _db.SupplierDebts
            .AsNoTracking()
            .Include(x => x.Supplier)
            .AsQueryable();

        if (supplierId.HasValue) query = query.Where(x => x.SupplierId == supplierId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        var total = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var responses = items.Select(d => new SupplierDebtResponse(
            d.Id,
            d.SupplierId,
            d.Supplier?.Name ?? string.Empty,
            d.Amount,
            d.PaidAmount,
            Math.Max(0m, d.Amount - d.PaidAmount),
            d.Status.ToString(),
            d.ReferenceType,
            d.ReferenceId,
            d.Note,
            d.CreatedAt
        )).ToList();

        var totalPages = (int)Math.Ceiling(total / (double)pageSize);
        if (totalPages < 1) totalPages = 1;

        return new PagedSupplierDebtResponse(page, pageSize, total, totalPages, responses);
    }

    public async Task<PaySupplierDebtResponse> PaySupplierDebtAsync(int userId, int supplierDebtId, decimal amount, string? note)
    {
        if (amount <= 0)
            throw new BusinessRuleException("Số tiền trả phải lớn hơn 0");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _db.Database.CurrentTransaction;
            await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

            var debt = await _db.SupplierDebts
                .Include(x => x.Supplier)
                .FirstOrDefaultAsync(x => x.Id == supplierDebtId);

            if (debt is null)
                throw new KeyNotFoundException($"Công nợ nhà cung cấp {supplierDebtId} không tồn tại");

            if (debt.Status == DebtStatus.Settled)
                throw new BusinessRuleException("Công nợ nhà cung cấp này đã thanh toán xong");

            var remaining = debt.Amount - debt.PaidAmount;
            if (amount > remaining + 0.0001m)
                throw new BusinessRuleException($"Tiền trả {amount:N0}đ vượt số còn nợ {remaining:N0}đ");

            debt.PaidAmount += amount;
            debt.Status = debt.PaidAmount >= debt.Amount ? DebtStatus.Settled : DebtStatus.PartiallyPaid;

            if (string.IsNullOrWhiteSpace(note))
                debt.Note = string.IsNullOrEmpty(debt.Note) ? $"Thanh toán công nợ {DateTime.UtcNow:dd/MM/yyyy}" : debt.Note;
            else
                debt.Note = string.IsNullOrEmpty(debt.Note) ? note : $"{debt.Note} | {note}";

            var newRemaining = Math.Max(0m, debt.Amount - debt.PaidAmount);

            PosLogService.Write(_db, "SUPPLIER_DEBT_PAID", userId, "supplier_debt", debt.Id.ToString(),
                new { supplierId = debt.SupplierId, paid = amount, remaining = newRemaining, status = debt.Status.ToString() });

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            return new PaySupplierDebtResponse(debt.Id, debt.Amount, debt.PaidAmount, newRemaining, debt.Status.ToString());
        });
    }
}
