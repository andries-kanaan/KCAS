using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class SecuritySeederTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Seeds_expected_roles_and_permissions()
    {
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        var expectedRoles = new[]
        {
            KcasRoles.Administrator,
            KcasRoles.Advisor,
            KcasRoles.ComplianceAdministrator,
            KcasRoles.ComplianceApprover,
            KcasRoles.ComplianceReadOnly,
            KcasRoles.Operations,
            KcasRoles.ReadOnly,
            KcasRoles.Reports
        };

        foreach (var roleName in expectedRoles)
        {
            Assert.True(await roleManager.RoleExistsAsync(roleName), $"Missing role '{roleName}'.");
        }

        var administrator = await roleManager.FindByNameAsync(KcasRoles.Administrator);
        Assert.NotNull(administrator);

        var administratorClaims = await roleManager.GetClaimsAsync(administrator);
        var administratorPermissions = administratorClaims
            .Where(claim => claim.Type == KcasClaimTypes.Permission)
            .Select(claim => claim.Value)
            .Order()
            .ToArray();

        Assert.Equal(KcasPermissions.All.Order().ToArray(), administratorPermissions);
        Assert.Contains(KcasPermissions.ComplianceView, administratorPermissions);
        Assert.Contains(KcasPermissions.ComplianceManage, administratorPermissions);
        Assert.Contains(KcasPermissions.ComplianceApprove, administratorPermissions);
        Assert.Contains(KcasPermissions.ComplianceAudit, administratorPermissions);
    }

    [Fact]
    public async Task Removes_stale_kcas_permission_claims()
    {
        using var scope = factory.Services.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var role = await roleManager.FindByNameAsync(KcasRoles.ReadOnly);
        Assert.NotNull(role);

        var staleClaim = new System.Security.Claims.Claim(KcasClaimTypes.Permission, "Clients.Edit");
        var addResult = await roleManager.AddClaimAsync(role, staleClaim);
        Assert.True(addResult.Succeeded);

        await KcasSecuritySeeder.SeedAsync(factory.Services);

        var claims = await roleManager.GetClaimsAsync(role);
        Assert.DoesNotContain(claims, claim =>
            claim.Type == KcasClaimTypes.Permission &&
            claim.Value == staleClaim.Value);
    }

    [Fact]
    public async Task Promotes_first_user_to_approved_administrator()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var existingUser in userManager.Users.ToList())
        {
            var deleteResult = await userManager.DeleteAsync(existingUser);
            Assert.True(deleteResult.Succeeded, string.Join("; ", deleteResult.Errors.Select(error => error.Description)));
        }

        var user = new ApplicationUser
        {
            UserName = "first.admin@example.test",
            Email = "first.admin@example.test",
            IsApproved = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, "Passw0rd!Test");
        Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(error => error.Description)));

        await KcasSecuritySeeder.PromoteFirstUserIfNeededAsync(userManager, roleManager, user);

        var refreshedUser = await userManager.FindByEmailAsync(user.Email);
        Assert.NotNull(refreshedUser);
        Assert.True(refreshedUser.IsApproved);
        Assert.True(await userManager.IsInRoleAsync(refreshedUser, KcasRoles.Administrator));
    }

    [Fact]
    public async Task Post_login_redirect_prefers_clients_when_user_can_view_clients()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        var user = await CreateApprovedUserAsync(userManager, "clients-redirect@example.test");
        var addToRoleResult = await userManager.AddToRoleAsync(user, KcasRoles.ReadOnly);
        Assert.True(addToRoleResult.Succeeded, string.Join("; ", addToRoleResult.Errors.Select(error => error.Description)));

        var redirectPath = await KcasPostLoginRedirects.GetApprovedUserPathAsync(userManager, roleManager, user);

        Assert.Equal(KcasPostLoginRedirects.ClientsPath, redirectPath);
    }

    [Fact]
    public async Task Post_login_redirect_falls_back_home_when_clients_are_not_available()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        var roleName = $"NoClients-{Guid.NewGuid():N}";
        var createRoleResult = await roleManager.CreateAsync(new IdentityRole(roleName));
        Assert.True(createRoleResult.Succeeded, string.Join("; ", createRoleResult.Errors.Select(error => error.Description)));

        var user = await CreateApprovedUserAsync(userManager, "home-redirect@example.test");
        var addToRoleResult = await userManager.AddToRoleAsync(user, roleName);
        Assert.True(addToRoleResult.Succeeded, string.Join("; ", addToRoleResult.Errors.Select(error => error.Description)));

        var redirectPath = await KcasPostLoginRedirects.GetApprovedUserPathAsync(userManager, roleManager, user);

        Assert.Equal(KcasPostLoginRedirects.HomePath, redirectPath);
    }

    private static async Task<ApplicationUser> CreateApprovedUserAsync(
        UserManager<ApplicationUser> userManager,
        string email)
    {
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            IsApproved = true,
            CreatedAtUtc = DateTime.UtcNow,
            ApprovedAtUtc = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, "Passw0rd!Test");
        Assert.True(createResult.Succeeded, string.Join("; ", createResult.Errors.Select(error => error.Description)));

        return user;
    }
}
