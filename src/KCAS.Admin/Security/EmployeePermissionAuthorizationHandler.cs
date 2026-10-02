using System.Security.Claims;
using KCAS.Admin.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Security;

public sealed record EmployeePermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class EmployeePermissionAuthorizationHandler(IDbContextFactory<ApplicationDbContext> factory)
    : AuthorizationHandler<EmployeePermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, EmployeePermissionRequirement requirement)
    {
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (context.User.Identity?.IsAuthenticated != true || id is null) return;
        await using var db = await factory.CreateDbContextAsync();
        if (!await db.Users.AnyAsync(x => x.Id == id && x.IsApproved)) return;
        if (await (from role in db.UserRoles where role.UserId == id
                   join claim in db.RoleClaims on role.RoleId equals claim.RoleId
                   where claim.ClaimType == KcasClaimTypes.Permission && claim.ClaimValue == requirement.Permission
                   select role).AnyAsync()) context.Succeed(requirement);
    }
}
