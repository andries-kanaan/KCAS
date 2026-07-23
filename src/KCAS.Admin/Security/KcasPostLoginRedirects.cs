using KCAS.Admin.Data;
using Microsoft.AspNetCore.Identity;

namespace KCAS.Admin.Security;

public static class KcasPostLoginRedirects
{
    public const string ClientsPath = "/clients";
    public const string HomePath = "/";

    public static async Task<string> GetApprovedUserPathAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ApplicationUser? user)
    {
        if (user is null)
        {
            return HomePath;
        }

        var roleNames = await userManager.GetRolesAsync(user);
        foreach (var roleName in roleNames)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            var roleClaims = await roleManager.GetClaimsAsync(role);
            if (roleClaims.Any(claim =>
                    claim.Type == KcasClaimTypes.Permission &&
                    claim.Value == KcasPermissions.ClientsView))
            {
                return ClientsPath;
            }
        }

        return HomePath;
    }
}
