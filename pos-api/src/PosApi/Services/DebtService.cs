using PosApi.Data;
using PosApi.Domain;
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
}
