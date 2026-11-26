using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using Npgsql;

namespace PosApi.Services;

public class BankReconciliationService
{
    private readonly AppDbContext _context;
    private readonly ILogger<BankReconciliationService> _log;
    private readonly INotificationService? _notificationService;
    private static readonly Regex OrderCodeRegex = new(@"INV[\-\s\.]?(\d{8})[\-\s\.]?(\d{4})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public BankReconciliationService(AppDbContext context, ILogger<BankReconciliationService> log, INotificationService? notificationService = null)
    {
        _context = context;
        _log = log;
        _notificationService = notificationService;
    }

    public async Task ProcessAsync(IEnumerable<IncomingBankTransaction> incomingTransactions)
    {
        foreach (var tx in incomingTransactions)
        {
            try
            {
                await ProcessSingleAsync(tx);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error processing incoming bank transaction ExternalId={ExternalId}", tx.ExternalId);
            }
        }
    }

    private async Task ProcessSingleAsync(IncomingBankTransaction tx)
    {
        if (tx.Amount <= 0) return;

        var setting = await _context.StoreSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);
        var configuredAccountNo = setting?.VietQrAccountNo;
        if (!string.IsNullOrWhiteSpace(configuredAccountNo))
        {
            if (!string.Equals(tx.AccountNumber?.Trim(), configuredAccountNo.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
        else
        {
            _log.LogWarning("StoreSetting.VietQrAccountNo is empty. Skipping account number filtering for transaction ExternalId={ExternalId}", tx.ExternalId);
        }

        var existing = await _context.BankTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Provider == "SePay" && b.ExternalId == tx.ExternalId);

        if (existing != null)
        {
            return;
        }

        string? orderCode = ExtractOrderCode(tx.Content) ?? ExtractOrderCode(tx.ReferenceCode);

        var existingTx = _context.Database.CurrentTransaction;
        await using var dbTx = existingTx == null && _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync() : null;

        try
        {
            var duplicate = await _context.BankTransactions
                .FirstOrDefaultAsync(b => b.Provider == "SePay" && b.ExternalId == tx.ExternalId);
            if (duplicate != null)
            {
                if (dbTx != null) await dbTx.CommitAsync();
                return;
            }

            var bankTx = new BankTransaction
            {
                Provider = "SePay",
                ExternalId = tx.ExternalId,
                AccountNumber = tx.AccountNumber ?? "",
                Amount = tx.Amount,
                Content = tx.Content,
                ReferenceCode = tx.ReferenceCode,
                TransactionDate = tx.TransactionDateUtc,
                CreatedAt = DateTime.UtcNow
            };

            if (string.IsNullOrWhiteSpace(orderCode))
            {
                bankTx.Status = BankTransactionStatus.Unmatched;
                bankTx.UnmatchedReason = UnmatchedReason.NoOrderCode;
                _context.BankTransactions.Add(bankTx);
                await _context.SaveChangesAsync();
                if (dbTx != null) await dbTx.CommitAsync();
                return;
            }

            var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            using var cmd = conn.CreateCommand() as NpgsqlCommand;
            cmd!.CommandText = "SELECT \"Id\", \"Status\", \"Total\", \"PaymentMethod\" FROM \"Orders\" WHERE \"Code\" = @code FOR UPDATE";
            if (_context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx)
            {
                cmd.Transaction = npgTx;
            }
            cmd.Parameters.Add(new NpgsqlParameter("@code", orderCode));

            int? orderId = null;
            OrderStatus? orderStatus = null;
            decimal orderTotal = 0m;
            PaymentMethod? paymentMethod = null;

            using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    orderId = reader.GetInt32(0);
                    orderStatus = (OrderStatus)reader.GetInt32(1);
                    orderTotal = reader.GetDecimal(2);
                    paymentMethod = (PaymentMethod)reader.GetInt32(3);
                }
            }

            if (orderId == null)
            {
                bankTx.Status = BankTransactionStatus.Unmatched;
                bankTx.UnmatchedReason = UnmatchedReason.OrderNotFound;
                _context.BankTransactions.Add(bankTx);
                await _context.SaveChangesAsync();
                if (dbTx != null) await dbTx.CommitAsync();
                return;
            }

            if (orderStatus != OrderStatus.Pending || (paymentMethod != PaymentMethod.VietQr && paymentMethod != PaymentMethod.BankTransfer))
            {
                bankTx.Status = BankTransactionStatus.Unmatched;
                bankTx.UnmatchedReason = UnmatchedReason.OrderNotPending;
                bankTx.OrderId = orderId;
                _context.BankTransactions.Add(bankTx);
                await _context.SaveChangesAsync();
                if (dbTx != null) await dbTx.CommitAsync();
                return;
            }

            if (tx.Amount != orderTotal)
            {
                bankTx.Status = BankTransactionStatus.Unmatched;
                bankTx.UnmatchedReason = UnmatchedReason.AmountMismatch;
                bankTx.OrderId = orderId;
                _context.BankTransactions.Add(bankTx);
                await _context.SaveChangesAsync();
                if (dbTx != null) await dbTx.CommitAsync();
                return;
            }

            Payment? payment = null;
            var order = await _context.Orders
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == orderId.Value);

            if (order != null)
            {
                payment = await CompleteOrderAsync(_context, order, tx.Amount, tx.ReferenceCode ?? tx.ExternalId);
            }

            bankTx.Status = BankTransactionStatus.Matched;
            bankTx.OrderId = orderId;
            bankTx.MatchedAt = DateTime.UtcNow;
            _context.BankTransactions.Add(bankTx);

            // Record PosLog for auto-match (userId is null)
            PosLogService.Write(_context, "BANK_TX_MATCHED", null, "bank_transaction", bankTx.ExternalId, new { orderId, amount = tx.Amount });

            await _context.SaveChangesAsync();
            if (dbTx != null) await dbTx.CommitAsync();

            try
            {
                if (_notificationService != null && payment != null && order != null)
                {
                    await _notificationService.NotifyPaymentConfirmedAsync(payment.Id, order.Id, order.Code, payment.Amount);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to dispatch PaymentConfirmed notification for order {OrderId}", order?.Id);
            }
        }
        catch
        {
            if (dbTx != null) await dbTx.RollbackAsync();
            throw;
        }
    }

