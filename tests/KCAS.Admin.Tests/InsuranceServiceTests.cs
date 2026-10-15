using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class InsuranceServiceTests(KcasWebApplicationFactory factory)
{
    [Theory]
    [InlineData("Compliance/PI Cover/2026/../secret.pdf")]
    [InlineData("Compliance/PI Cover/2026/C:/secret.pdf")]
    [InlineData("/Compliance/PI Cover/2026/a.pdf")]
    [InlineData("Compliance/Elsewhere/2026/a.pdf")]
    [InlineData("\\\\host\\Compliance\\PI Cover\\a.pdf")]
    public void Portable_paths_reject_traversal_and_arbitrary_roots(string value)
    {
        var files = new InsuranceFiles(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Insurance:ComplianceRoot"] = Path.GetTempPath() }).Build());
        Assert.Throws<ValidationException>(() => files.Relative(value));
    }

    [Fact]
    public async Task Review_records_Codex_closes_only_preparation_and_terms_changes_invalidate_review()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var root = Path.Combine(Path.GetTempPath(), "kcas-insurance-" + Guid.NewGuid().ToString("N"));
        var files = Files(root); var service = new InsuranceService(db, files); var id = 0;
        try
        {
            var path = "Compliance/PI Cover/2030/quote.pdf";
            Directory.CreateDirectory(Path.GetDirectoryName(files.Resolve(path))!);
            await File.WriteAllTextAsync(files.Resolve(path), "Synthetic quotation");
            var model = Policy(); id = await service.SaveAsync(model, "Synthetic source capture", actor);
            var row = await service.LoadAsync(id, actor);
            await service.LinkAsync(id, row.Version, "Quotation", "Quotation", path, null, "Read actual quotation", actor);
            row = await service.LoadAsync(id, actor);
            var handoff = await service.RequestCodexAsync(id, row.Version, actor);
            row = await service.LoadAsync(id, actor);
            Assert.Contains("Do not scan folders", handoff);
            Assert.Contains(await service.NotificationsAsync(actor), x => x.Id == id);
            await service.RequestCodexAsync(id, row.Version, actor);
            Assert.Equal(1, await db.ComplianceTasks.CountAsync(x => x.InsurancePolicyId == id));
            await service.RecordReviewAsync(id, row.Version, "Compared actual previous and current cover", "", actor);
            row = await service.LoadAsync(id, actor);
            Assert.Equal(InsuranceStages.Reviewed, row.Status);
            Assert.Equal(row.Version, row.ReviewedVersion);
            Assert.Contains(row.Events, x => x.Action == "CodexReviewRecorded" && x.Performer == "Codex" && x.RecordedBy == actor.Identity!.Name);
            Assert.DoesNotContain(await service.NotificationsAsync(actor), x => x.Id == id);
            var task = await db.ComplianceTasks.SingleAsync(x => x.InsurancePolicyId == id);
            Assert.Equal(ComplianceStatuses.Closed, task.Status);
            var user = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(actor.FindFirstValue(ClaimTypes.NameIdentifier)!))!;
            var webPrincipal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(user);
            var cookies = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
            var ticket = cookies.TicketDataFormat.Protect(new AuthenticationTicket(webPrincipal,
                new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10) }, IdentityConstants.ApplicationScheme));
            using var http = factory.CreateClient(new() { AllowAutoRedirect = false });
            Assert.Equal(System.Net.HttpStatusCode.Redirect, (await http.GetAsync("/compliance/insurance")).StatusCode);
            http.DefaultRequestHeaders.Add("Cookie", $"{cookies.Cookie.Name}={ticket}");
            Assert.Contains("Annual premium incl. VAT", await http.GetStringAsync("/compliance/insurance"));
            var html = await http.GetStringAsync($"/compliance/insurance/{id}");
            Assert.Contains("CodexReviewRecorded", html);
            Assert.Contains("Recorded monthly instalment", html);
            Assert.Contains("Update policy terms", await http.GetStringAsync($"/compliance/insurance/{id}/edit"));
            await Assert.ThrowsAsync<ValidationException>(() => scope.ServiceProvider.GetRequiredService<ComplianceWorkService>()
                .RequestClosureAsync(task.Id, "Reviewed", "Done", "Close", actor.Identity!.Name, "Cannot replace controlled workflow"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AdvanceAsync(id, row.Version, InsuranceStages.Accepted,
                DateOnly.FromDateTime(DateTime.Today), "Cannot impersonate a KI", path, actor));
            row.PremiumExVat += 1;
            await service.SaveAsync(row, "New quoted premium", actor);
            var changed = await service.LoadAsync(id, actor);
            Assert.Null(changed.ReviewedVersion);
            Assert.Equal(InsuranceStages.QuotationReceived, changed.Status);
            await Assert.ThrowsAsync<ValidationException>(() => service.RecordReviewAsync(id, row.Version, "Stale", "", actor));
        }
        finally { await Cleanup(db, id); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Renewal_task_survives_acceptance_and_closes_only_after_evidenced_next_year_confirmation()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator, KcasRoles.ComplianceApprover);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var root = Path.Combine(Path.GetTempPath(), "kcas-insurance-" + Guid.NewGuid().ToString("N"));
        var files = Files(root); var service = new InsuranceService(db, files); var ids = new List<int>();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var assignment = new GovernanceRoleAssignment { RoleType = "Key Individual", PersonName = "Synthetic KI", Email = actor.Identity!.Name, IsActive = true };
        db.GovernanceRoleAssignments.Add(assignment);
        await db.SaveChangesAsync();
        try
        {
            var old = Policy(); old.StartDate = today.AddYears(-1); old.EndDate = today;
            var oldId = await service.SaveAsync(old, "Current policy", actor); ids.Add(oldId);
            var next = Policy(); next.PolicyNumber = old.PolicyNumber; next.StartDate = today.AddDays(1); next.EndDate = today.AddYears(1);
            var nextId = await service.SaveAsync(next, "Renewal", actor); ids.Add(nextId);
            var path = "Compliance/PI Cover/2030/quote.pdf";
            Directory.CreateDirectory(Path.GetDirectoryName(files.Resolve(path))!);
            await File.WriteAllTextAsync(files.Resolve(path), "Synthetic quotation");
            var row = await service.LoadAsync(nextId, actor);
            await service.LinkAsync(nextId, row.Version, "Quote", "Quotation", path, today, "Quote source", actor);
            row = await service.LoadAsync(nextId, actor);
            await service.AdvanceAsync(nextId, row.Version, InsuranceStages.QuotationReceived, today, "Quotation received", path, actor);
            row = await service.LoadAsync(nextId, actor);
            await service.AdvanceAsync(nextId, row.Version, InsuranceStages.Reviewed, today, "Human review", "", actor);
            row = await service.LoadAsync(nextId, actor);
            Assert.True(await service.CanAcceptAsync(actor));
            await Assert.ThrowsAsync<ValidationException>(() => service.AdvanceAsync(nextId, row.Version, InsuranceStages.Accepted, today, "Quotation is not acceptance", path, actor));
            var acceptance = "Compliance/PI Cover/2030/acceptance.txt";
            await File.WriteAllTextAsync(files.Resolve(acceptance), "Synthetic KI acceptance correspondence");
            await service.AdvanceAsync(nextId, row.Version, InsuranceStages.Accepted, today, "Actual KI acceptance", acceptance, actor);
            Assert.Equal(ComplianceWorkStatuses.Open, (await db.ComplianceTasks.SingleAsync(x => x.InsurancePolicyId == oldId)).Status);
            row = await service.LoadAsync(nextId, actor);
            await Assert.ThrowsAsync<ValidationException>(() => service.AdvanceAsync(nextId, row.Version, InsuranceStages.Confirmed, today, "Quotation is not confirmation", path, actor));
            var confirmed = "Compliance/PI Cover/2030/schedule.pdf";
            await File.WriteAllTextAsync(files.Resolve(confirmed), "Synthetic final schedule");
            await service.AdvanceAsync(nextId, row.Version, InsuranceStages.Confirmed, today, "Insurer issued schedule", confirmed, actor);
            Assert.Equal(ComplianceStatuses.Closed, (await db.ComplianceTasks.SingleAsync(x => x.InsurancePolicyId == oldId)).Status);
            row = await service.LoadAsync(nextId, actor);
            await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(row, "Cannot change frozen cover", actor));
        }
        finally { foreach (var id in ids) await Cleanup(db, id); await db.GovernanceRoleAssignments.Where(x => x.Id == assignment.Id).ExecuteDeleteAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    internal static InsuranceFiles Files(string root) => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Insurance:ComplianceRoot"] = root }).Build());
    internal static InsurancePolicy Policy() => new() { InsuredName = "Synthetic asset manager", PolicyNumber = Guid.NewGuid().ToString("N"), Insurer = "Synthetic insurer",
        StartDate = new(2030, 1, 1), EndDate = new(2030, 12, 31), Limit = 5_000_000, Excess = 250_000, PremiumExVat = 1000, Vat = 150 };
    private static async Task Cleanup(ApplicationDbContext db, int id)
    {
        db.ChangeTracker.Clear();
        await db.ComplianceTasks.Where(x => x.InsurancePolicyId == id).ExecuteDeleteAsync();
        await db.InsurancePolicies.Where(x => x.Id == id).ExecuteDeleteAsync();
    }
}
