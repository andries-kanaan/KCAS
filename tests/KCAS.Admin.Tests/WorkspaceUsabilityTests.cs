using System.Net;
using System.Text.RegularExpressions;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class WorkspaceUsabilityTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Anonymous_home_has_no_client_metrics_or_privileged_navigation()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Foundation areas", html);
        Assert.DoesNotContain("metric-card", html);
        Assert.DoesNotContain("href=\"/imports\"", html);
        Assert.DoesNotContain("href=\"/security\"", html);
        Assert.Contains("Sign in", html);
        var css = await client.GetStringAsync("/kcas.css?v=6");
        Assert.Contains(".nav-scrollable.workspace-navigation", css);
        Assert.Contains(".client-workspace-nav", css);
        Assert.Contains(".client-overview-summary", css);
        var script = await client.GetStringAsync("/workspace-navigation.js?v=1");
        Assert.Contains(".workspace-action-menu[open]", script);
        Assert.Contains("Escape", script);
    }

    [Theory]
    [InlineData(KcasRoles.Administrator)]
    [InlineData(KcasRoles.ReadOnly)]
    [InlineData(KcasRoles.ComplianceReadOnly)]
    public async Task Actual_pages_render_permission_filtered_navigation_and_client_context(string role)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"workspace-ui-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Email = email, UserName = email, IsApproved = true };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        var record = new Client { DisplayName = "Synthetic navigation trust", SurnameOrEntityName = "Synthetic navigation trust", ClientCategory = ClientCategories.Trust, KanaanId = "UX-" + Guid.NewGuid().ToString("N")[..8] };
        try
        {
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            db.Clients.Add(record);
            await db.SaveChangesAsync();
            var principal = await services.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(user);
            var options = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
            var ticket = options.TicketDataFormat.Protect(new AuthenticationTicket(principal,
                new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) }, IdentityConstants.ApplicationScheme));
            using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={ticket}");
            var authorization = services.GetRequiredService<IAuthorizationService>();

            if ((await authorization.AuthorizeAsync(principal, null, KcasPermissions.ClientsView)).Succeeded)
            {
                var list = await client.GetStringAsync($"/clients?q={record.KanaanId}&sort=currentValue&direction=desc&page=1");
                Assert.Contains("Search clients", list);
                Assert.Contains("Reset filters", list);
                Assert.Contains("of 1 clients", list);
                Assert.Contains($"/clients/{record.Id}?returnTo=", list);
                var reviews = await client.GetStringAsync($"/clients/operational-review?q={record.KanaanId}&review=incomplete");
                Assert.Contains("of 1 clients", reviews);
                Assert.Contains("Review status", reviews);
                Assert.Contains($"/clients/{record.Id}/compliance-review?returnTo=", reviews);
                var empty = await client.GetStringAsync($"/clients/operational-review?q={record.KanaanId}&review=completed");
                Assert.Contains("No clients found", empty);
                var overview = await client.GetStringAsync($"/clients/{record.Id}?returnTo={Uri.EscapeDataString($"/clients?q={record.KanaanId}&page=1")}");
                Assert.Contains("Back to results", overview);
                Assert.Contains("aria-label=\"Client status summary\"", overview);
                Assert.Contains("Next compliance action", overview);
                Assert.Contains("Not started", overview);
                Assert.Equal((await authorization.AuthorizeAsync(principal, null, KcasPermissions.AdviceView)).Succeeded, overview.Contains("Latest advice"));
                Assert.Contains("role=\"tabpanel\" aria-label=\"Overview\"", overview);
                Assert.DoesNotContain("role=\"tabpanel\" aria-label=\"Investments\"", overview);
                Assert.DoesNotContain("role=\"tabpanel\" aria-label=\"Notes\"", overview);
                var notes = await client.GetStringAsync($"/clients/{record.Id}?tab=notes");
                Assert.Contains("role=\"tabpanel\" aria-label=\"Notes\"", notes);
                Assert.DoesNotContain("role=\"tabpanel\" aria-label=\"Overview\"", notes);
                Assert.DoesNotContain("role=\"tabpanel\" aria-label=\"Investments\"", notes);
                if ((await authorization.AuthorizeAsync(principal, null, KcasPermissions.InvestmentsView)).Succeeded)
                {
                    var investments = await client.GetStringAsync($"/clients/{record.Id}?tab=investments");
                    Assert.Contains("role=\"tabpanel\" aria-label=\"Investments\"", investments);
                    Assert.DoesNotContain("role=\"tabpanel\" aria-label=\"Overview\"", investments);
                    Assert.DoesNotContain("role=\"tabpanel\" aria-label=\"Notes\"", investments);
                }
            }

            var home = await client.GetStringAsync("/");
            var nav = Regex.Match(home, "<nav\\b[^>]*aria-label=\"Main navigation\"[^>]*>(.*?)</nav>", RegexOptions.Singleline).Groups[1].Value;
            Assert.NotEmpty(nav);
            Assert.DoesNotContain("Legacy imports", nav);
            Assert.DoesNotContain("Foundation areas", home);
            Assert.Equal(role == KcasRoles.Administrator, nav.Contains("href=\"/imports\""));
            Assert.Equal(role == KcasRoles.Administrator, nav.Contains("href=\"/compliance/review-transfers\""));

            foreach (var (suffix, policy) in new[] {
                ("", KcasPermissions.ClientsView), ("/compliance-review", KcasPermissions.ClientsView),
                ("/evidence", KcasPermissions.ComplianceView), ("/risk", KcasPermissions.RiskAssessmentsView),
                ("/onboarding", KcasPermissions.RiskAssessmentsView), ("/advice", KcasPermissions.AdviceView) })
            {
                if (!(await authorization.AuthorizeAsync(principal, null, policy)).Succeeded) continue;
                var html = await client.GetStringAsync($"/clients/{record.Id}{suffix}");
                Assert.Contains("aria-label=\"Client workspace\"", html);
                var contextLinks = Regex.Match(html, "<nav\\b[^>]*aria-label=\"Client workspace\"[^>]*>(.*?)</nav>", RegexOptions.Singleline).Groups[1].Value;
                Assert.NotEmpty(contextLinks);
                Assert.Equal((await authorization.AuthorizeAsync(principal, null, KcasPermissions.ClientsView)).Succeeded,
                    contextLinks.Contains($"href=\"/clients/{record.Id}?tab=overview\""));
                Assert.Contains(record.DisplayName, html);
                if (suffix == "/evidence") Assert.Contains("Ownership and control", html);
            }

            if ((await authorization.AuthorizeAsync(principal, null, KcasPermissions.ComplianceView)).Succeeded)
            {
                var programme = await client.GetStringAsync("/compliance/programme");
                var content = Regex.Match(programme, "<ol\\b[^>]*class=\"programme-navigation\"[^>]*>(.*?)</ol>", RegexOptions.Singleline).Groups[1].Value;
                Assert.NotEmpty(content);
                Assert.True(content.IndexOf("/compliance/methodologies", StringComparison.Ordinal) < content.IndexOf("/compliance/business-risk", StringComparison.Ordinal));
                Assert.True(content.IndexOf("/compliance/business-risk", StringComparison.Ordinal) < content.IndexOf("/compliance/rmcp", StringComparison.Ordinal));
                Assert.Contains("href=\"/compliance/work-register\"", content);
                Assert.Contains("Task configuration", content);
                var evidence = await client.GetStringAsync($"/compliance/client-evidence?q={record.KanaanId}&readiness=NotReady&page=1");
                Assert.Contains("of 1", evidence);
                Assert.Contains("Kanaan ID", evidence);
                Assert.Contains("Client folder configuration", evidence);
                Assert.Contains($"/clients/{record.Id}/evidence?returnTo=", evidence);
            }
        }
        finally
        {
            if (record.Id > 0)
            {
                db.ComplianceTasks.RemoveRange(await db.ComplianceTasks.Where(task => task.ClientId == record.Id).ToListAsync());
                db.Clients.Remove(record);
                await db.SaveChangesAsync();
            }
            Assert.True((await users.DeleteAsync(user)).Succeeded);
        }
    }
}