    private static async Task<Payment> CompleteOrderAsync(AppDbContext context, Order order, decimal paidAmount, string reference)
    {
        order.Status = OrderStatus.Completed;
        order.CompletedAt = DateTime.UtcNow;
        order.PaidAmount = paidAmount;

        await ShiftService.AssignShiftOnCompletionAsync(context, order);

        System.Data.Common.DbConnection? conn = null;
        NpgsqlTransaction? npgTx = null;
        if (context.Database.IsRelational())
        {
            conn = context.Database.GetDbConnection();
            npgTx = context.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
        }
        await OrderService.EarnLoyaltyForCompletedOrderAsync(context, order, conn, npgTx);

        var payment = order.Payments.FirstOrDefault(p => (p.Method == PaymentMethod.VietQr || p.Method == PaymentMethod.BankTransfer) && p.Status == PaymentStatus.Pending);
        if (payment != null)
        {
            payment.Status = PaymentStatus.Paid;
            payment.PaidAt = DateTime.UtcNow;
            payment.Reference = reference;
        }
        else
        {
            payment = new Payment
            {
                OrderId = order.Id,
                Method = order.PaymentMethod,
                Amount = paidAmount,
                Reference = reference,
                Status = PaymentStatus.Paid,
                PaidAt = DateTime.UtcNow
            };
            order.Payments.Add(payment);
        }
        return payment;
    }

    public async Task<BankTransactionResponse> ManualMatchAsync(int bankTransactionId, int orderId, string? note, int userId)
    {
        var existingTx = _context.Database.CurrentTransaction;
        await using var dbTx = existingTx == null && _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync() : null;

        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            // 1. Lock BankTransactions row FOR UPDATE
            using (var cmdTx = conn.CreateCommand() as NpgsqlCommand)
            {
                cmdTx!.CommandText = "SELECT \"Id\" FROM \"BankTransactions\" WHERE \"Id\" = @txId FOR UPDATE";
                if (_context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx1)
                {
                    cmdTx.Transaction = npgTx1;
                }
                cmdTx.Parameters.Add(new NpgsqlParameter("@txId", bankTransactionId));
                await cmdTx.ExecuteNonQueryAsync();
            }

            // 2. Lock Orders row FOR UPDATE
            using (var cmdOrd = conn.CreateCommand() as NpgsqlCommand)
            {
                cmdOrd!.CommandText = "SELECT \"Id\" FROM \"Orders\" WHERE \"Id\" = @ordId FOR UPDATE";
                if (_context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx2)
                {
                    cmdOrd.Transaction = npgTx2;
                }
                cmdOrd.Parameters.Add(new NpgsqlParameter("@ordId", orderId));
                await cmdOrd.ExecuteNonQueryAsync();
            }

            var tx = await _context.BankTransactions
                .FirstOrDefaultAsync(b => b.Id == bankTransactionId);

            if (tx == null)
                throw new KeyNotFoundException($"Không tìm thấy giao dịch ngân hàng #{bankTransactionId}");

            if (tx.Status != BankTransactionStatus.Unmatched)
                throw new BusinessRuleException("Chỉ có thể khớp thủ công giao dịch ở trạng thái Chưa khớp (Unmatched)");

            var order = await _context.Orders
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == orderId);

            if (order == null)
                throw new KeyNotFoundException($"Không tìm thấy đơn hàng #{orderId}");

