using System.Security.Claims;
using KCAS.Admin.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Security;

public sealed class ApprovedAccountRequirement : IAuthorizationRequirement;

public sealed class ApprovedAccountAuthorizationHandler(IDbContextFactory<ApplicationDbContext> factory)
    : AuthorizationHandler<ApprovedAccountRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ApprovedAccountRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;
        await using var db = await factory.CreateDbContextAsync();
        if (await IsApprovedAsync(db, context.User)) context.Succeed(requirement);
    }

    internal static Task<bool> IsApprovedAsync(ApplicationDbContext db, ClaimsPrincipal principal)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return principal.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(id)
            ? db.Users.AnyAsync(user => user.Id == id && user.IsApproved)
            : Task.FromResult(false);
    }
}
