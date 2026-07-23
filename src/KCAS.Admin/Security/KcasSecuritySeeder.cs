using System.Security.Claims;
using KCAS.Admin.Data;
using Microsoft.AspNetCore.Identity;

namespace KCAS.Admin.Security;

public static class KcasSecuritySeeder
{
    private static readonly Dictionary<string, string[]> RolePermissions = new()
    {
        [KcasRoles.Administrator] = KcasPermissions.All.ToArray(),
        [KcasRoles.Advisor] =
        [
            KcasPermissions.ClientsView,
            KcasPermissions.ClientsManage,
            KcasPermissions.NotesManage,
            KcasPermissions.InvestmentsView,
            KcasPermissions.InvestmentsManage,
            KcasPermissions.KycView,
            KcasPermissions.KycManage,
            KcasPermissions.ReportsView
        ],
        [KcasRoles.Operations] =
        [
            KcasPermissions.ClientsView,
            KcasPermissions.ClientsManage,
            KcasPermissions.NotesManage,
            KcasPermissions.InvestmentsView,
            KcasPermissions.KycView,
            KcasPermissions.KycManage
        ],
        [KcasRoles.Reports] =
        [
            KcasPermissions.ClientsView,
            KcasPermissions.InvestmentsView,
            KcasPermissions.KycView,
            KcasPermissions.ReportsView
        ],
        [KcasRoles.ReadOnly] =
        [
            KcasPermissions.ClientsView,
            KcasPermissions.InvestmentsView,
            KcasPermissions.KycView,
            KcasPermissions.ReportsView
        ],
        [KcasRoles.ComplianceAdministrator] =
        [
            KcasPermissions.ComplianceView,
            KcasPermissions.ComplianceManage,
            KcasPermissions.ComplianceApprove,
            KcasPermissions.ComplianceAudit
        ],
        [KcasRoles.ComplianceApprover] =
        [
            KcasPermissions.ComplianceView,
            KcasPermissions.ComplianceApprove,
            KcasPermissions.ComplianceAudit
        ],
        [KcasRoles.ComplianceReadOnly] =
        [
            KcasPermissions.ComplianceView,
            KcasPermissions.ComplianceAudit
        ]
    };

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var (roleName, permissions) in RolePermissions)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                role = new IdentityRole(roleName);
                var createResult = await roleManager.CreateAsync(role);
                if (!createResult.Succeeded)
                {
                    throw new InvalidOperationException($"Could not create role '{roleName}': {FormatErrors(createResult)}");
                }
            }

            var existingClaims = await roleManager.GetClaimsAsync(role);
            var targetPermissions = permissions.ToHashSet(StringComparer.Ordinal);
            foreach (var staleClaim in existingClaims.Where(claim =>
                         claim.Type == KcasClaimTypes.Permission &&
                         !targetPermissions.Contains(claim.Value)))
            {
                var removeResult = await roleManager.RemoveClaimAsync(role, staleClaim);
                if (!removeResult.Succeeded)
                {
                    throw new InvalidOperationException($"Could not remove stale permission '{staleClaim.Value}' from role '{roleName}': {FormatErrors(removeResult)}");
                }
            }

            foreach (var permission in permissions)
            {
                if (existingClaims.Any(claim => claim.Type == KcasClaimTypes.Permission && claim.Value == permission))
                {
                    continue;
                }

                var addResult = await roleManager.AddClaimAsync(role, new Claim(KcasClaimTypes.Permission, permission));
                if (!addResult.Succeeded)
                {
                    throw new InvalidOperationException($"Could not add permission '{permission}' to role '{roleName}': {FormatErrors(addResult)}");
                }
            }
        }
    }

    public static async Task PromoteFirstUserIfNeededAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ApplicationUser user)
    {
        if (userManager.Users.Count() > 1)
        {
            return;
        }

        user.IsApproved = true;
        user.ApprovedAtUtc = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        if (await roleManager.RoleExistsAsync(KcasRoles.Administrator))
        {
            await userManager.AddToRoleAsync(user, KcasRoles.Administrator);
        }
    }

    private static string FormatErrors(IdentityResult result)
        => string.Join("; ", result.Errors.Select(error => error.Description));
}
