using System.Text.Json;
using PosApi.Data;
using PosApi.Domain;

namespace PosApi.Services;

/// <summary>
/// BE-05: ghi audit log trong transaction hiện hành.
/// Bảng append-only (DB trigger chặn UPDATE/DELETE). Không ghi password/token/secret.
/// </summary>
public static class PosLogService
{
    public static void Write(AppDbContext db, string action, int? userId,
        string? entityType = null, string? entityId = null, object? detail = null)
    {
        db.PosLogs.Add(new PosLog
        {
            Action = action,
            UserId = userId,
            EntityType = entityType,
            EntityId = entityId,
            DetailJson = detail is null ? null : JsonSerializer.Serialize(detail),
            CreatedAt = DateTime.UtcNow
        });
    }
}
