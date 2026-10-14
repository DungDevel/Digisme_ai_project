using Microsoft.AspNetCore.SignalR;
using PosApi.Hubs;

namespace PosApi.Services;

public interface INotificationService
{
    Task NotifyOrderCreatedAsync(int orderId, string orderCode, decimal total);
    Task NotifyOrderCompletedAsync(int orderId, string orderCode, decimal total);
    Task NotifyPaymentConfirmedAsync(int paymentId, int orderId, string orderCode, decimal amount);
    Task NotifyLowStockAsync(int productId, string productName, string barcode, decimal stock, decimal minStock);
    Task NotifyShiftOpenedAsync(int shiftId, int userId, string username);
    Task NotifyShiftClosedAsync(int shiftId, int userId, string username, decimal countedCash, decimal diff);
}

public class NotificationService : INotificationService
{
    private readonly IHubContext<PosHub> _hubContext;
    private readonly ILogger<NotificationService> _log;

    public NotificationService(IHubContext<PosHub> hubContext, ILogger<NotificationService> log)
    {
        _hubContext = hubContext;
        _log = log;
    }

    public async Task NotifyOrderCreatedAsync(int orderId, string orderCode, decimal total)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveNotification", new
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = "OrderCreated",
                OccurredAtUtc = DateTime.UtcNow,
                EntityId = orderId.ToString(),
                Message = $"Đơn hàng mới {orderCode} (Tổng: {total:N0} đ)"
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send OrderCreated notification");
        }
    }

    public async Task NotifyOrderCompletedAsync(int orderId, string orderCode, decimal total)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveNotification", new
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = "OrderCompleted",
                OccurredAtUtc = DateTime.UtcNow,
                EntityId = orderId.ToString(),
                Message = $"Đơn hàng {orderCode} đã hoàn tất (Tổng: {total:N0} đ)"
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send OrderCompleted notification");
        }
    }

    public async Task NotifyPaymentConfirmedAsync(int paymentId, int orderId, string orderCode, decimal amount)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveNotification", new
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = "PaymentConfirmed",
                OccurredAtUtc = DateTime.UtcNow,
                EntityId = paymentId.ToString(),
                Message = $"Thanh toán cho đơn hàng {orderCode} số tiền {amount:N0} đ đã được xác nhận"
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send PaymentConfirmed notification");
        }
    }

    public async Task NotifyLowStockAsync(int productId, string productName, string barcode, decimal stock, decimal minStock)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveNotification", new
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = "LowStockAlert",
                OccurredAtUtc = DateTime.UtcNow,
                EntityId = productId.ToString(),
                Message = $"Cảnh báo tồn kho thấp: [{barcode}] {productName} hiện còn {stock} (Định mức tối thiểu: {minStock})"
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send LowStockAlert notification");
        }
    }

    public async Task NotifyShiftOpenedAsync(int shiftId, int userId, string username)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveNotification", new
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = "ShiftOpened",
                OccurredAtUtc = DateTime.UtcNow,
                EntityId = shiftId.ToString(),
                Message = $"Ca làm việc #{shiftId} đã được mở bởi {username}"
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send ShiftOpened notification");
        }
    }

    public async Task NotifyShiftClosedAsync(int shiftId, int userId, string username, decimal countedCash, decimal diff)
    {
        try
        {
            await _hubContext.Clients.All.SendAsync("ReceiveNotification", new
            {
                EventId = Guid.NewGuid().ToString(),
                EventType = "ShiftClosed",
                OccurredAtUtc = DateTime.UtcNow,
                EntityId = shiftId.ToString(),
                Message = $"Ca làm việc #{shiftId} đã đóng bởi {username} (Kiểm đếm: {countedCash:N0} đ, Chênh lệch: {diff:N0} đ)"
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to send ShiftClosed notification");
        }
    }
}
