using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PosApi.Hubs;

/// <summary>
/// SPRINT 4. SIGNALR REALTIME HUB
/// Route: /hubs/pos
/// Yêu cầu xác thực JWT cho tất cả kết nối client.
/// </summary>
[Authorize]
public class PosHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
