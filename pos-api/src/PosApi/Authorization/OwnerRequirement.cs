using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using System.Security.Claims;

namespace PosApi.Authorization;

public class OwnerRequirement : IAuthorizationRequirement
{
}

public class OwnerRequirementHandler : AuthorizationHandler<OwnerRequirement>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public OwnerRequirementHandler(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OwnerRequirement requirement)
    {
        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);

        if (user != null && user.IsActive && user.IsOwner)
        {
            context.Succeed(requirement);
        }
    }
}