            if (order.Status != OrderStatus.Pending || (order.PaymentMethod != PaymentMethod.VietQr && order.PaymentMethod != PaymentMethod.BankTransfer))
                throw new BusinessRuleException("Đơn hàng phải ở trạng thái chờ thanh toán (Pending) và thuộc hình thức chuyển khoản");

            if (tx.Amount != order.Total && string.IsNullOrWhiteSpace(note))
            {
                throw new BusinessRuleException("Giao dịch lệch số tiền so với đơn hàng bắt buộc phải có ghi chú (note)");
            }

            var payment = await CompleteOrderAsync(_context, order, tx.Amount, tx.ReferenceCode ?? tx.ExternalId);

            tx.Status = BankTransactionStatus.Resolved;
            tx.OrderId = order.Id;
            tx.ResolvedByUserId = userId;
            tx.ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            tx.MatchedAt = DateTime.UtcNow;

            PosLogService.Write(_context, "BANK_TX_RESOLVED", userId, "bank_transaction", tx.Id.ToString(), new { orderId = order.Id, amount = tx.Amount, note = tx.ResolutionNote });
            await _context.SaveChangesAsync();

            if (dbTx != null) await dbTx.CommitAsync();

            try
            {
                if (_notificationService != null)
                {
                    await _notificationService.NotifyPaymentConfirmedAsync(payment.Id, order.Id, order.Code, payment.Amount);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to dispatch PaymentConfirmed notification for order {OrderId}", order.Id);
            }

            return ToResponse(tx, order.Code);
        }
        catch
        {
            if (dbTx != null) await dbTx.RollbackAsync();
            throw;
        }
    }

    public async Task<BankTransactionResponse> ManualIgnoreAsync(int bankTransactionId, string note, int userId)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new BusinessRuleException("Bắt buộc phải có ghi chú (note) khi bỏ qua giao dịch");

        var existingTx = _context.Database.CurrentTransaction;
        await using var dbTx = existingTx == null && _context.Database.IsRelational() ? await _context.Database.BeginTransactionAsync() : null;

        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync();

            // Lock BankTransactions row FOR UPDATE
            using (var cmdTx = conn.CreateCommand() as NpgsqlCommand)
            {
                cmdTx!.CommandText = "SELECT \"Id\" FROM \"BankTransactions\" WHERE \"Id\" = @txId FOR UPDATE";
                if (_context.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx1)
                {
                    cmdTx.Transaction = npgTx1;
                }
                cmdTx.Parameters.Add(new NpgsqlParameter("@txId", bankTransactionId));
                await cmdTx.ExecuteNonQueryAsync();
            }

            var tx = await _context.BankTransactions
                .Include(b => b.Order)
                .FirstOrDefaultAsync(b => b.Id == bankTransactionId);

            if (tx == null)
                throw new KeyNotFoundException($"Không tìm thấy giao dịch ngân hàng #{bankTransactionId}");

            if (tx.Status != BankTransactionStatus.Unmatched)
                throw new BusinessRuleException("Chỉ có thể bỏ qua giao dịch ở trạng thái Chưa khớp");

            tx.Status = BankTransactionStatus.Ignored;
            tx.ResolvedByUserId = userId;
            tx.ResolutionNote = note.Trim();

            PosLogService.Write(_context, "BANK_TX_IGNORED", userId, "bank_transaction", tx.Id.ToString(), new { note = tx.ResolutionNote });
            await _context.SaveChangesAsync();

            if (dbTx != null) await dbTx.CommitAsync();

            return ToResponse(tx, tx.Order?.Code);
        }
        catch
        {
            if (dbTx != null) await dbTx.RollbackAsync();
            throw;
        }
    }

    public static string? ExtractOrderCode(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;
        var matches = OrderCodeRegex.Matches(content);
        string? foundCode = null;

        foreach (Match match in matches)
        {
            if (!match.Success) continue;
            var datePart = match.Groups[1].Value;
            var numPart = match.Groups[2].Value;

            if (!DateTime.TryParseExact(datePart, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
            {
                continue;
            }

            var normalized = $"INV-{datePart}-{numPart.PadLeft(4, '0')}";
            if (foundCode != null && foundCode != normalized)
            {
                return null;
            }
            foundCode = normalized;
        }

        return foundCode;
    }

    private static BankTransactionResponse ToResponse(BankTransaction x, string? orderCode) =>
        new(
            x.Id,
            x.Provider,
            x.ExternalId,
            x.AccountNumber,
            x.Amount,
            x.Content,
            x.ReferenceCode,
            x.TransactionDate,
            x.Status.ToString(),
            x.UnmatchedReason.HasValue ? x.UnmatchedReason.Value.ToString() : null,
            x.OrderId,
            orderCode,
            x.MatchedAt,
            x.ResolvedByUserId,
            x.ResolutionNote,
            x.CreatedAt
        );
}
