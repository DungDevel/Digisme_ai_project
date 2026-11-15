using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using Npgsql;

namespace PosApi.Services;

public interface ISupplierDebtService
{
    Task<PagedSupplierDebtResponse> GetSupplierDebtsAsync(int? supplierId, DebtStatus? status, int page, int pageSize);
    Task<PaySupplierDebtResponse> PaySupplierDebtAsync(int userId, int supplierDebtId, decimal amount, string? note);
    Task AllocatePaymentAsync(int supplierId, decimal amount, int? specificStockInId, string? note, int userId);
    Task AllocateReturnAsync(int supplierId, decimal amount, string? referenceCode, int userId);
    Task<List<SupplierDebtAgingResponse>> GetAgingAsync(int? supplierId);
    Task<RecalculateSupplierDebtResponse> RecalculateAsync(int userId, bool preview);
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
            d.StockInId,
            d.DueDate,
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

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            int? lockedId = null;
            using (var cmd = conn.CreateCommand() as NpgsqlCommand)
            {
                cmd!.CommandText = "SELECT \"Id\" FROM \"SupplierDebts\" WHERE \"Id\" = @id FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx)
                {
                    cmd.Transaction = npgTx;
                }
                cmd.Parameters.Add(new NpgsqlParameter("@id", supplierDebtId));
                lockedId = (await cmd.ExecuteScalarAsync()) as int?;
            }

            if (lockedId is null)
                throw new KeyNotFoundException($"Công nợ nhà cung cấp {supplierDebtId} không tồn tại");

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

