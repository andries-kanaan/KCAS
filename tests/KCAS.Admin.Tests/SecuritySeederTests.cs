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
}
