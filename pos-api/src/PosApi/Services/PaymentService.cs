using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;

namespace PosApi.Services;

public interface IPaymentService
{
    Task<VietQrResponse> GenerateVietQrAsync(int orderId);
    Task<PaymentResponse> ConfirmPaymentAsync(int paymentId, int userId, PaymentStatus status, string? reference);
}

public class PaymentService : IPaymentService
{
    private readonly AppDbContext _context;
    private readonly INotificationService? _notificationService;
    private readonly ILogger<PaymentService> _log;

    public PaymentService(AppDbContext context, INotificationService? notificationService = null, ILogger<PaymentService>? log = null)
    {
        _context = context;
        _notificationService = notificationService;
        _log = log ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentService>.Instance;
    }

    public async Task<VietQrResponse> GenerateVietQrAsync(int orderId)
    {
        var order = await _context.Orders
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
            throw new KeyNotFoundException($"Không tìm thấy đơn hàng #{orderId}");

        var setting = await _context.StoreSettings.FirstOrDefaultAsync(s => s.Id == 1)
            ?? new StoreSetting { VietQrBankCode = "MB", VietQrAccountNo = "0987654321", VietQrAccountName = "DIGISME DEMO" };

        if (string.IsNullOrWhiteSpace(setting.VietQrBankCode) || string.IsNullOrWhiteSpace(setting.VietQrAccountNo))
            throw new BusinessRuleException("Chưa cấu hình tài khoản VietQR cho cửa hàng");

        var amount = order.Total;
        var addInfo = order.Code;
        var bankCode = setting.VietQrBankCode;
        var accountNo = setting.VietQrAccountNo;
        var accountName = Uri.EscapeDataString(setting.VietQrAccountName);

        var qrImageUrl = $"https://img.vietqr.io/image/{bankCode}-{accountNo}-compact2.png?amount={amount}&addInfo={addInfo}&accountName={accountName}";

        var payment = order.Payments.FirstOrDefault(p => p.Method == PaymentMethod.VietQr && p.Status == PaymentStatus.Pending);
        if (payment == null)
        {
            payment = new Payment
            {
                OrderId = order.Id,
                Method = PaymentMethod.VietQr,
                Amount = amount,
                Reference = addInfo,
                Status = PaymentStatus.Pending
            };
            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();
        }

        return new VietQrResponse(
            payment.Id,
            order.Id,
            order.Code,
            amount,
            bankCode,
            accountNo,
            setting.VietQrAccountName,
            addInfo,
            qrImageUrl,
            payment.Status.ToString()
        );
    }

    public async Task<PaymentResponse> ConfirmPaymentAsync(int paymentId, int userId, PaymentStatus status, string? reference)
    {
        var payment = await _context.Payments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == paymentId);

        if (payment == null)
            throw new KeyNotFoundException($"Không tìm thấy giao dịch thanh toán #{paymentId}");

        payment.Status = status;
        if (status == PaymentStatus.Paid)
        {
            payment.PaidAt = DateTime.UtcNow;
            if (payment.Order != null)
            {
                payment.Order.PaidAmount = payment.Amount;
                payment.Order.Status = OrderStatus.Completed;
            }
        }
        if (!string.IsNullOrWhiteSpace(reference))
        {
            payment.Reference = reference;
        }

        PosLogService.Write(_context, "PAYMENT_CONFIRMED", userId, "payment", paymentId.ToString(), new { payment.Status, payment.Amount });
        await _context.SaveChangesAsync();

        try
        {
            if (_notificationService != null && payment.Status == PaymentStatus.Paid)
            {
                await _notificationService.NotifyPaymentConfirmedAsync(payment.Id, payment.OrderId, payment.Order?.Code ?? "", payment.Amount);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to dispatch PaymentConfirmed notification for payment {PaymentId}", payment.Id);
        }

        return new PaymentResponse(
            payment.Id,
            payment.OrderId,
            payment.Method.ToString(),
            payment.Amount,
            payment.Reference,
            payment.Status.ToString(),
            payment.PaidAt,
            payment.CreatedAt
        );
    }
}
