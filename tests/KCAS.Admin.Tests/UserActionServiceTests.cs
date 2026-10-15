using System.Security.Claims;
using System.Text.Json;
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
public sealed class UserActionServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Codex_actions_follow_recorded_recipients_and_do_not_clear_on_read_or_generic_closure()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = scope.ServiceProvider.GetRequiredService<UserActionService>();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var client = Client(); db.Clients.Add(client); await db.SaveChangesAsync();
        var onboarding = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        await onboarding.RequestCodexAsync(client.Id, "Synthetic request", actor);
        await onboarding.RequestCodexAsync(client.Id, "Same scope", actor);
        var lateRecipient = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var key = $"codex:{client.Id}";
        Assert.Equal($"/clients/{client.Id}/onboarding#codex-handoff", Assert.Single(await actions.LoadAsync(actor), x => x.Key == key).Url);
        Assert.Single(await actions.LoadAsync(actor), x => x.Key == key);
        Assert.DoesNotContain(await actions.LoadAsync(lateRecipient), x => x.Key == key);
        var request = await db.ClientCodexReviewRequests.Include(x => x.Task).SingleAsync(x => x.ClientId == client.Id);
        request.Task.Status = ComplianceStatuses.Closed;
        await db.SaveChangesAsync();
        Assert.Contains(await actions.LoadAsync(actor), x => x.Key == key);
        request.Status = "ResultsRecorded";
        await db.SaveChangesAsync();
        Assert.DoesNotContain(await actions.LoadAsync(actor), x => x.Key == key);
    }

    [Fact]
    public async Task Independent_review_excludes_preparer_and_Codex_recorder_and_clears_on_return()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = scope.ServiceProvider.GetRequiredService<UserActionService>();
        var preparer = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var recorder = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var reviewer = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var item = new ClientAdviceCase { Client = Client(), PreparedBy = preparer.Identity!.Name!, Status = ClientAdviceStatuses.ReadyForReview };
        db.ClientAdviceCases.Add(item); await db.SaveChangesAsync();
        db.ComplianceAuditEvents.Add(new() { EntityType = nameof(ClientAdviceCase), EntityId = item.Id,
            Action = "AdviceCodexPreparationRecorded", UserName = recorder.Identity!.Name });
        await db.SaveChangesAsync();
        var key = $"advice-review:{item.Id}";
        Assert.DoesNotContain(await actions.LoadAsync(preparer), x => x.Key == key);
        Assert.DoesNotContain(await actions.LoadAsync(recorder), x => x.Key == key);
        Assert.Contains(await actions.LoadAsync(reviewer), x => x.Key == key && x.Url == $"/clients/{item.ClientId}/advice/{item.Id}#independent-review");
        await scope.ServiceProvider.GetRequiredService<ClientAdviceService>().ReturnAsync(item.Id, reviewer.Identity!.Name, "Synthetic return");
        Assert.DoesNotContain(await actions.LoadAsync(reviewer), x => x.Key == key);
        Assert.Contains(await actions.LoadAsync(preparer), x => x.Key == $"advice-returned:{item.Id}");
    }

    [Fact]
    public async Task Preparation_and_participant_privacy_match_advice_access()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = scope.ServiceProvider.GetRequiredService<UserActionService>();
        var officer = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var admin = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var item = new ClientAdviceCase { Client = Client(), PreparedBy = "synthetic@example.test" };
        item.Participants.Add(new() { Client = Client(excluded: true) });
        db.ClientAdviceCases.Add(item); await db.SaveChangesAsync();
        var task = new ComplianceTask { TaskType = ComplianceTaskTypes.AdvicePreparation, Status = ComplianceWorkStatuses.Open,
            ClientId = item.ClientId, LinkedEntityType = nameof(ClientAdviceCase), LinkedEntityId = item.Id, Title = "Synthetic preparation" };
        db.ComplianceTasks.Add(task); await db.SaveChangesAsync();
        var key = $"advice-preparation:{item.Id}";
        Assert.DoesNotContain(await actions.LoadAsync(officer), x => x.Key == key);
        Assert.Contains(await actions.LoadAsync(admin), x => x.Key == key && x.Url.Contains($"/{item.Id}/preparation#"));
        task.Status = ComplianceStatuses.Closed; await db.SaveChangesAsync();
        Assert.DoesNotContain(await actions.LoadAsync(admin), x => x.Key == key);
    }

    [Fact]
    public async Task Shared_closure_disappears_for_each_approver_after_their_approval()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = scope.ServiceProvider.GetRequiredService<UserActionService>();
        var first = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceApprover);
        var second = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceApprover);
        var work = scope.ServiceProvider.GetRequiredService<ComplianceWorkService>();
        var taskId = await work.SaveAsync(new() { Title = "Synthetic high review " + Guid.NewGuid(), Priority = "High", Owner = "Synthetic owner" }, "synthetic@example.test", "Synthetic work");
        await work.RequestClosureAsync(taskId, "Synthetic evidence", "Reviewed", "Supported closure", "synthetic@example.test", "Synthetic request");
        var key = $"task:{taskId}";
        Assert.Contains(await actions.LoadAsync(first), x => x.Key == key && x.Url == $"/compliance/work-register/{taskId}");
        await work.ApproveClosureAsync(taskId, first.Identity!.Name, "First approval");
        Assert.DoesNotContain(await actions.LoadAsync(first), x => x.Key == key);
        Assert.Contains(await actions.LoadAsync(second), x => x.Key == key);
        await work.ApproveClosureAsync(taskId, second.Identity!.Name, "Second approval");
        Assert.DoesNotContain(await actions.LoadAsync(second), x => x.Key == key);
    }

    [Fact]
    public async Task Methodology_needs_current_KI_and_BRA_does_not_repeat_a_recorded_approval()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = scope.ServiceProvider.GetRequiredService<UserActionService>();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var method = new RiskMethodologyVersion { Name = "Synthetic bell method " + Guid.NewGuid(), Status = ComplianceStatuses.Review };
        var bra = new BusinessRiskAssessment { Name = "Synthetic bell BRA " + Guid.NewGuid(), Status = ComplianceStatuses.Review,
            AssessmentYear = 2026, AsAtDate = DateOnly.FromDateTime(DateTime.Today) };
        db.AddRange(method, bra); await db.SaveChangesAsync();
        Assert.DoesNotContain(await actions.LoadAsync(actor), x => x.Key == $"methodology:{method.Id}");
        Assert.Contains(await actions.LoadAsync(actor), x => x.Key == $"bra:{bra.Id}");
        db.GovernanceRoleAssignments.Add(new() { RoleType = "Key Individual", PersonName = "Synthetic KI", Email = actor.Identity!.Name, IsActive = true });
        bra.Approvals.Add(new() { Approver = actor.Identity!.Name!, Reason = "Synthetic first approval" });
        await db.SaveChangesAsync();
        Assert.Contains(await actions.LoadAsync(actor), x => x.Key == $"methodology:{method.Id}" && x.Url.EndsWith($"#methodology-{method.Id}"));
        Assert.DoesNotContain(await actions.LoadAsync(actor), x => x.Key == $"bra:{bra.Id}");
        await scope.ServiceProvider.GetRequiredService<ComplianceService>().ApproveMethodologyAsync(method.Id, actor.Identity!.Name, "Synthetic approval");
        Assert.DoesNotContain(await actions.LoadAsync(actor), x => x.Key == $"methodology:{method.Id}");
    }

    [Fact]
    public async Task Acceptance_is_not_duplicated_as_risk_approval_and_clears_after_KI_decision()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = scope.ServiceProvider.GetRequiredService<UserActionService>();
        var officer = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var ki = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor, KcasRoles.ComplianceApprover);
        db.GovernanceRoleAssignments.Add(new() { RoleType = "KI", PersonName = "Synthetic acceptance KI", Email = ki.Identity!.Name, IsActive = true });
        await db.SaveChangesAsync();
        var clientId = await ClientOnboardingServiceTests.ReadyAsync(scope, officer);
        var batch = new ClientSanctionsBatch { SourceVersion = "Synthetic bell coverage " + Guid.NewGuid(),
            SourceUrl = "https://www.fic.gov.za/", SourcePublishedAtUtc = DateTime.UtcNow, CreatedBy = "Synthetic test fixture", Reason = "Isolated notification test" };
        db.ClientSanctionsBatches.Add(batch);
        await db.SaveChangesAsync();
        foreach (var subject in await ClientSanctionsCoverageService.PopulationAsync(db, clientId))
        {
            subject.Batch = batch;
            db.ClientSanctionsCoverageRecords.Add(new() { Subject = subject, Outcome = "Excluded", ExclusionReference = "Synthetic fixture only",
                Reason = "Isolated bell test; not a real screening finding", RecordedBy = "Synthetic test fixture" });
        }
        await db.SaveChangesAsync();
        try
        {
        var onboarding = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        await onboarding.RequestCodexAsync(clientId, "Synthetic handoff", officer);
        Assert.DoesNotContain(await actions.LoadAsync(ki), x => x.Key == $"acceptance:{clientId}");
        await onboarding.ValidateRecordedResultsAsync(clientId, officer);
        Assert.Contains(await actions.LoadAsync(ki), x => x.Key == $"acceptance:{clientId}" && x.Url == $"/clients/{clientId}/onboarding#ki-decision");
        var model = await onboarding.LoadAsync(clientId, ki);
        await onboarding.DecideAsync(clientId, model.ContentHash, "Accepted", "Synthetic KI decision", ki);
        Assert.DoesNotContain(await actions.LoadAsync(ki), x => x.Key == $"acceptance:{clientId}");
        }
        finally
        {
            db.ClientSanctionsCoverageRecords.RemoveRange(await db.ClientSanctionsCoverageRecords.Where(x => x.Subject.ClientSanctionsBatchId == batch.Id).ToListAsync());
            await db.SaveChangesAsync();
            db.ClientSanctionsSubjects.RemoveRange(await db.ClientSanctionsSubjects.Where(x => x.ClientSanctionsBatchId == batch.Id).ToListAsync());
            await db.SaveChangesAsync();
            db.ClientSanctionsBatches.Remove(batch);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Outstanding_work_is_personal_overdue_first_and_revoked_accounts_get_nothing()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actions = scope.ServiceProvider.GetRequiredService<UserActionService>();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var tasks = new[] {
            new ComplianceTask { Title = "Synthetic future", Owner = actor.Identity!.Name, Status = ComplianceWorkStatuses.Open, DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(3)) },
            new ComplianceTask { Title = "Synthetic overdue", Owner = actor.Identity!.Name, Status = ComplianceWorkStatuses.Open, DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)) },
            new ComplianceTask { Title = "Synthetic other owner", Owner = "someone-else@example.test", Status = ComplianceWorkStatuses.Open },
            new ComplianceTask { Title = "Synthetic restricted", Client = Client(excluded: true), Owner = actor.Identity!.Name, Status = ComplianceWorkStatuses.Open }
        };
        db.ComplianceTasks.AddRange(tasks); await db.SaveChangesAsync();
        var mine = (await actions.LoadAsync(actor)).Where(x => tasks.Any(t => x.Key == $"task:{t.Id}")).ToList();
        Assert.Equal(new[] { $"task:{tasks[1].Id}", $"task:{tasks[0].Id}" }, mine.Select(x => x.Key));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(actor.FindFirstValue(ClaimTypes.NameIdentifier)!))!;
        Assert.True((await users.RemoveFromRoleAsync(user, KcasRoles.ComplianceAdministrator)).Succeeded);
        Assert.Empty(await actions.LoadAsync(actor));
        user.IsApproved = false; await users.UpdateAsync(user);
        Assert.Empty(await actions.LoadAsync(actor));
        Assert.Empty(await actions.LoadAsync(new()));
    }

    private static Client Client(bool excluded = false) => new() { DisplayName = "Synthetic bell client " + Guid.NewGuid().ToString("N"),
        SurnameOrEntityName = "Synthetic", ExcludeFromComplianceLists = excluded };

    [Fact]
    public async Task Signed_in_header_contains_bell_with_no_intermediate_actions_page()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var client = Client(); db.Clients.Add(client); await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<ClientOnboardingService>().RequestCodexAsync(client.Id, "Synthetic browser handoff", actor);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(actor.FindFirstValue(ClaimTypes.NameIdentifier)!))!;
        var principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>().CreateAsync(user);
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = options.TicketDataFormat.Protect(new AuthenticationTicket(principal,
            new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30) }, IdentityConstants.ApplicationScheme));
        using var http = factory.CreateClient(new() { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={ticket}");
        var response = await http.GetAsync("/compliance/work-register");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("action-bell workspace-action-menu", html);
        Assert.Contains("/images/bell.svg", html);
        Assert.DoesNotContain("href=\"/actions\"", html);
        var qa = Environment.GetEnvironmentVariable("KCAS_BELL_QA_DIR");
        if (!string.IsNullOrWhiteSpace(qa))
        {
            Directory.CreateDirectory(qa);
            var password = "Synthetic!" + Guid.NewGuid().ToString("N");
            Assert.True((await users.AddPasswordAsync(user, password)).Succeeded);
            await File.WriteAllTextAsync(Path.Combine(qa, "synthetic-browser.json"), JsonSerializer.Serialize(new
                { ClientId = client.Id, Email = user.Email, Password = password }));
        }
    }
}
