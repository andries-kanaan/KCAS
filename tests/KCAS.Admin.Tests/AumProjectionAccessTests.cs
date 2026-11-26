using System.Net;
using System.Security.Claims;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class AumProjectionAccessTests(KcasWebApplicationFactory factory)
{
    private const string PagePath = "/investments/aum-projections";

    [Fact]
    public async Task ApprovedUsersCanUseProjectionsButOnlyAdministratorsCanImportTheBaseline()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var projections = services.GetRequiredService<AumProjectionService>();
        var (admin, adminPrincipal) = await CreateAccountAsync(services, true, KcasRoles.Administrator);
        var (staff, staffPrincipal) = await CreateAccountAsync(services, true);
        // A stale role claim must not let a pending account read or write AUM data.
        var (pending, pendingPrincipal) = await CreateAccountAsync(services, false, KcasRoles.Administrator);
        var source = AumProjectionTests.Fixture() with { Source = "Access test " + Guid.NewGuid().ToString("N") };
        var json = FundModelJson.Write(source);
        try
        {
            var snapshot = await projections.ImportAsync(json, adminPrincipal);
            var id = await projections.SaveAsync(snapshot.Id, "AUM access test", [], adminPrincipal);
            var reportPath = $"/aum-projections/runs/{id}/xlsx";

            using var viewer = CreateClient(services, staffPrincipal);
            var home = await viewer.GetStringAsync("/");
            Assert.Contains("href=\"" + PagePath + "\"", home);
            var html = await viewer.GetStringAsync(PagePath);
            Assert.Contains("Annual results (ZAR)", html);
            Assert.Contains("Save projection", html);
            using var download = await viewer.GetAsync(reportPath);
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                download.Content.Headers.ContentType?.MediaType);
            Assert.Equal(id, (await projections.RecentAsync(staffPrincipal)).First(x => x.Id == id).Id);
            Assert.NotNull(await projections.LoadAsync(id, staffPrincipal));
            Assert.Equal("Test preview", (await projections.PreviewAsync(snapshot.Id, "Test preview", [], staffPrincipal)).Name);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => projections.ImportAsync(json, staffPrincipal));
            var staffId = await projections.SaveAsync(snapshot.Id, "Staff scenario",
                new() { [AumProjectionEngine.Equity] = .115 }, staffPrincipal);
            Assert.Equal(staff.Email, (await projections.RecentAsync(adminPrincipal)).Single(x => x.Id == staffId).Actor);
            Assert.Equal(.115, (await projections.LoadAsync(staffId, staffPrincipal))!.Overrides[AumProjectionEngine.Equity]);
            Assert.Equal(snapshot.Id, (await projections.LatestAsync(staffPrincipal))!.Id);
            Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/aum-projections/runs/{staffId}/pdf")).StatusCode);

            using var administrator = CreateClient(services, adminPrincipal);
            Assert.Contains("Save projection", await administrator.GetStringAsync(PagePath));

            using var guest = CreateClient(services, pendingPrincipal);
            Assert.DoesNotContain("href=\"" + PagePath + "\"", await guest.GetStringAsync("/"));
            await AssertBlockedAsync(guest, PagePath);
            await AssertBlockedAsync(guest, reportPath);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => projections.LoadAsync(id, pendingPrincipal));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => projections.PreviewAsync(snapshot.Id, "Denied", [], pendingPrincipal));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => projections.SaveAsync(snapshot.Id, "Denied", [], pendingPrincipal));

            using var anonymous = CreateClient(services, null);
            await AssertBlockedAsync(anonymous, PagePath);
            await AssertBlockedAsync(anonymous, reportPath);

            staff.IsApproved = false;
            Assert.True((await users.UpdateAsync(staff)).Succeeded);
            await AssertBlockedAsync(viewer, reportPath);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => projections.LoadAsync(id, staffPrincipal));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => projections.SaveAsync(snapshot.Id, "Denied", [], staffPrincipal));
        }
        finally
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            db.ModelEvents.RemoveRange(await db.ModelEvents.Where(e => e.Actor == admin.Email || e.Actor == staff.Email).ToListAsync());
            await db.SaveChangesAsync();
            foreach (var user in new[] { admin, staff, pending })
                Assert.True((await users.DeleteAsync(user)).Succeeded);
        }
    }

    private static async Task<(ApplicationUser User, ClaimsPrincipal Principal)> CreateAccountAsync(IServiceProvider services, bool approved, string? role = null)
    {
        var email = $"aum-access-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Email = email, UserName = email, IsApproved = approved };
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.CreateAsync(user)).Succeeded);
        if (role is not null) Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return (user, await services.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(user));
    }

    private HttpClient CreateClient(IServiceProvider services, ClaimsPrincipal? principal)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        if (principal is null) return client;
        var options = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = options.TicketDataFormat.Protect(new AuthenticationTicket(principal,
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) }, IdentityConstants.ApplicationScheme));
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={ticket}");
        return client;
    }

    private static async Task AssertBlockedAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.True(response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"Unexpected status for {path}: {response.StatusCode}");
    }
}