    public async Task AllocatePaymentAsync(int supplierId, decimal amount, int? specificStockInId, string? note, int userId)
    {
        if (amount <= 0) return;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _db.Database.CurrentTransaction;
            await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            // Lock supplier debts row-level FOR UPDATE
            using (var cmd = conn.CreateCommand() as NpgsqlCommand)
            {
                cmd!.CommandText = "SELECT \"Id\" FROM \"SupplierDebts\" WHERE \"SupplierId\" = @sid AND \"Status\" <> 2 ORDER BY \"Id\" FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx)
                {
                    cmd.Transaction = npgTx;
                }
                cmd.Parameters.Add(new NpgsqlParameter("@sid", supplierId));
                await cmd.ExecuteNonQueryAsync();
            }

            var query = _db.SupplierDebts
                .Where(x => x.SupplierId == supplierId && x.Status != DebtStatus.Settled && (x.Amount - x.PaidAmount > 0));

            if (specificStockInId.HasValue)
            {
                query = query.Where(x => x.StockInId == specificStockInId.Value || x.ReferenceId == specificStockInId.Value.ToString());
            }

            var openDebts = await query
                .OrderBy(x => x.DueDate ?? DateTime.MaxValue)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync();

            decimal remainingToPay = amount;

            foreach (var debt in openDebts)
            {
                if (remainingToPay <= 0) break;

                var debtRemaining = debt.Amount - debt.PaidAmount;
                var payAmount = Math.Min(remainingToPay, debtRemaining);

                debt.PaidAmount += payAmount;
                debt.Status = debt.PaidAmount >= debt.Amount ? DebtStatus.Settled : DebtStatus.PartiallyPaid;
                remainingToPay -= payAmount;

                var paymentNote = string.IsNullOrWhiteSpace(note) ? "Thanh toán phân bổ" : note;
                debt.Note = string.IsNullOrEmpty(debt.Note) ? paymentNote : $"{debt.Note} | {paymentNote}";

                PosLogService.Write(_db, "SUPPLIER_DEBT_PAID", userId, "supplier_debt", debt.Id.ToString(),
                    new { supplierId, paid = payAmount, remaining = debt.Amount - debt.PaidAmount, status = debt.Status.ToString() });
            }

            if (remainingToPay > 0 + 0.0001m)
            {
                throw new BusinessRuleException($"Số tiền thanh toán vượt quá tổng số dư công nợ của nhà cung cấp (dư thừa: {remainingToPay:N0}đ)");
            }

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
        });
    }

    public async Task AllocateReturnAsync(int supplierId, decimal amount, string? referenceCode, int userId)
    {
        if (amount <= 0) return;

        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _db.Database.CurrentTransaction;
            await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            using (var cmd = conn.CreateCommand() as NpgsqlCommand)
            {
                cmd!.CommandText = "SELECT \"Id\" FROM \"SupplierDebts\" WHERE \"SupplierId\" = @sid AND \"Status\" <> 2 ORDER BY \"Id\" FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx)
                {
                    cmd.Transaction = npgTx;
                }
                cmd.Parameters.Add(new NpgsqlParameter("@sid", supplierId));
                await cmd.ExecuteNonQueryAsync();
            }

            var openDebts = await _db.SupplierDebts
                .Where(x => x.SupplierId == supplierId && x.Status != DebtStatus.Settled && (x.Amount - x.PaidAmount > 0))
                .OrderBy(x => x.DueDate ?? DateTime.MaxValue)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync();

            decimal remainingToReduce = amount;

            foreach (var debt in openDebts)
            {
                if (remainingToReduce <= 0) break;

                var debtRemaining = debt.Amount - debt.PaidAmount;
                var reduceAmount = Math.Min(remainingToReduce, debtRemaining);

                debt.PaidAmount += reduceAmount;
                debt.Status = debt.PaidAmount >= debt.Amount ? DebtStatus.Settled : DebtStatus.PartiallyPaid;
                remainingToReduce -= reduceAmount;

                var retNote = $"Giảm nợ do trả hàng NCC ({referenceCode})";
                debt.Note = string.IsNullOrEmpty(debt.Note) ? retNote : $"{debt.Note} | {retNote}";

                PosLogService.Write(_db, "SUPPLIER_DEBT_RETURNED", userId, "supplier_debt", debt.Id.ToString(),
                    new { supplierId, reduced = reduceAmount, remaining = debt.Amount - debt.PaidAmount, status = debt.Status.ToString() });
            }

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
        });
    }

    public async Task<List<SupplierDebtAgingResponse>> GetAgingAsync(int? supplierId)
    {
        var query = _db.SupplierDebts
            .AsNoTracking()
            .Include(x => x.Supplier)
            .Where(x => x.Status != DebtStatus.Settled && (x.Amount - x.PaidAmount > 0));

        if (supplierId.HasValue)
        {
            query = query.Where(x => x.SupplierId == supplierId.Value);
        }

        var debts = await query.ToListAsync();
        var now = DateTime.UtcNow;

        var grouped = debts.GroupBy(d => d.SupplierId);
        var result = new List<SupplierDebtAgingResponse>();

        foreach (var g in grouped)
        {
            var supplier = g.First().Supplier;
            decimal totalRem = 0m;
            decimal under30 = 0m;
            decimal between30And60 = 0m;
            decimal over60 = 0m;
            decimal overdue = 0m;

            foreach (var d in g)
            {
                var rem = d.Amount - d.PaidAmount;
                totalRem += rem;
                var baseDate = d.DueDate ?? d.CreatedAt;
                int days = (int)(now - baseDate).TotalDays;

                if (days < 0) days = 0;

                if (days < 30) under30 += rem;
                else if (days <= 60) between30And60 += rem;
                else over60 += rem;

                if (d.DueDate.HasValue && d.DueDate.Value < now)
                {
                    overdue += rem;
                }
                else if (!d.DueDate.HasValue && (now - d.CreatedAt).TotalDays > 30)
                {
                    overdue += rem;
                }
            }

            result.Add(new SupplierDebtAgingResponse(
                g.Key,
                supplier?.Name ?? "Unknown",
                totalRem,
                new SupplierDebtAgingBucket(under30, between30And60, over60, overdue)
            ));
        }

        return result;
    }

    public async Task<RecalculateSupplierDebtResponse> RecalculateAsync(int userId, bool preview)
    {
        var suppliers = await _db.Suppliers.Where(s => s.IsActive).ToListAsync();
        var discrepancies = new List<SupplierDebtDiscrepancy>();

        foreach (var s in suppliers)
        {
            var recordedDebts = await _db.SupplierDebts
                .Where(x => x.SupplierId == s.Id)
                .ToListAsync();

            decimal recordedBalance = recordedDebts.Sum(x => x.Amount - x.PaidAmount);

            var stockIns = await _db.StockIns
                .Where(si => si.SupplierId == s.Id && si.Status == StockInStatus.Confirmed)
                .ToListAsync();
            decimal incurred = stockIns.Sum(si => si.Total - si.PaidAmount);

            var payments = await _db.CashVouchers
                .Where(cv => cv.PartnerType == PartnerType.Supplier && cv.PartnerId == s.Id && cv.Type == CashVoucherType.Payment && cv.Status == CashVoucherStatus.Confirmed)
                .ToListAsync();
            decimal paid = payments.Sum(cv => cv.Amount);

            var returns = await _db.SupplierReturns
                .Where(sr => sr.SupplierId == s.Id && sr.Status == SupplierReturnStatus.Completed)
                .ToListAsync();
            decimal returned = returns.Sum(sr => sr.Total);

            decimal calculatedBalance = Math.Max(0m, incurred - paid - returned);
            decimal diff = Math.Abs(recordedBalance - calculatedBalance);

            if (diff > 0.01m)
            {
                discrepancies.Add(new SupplierDebtDiscrepancy(s.Id, s.Name, recordedBalance, calculatedBalance, diff));
            }
        }

        if (!preview && discrepancies.Count > 0)
        {
            PosLogService.Write(_db, "SUPPLIER_DEBT_RECALCULATED", userId, "supplier_debt", "batch", new { discrepanciesCount = discrepancies.Count });
            await _db.SaveChangesAsync();
        }

        return new RecalculateSupplierDebtResponse(preview, suppliers.Count, discrepancies.Count, discrepancies);
    }
}
