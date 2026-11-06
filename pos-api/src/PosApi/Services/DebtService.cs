using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;

namespace PosApi.Services;

/// <summary>
/// P0-4. Nghiệp vụ thu công nợ, tách khỏi controller để test được mà không cần raw SQL.
/// Core state-machine <see cref="ApplyPayment"/> là pure (nhận Debt, trả trạng thái mới),
/// chỉ phụ thuộc entity — unit-test phủ đầy đủ các nhánh tiền.
/// </summary>
public class DebtService
{
    private readonly AppDbContext _db;

    public DebtService(AppDbContext db) => _db = db;

    /// <summary>
    /// Core: áp một lần trả tiền lên công nợ. KHÔNG tự SaveChanges — gọi chung transaction.
    /// Trạng thái: Open → PartiallyPaid (trả một phần) / Settled (trả đủ).
    /// </summary>
    /// <exception cref="BusinessRuleException">Đã Settled, hoặc tiền trả vượt số còn nợ.</exception>
    public (Debt debt, decimal newRemaining) ApplyPayment(Debt debt, decimal amount, string? note, int userId)
    {
        if (amount <= 0)
            throw new BusinessRuleException("Số tiền phải lớn hơn 0");

        if (debt.Status == DebtStatus.Settled)
            throw new BusinessRuleException("Công nợ này đã thanh toán xong");

        var remaining = debt.Amount - debt.PaidAmount;
        if (amount > remaining + 0.0001m)
            throw new BusinessRuleException($"Tiền trả {amount:N0}đ vượt số còn nợ {remaining:N0}đ");

        debt.PaidAmount += amount;
        debt.Status = debt.PaidAmount >= debt.Amount ? DebtStatus.Settled : DebtStatus.PartiallyPaid;

        if (string.IsNullOrWhiteSpace(note))
            debt.Note = string.IsNullOrEmpty(debt.Note) ? $"Trả nợ {DateTime.UtcNow:dd/MM/yyyy}" : debt.Note;
        else
            debt.Note = string.IsNullOrEmpty(debt.Note) ? note : $"{debt.Note} | {note}";

        var newRemaining = Math.Max(0m, debt.Amount - debt.PaidAmount);

        PosLogService.Write(_db, "DEBT_PAID", userId, "debt", debt.Id.ToString(),
            new { paid = amount, remaining = newRemaining, status = debt.Status.ToString() });

        return (debt, newRemaining);
    }

    public async Task<PayDebtResponse> PayDebtAsync(int userId, int debtId, decimal amount, string? note)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            var existingTx = _db.Database.CurrentTransaction;
            await using var tx = existingTx == null ? await _db.Database.BeginTransactionAsync() : null;

            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            int? lockedId = null;
            using (var cmd = conn.CreateCommand() as Npgsql.NpgsqlCommand)
            {
                cmd!.CommandText = "SELECT \"Id\" FROM \"Debts\" WHERE \"Id\" = @id FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is Npgsql.NpgsqlTransaction npgTx)
                {
                    cmd.Transaction = npgTx;
                }
                cmd.Parameters.Add(new Npgsql.NpgsqlParameter("@id", debtId));
                lockedId = (await cmd.ExecuteScalarAsync()) as int?;
            }

            if (lockedId is null)
                throw new KeyNotFoundException($"Công nợ {debtId} không tồn tại");

            var debt = await _db.Debts.FirstOrDefaultAsync(x => x.Id == debtId)
                ?? throw new KeyNotFoundException($"Công nợ {debtId} không tồn tại");

            var (_, newRemaining) = ApplyPayment(debt, amount, note, userId);

            await _db.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();

            return new PayDebtResponse(debt.Id, debt.Amount, debt.PaidAmount, newRemaining, debt.Status.ToString());
        });
    }
}
