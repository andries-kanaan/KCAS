using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientAdvicePreparationServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Request_creates_one_real_draft_and_pending_task_then_refreshes_the_handoff()
    {
        using var scope = factory.Services.CreateScope();
        var adviser = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var officer = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var client = await ClientAsync(scope);
        var service = scope.ServiceProvider.GetRequiredService<ClientAdvicePreparationService>();
        var edit = new AdvicePreparationEdit { Situation = "Review income and propose a 4% increase.", Reference = "Synthetic contact record" };
        var receipt = await service.RequestAsync(client.Id, null, edit, adviser);
        var again = await service.RequestAsync(client.Id, null, edit, adviser);
        Assert.Equal(receipt.CaseId, again.CaseId);
        var page = await service.LoadAsync(client.Id, receipt.CaseId, adviser);
        Assert.Equal(ClientAdviceStatuses.Draft, page.Case!.Status);
        Assert.Equal(ClientAdviceMethodologies.KcasEvidenced, page.Case.RiskMethodologyCode);
        Assert.Empty(page.Case.Approvals);
        Assert.Contains("Review income and propose a 4% increase.", page.Task!.Description);
        Assert.Contains("Do not run or overwrite evidence scans", page.Task.Description);
        Assert.Contains(await service.NotificationsAsync(officer), x => x.CaseId == receipt.CaseId);
        Assert.Empty(await service.NotificationsAsync(adviser));
        var previousVersion = page.Request!.Version;
        edit.ExpectedVersion = previousVersion;
        edit.Situation = "Review income; keep a fixed local cash payment.";
        await service.RequestAsync(client.Id, receipt.CaseId, edit, adviser);
        var refreshed = await service.LoadAsync(client.Id, receipt.CaseId, adviser);
        Assert.Equal(page.Task.Id, refreshed.Task!.Id);
        Assert.Contains(edit.Situation, refreshed.Task.Description);
        Assert.NotEqual(previousVersion, refreshed.Request!.Version);
        await Assert.ThrowsAsync<ValidationException>(() => service.RequestAsync(client.Id, receipt.CaseId, edit, adviser));
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordResultsAsync(client.Id, receipt.CaseId, previousVersion, "Stale completion", officer));
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.ClientAdviceCases.CountAsync(x => x.ClientId == client.Id));
        Assert.Equal(1, await db.ComplianceTasks.CountAsync(x => x.ClientId == client.Id && x.TaskType == ComplianceTaskTypes.AdvicePreparation));
        Assert.Equal(2, await db.ComplianceAuditEvents.CountAsync(x => x.EntityType == nameof(ClientAdviceCase) && x.EntityId == receipt.CaseId && x.Action == "AdviceCodexPreparationRequested"));
    }

    [Fact]
    public async Task Results_need_supported_draft_and_coordinator_and_leave_independent_review_pending()
    {
        using var scope = factory.Services.CreateScope();
        var adviser = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var officer = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var client = await ClientAsync(scope);
        var service = scope.ServiceProvider.GetRequiredService<ClientAdvicePreparationService>();
        var receipt = await service.RequestAsync(client.Id, null, new() { Situation = "Review current retirement income." }, adviser);
        var page = await service.LoadAsync(client.Id, receipt.CaseId, adviser);
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordResultsAsync(client.Id, receipt.CaseId, page.Request!.Version, "No draft saved", officer));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RecordResultsAsync(client.Id, receipt.CaseId, page.Request!.Version, "Not the coordinator", adviser));
        var advice = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
        var draft = await advice.LoadCaseAsync(receipt.CaseId);
        draft.RecommendationSummary = "Retain the supported income pending the client's genuine missing facts.";
        draft.FactSources.Add(new() { FactName = "Current income", DocumentPath = "Synthetic dated statement", SourceDate = DateOnly.FromDateTime(DateTime.Today) });
        await advice.SaveDraftAsync(draft, officer.Identity!.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => advice.SubmitForReviewAsync(receipt.CaseId, adviser.Identity!.Name));
        await service.RecordResultsAsync(client.Id, receipt.CaseId, page.Request!.Version, "Supported draft saved; outstanding risk answers remain for the adviser.", officer);
        var recorded = await service.LoadAsync(client.Id, receipt.CaseId, adviser);
        Assert.Equal(ComplianceStatuses.Closed, recorded.Task!.Status);
        Assert.Equal(ClientAdviceStatuses.Draft, recorded.Case!.Status);
        Assert.Null(recorded.Case.ApprovedAtUtc);
        Assert.NotEmpty(recorded.Blockers);
        Assert.Contains("outstanding", recorded.Task.ClosureReason);
        Assert.DoesNotContain(await service.NotificationsAsync(officer), x => x.CaseId == receipt.CaseId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => advice.SubmitForReviewAsync(receipt.CaseId, adviser.Identity!.Name));
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = await db.ComplianceAuditEvents.SingleAsync(x => x.EntityType == nameof(ClientAdviceCase) && x.EntityId == receipt.CaseId && x.Action == "AdviceCodexPreparationRecorded");
        Assert.Equal(officer.Identity.Name, audit.UserName);
        Assert.Contains("\"performedBy\":\"Codex\"", audit.NewValueJson);
    }

    [Fact]
    public async Task Generic_task_closure_cannot_replace_preparation_and_changed_methodology_needs_a_new_brief()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var client = await ClientAsync(scope);
        var service = scope.ServiceProvider.GetRequiredService<ClientAdvicePreparationService>();
        var receipt = await service.RequestAsync(client.Id, null, new() { Situation = "Prepare an annual review." }, actor);
        var page = await service.LoadAsync(client.Id, receipt.CaseId, actor);
        await Assert.ThrowsAsync<ValidationException>(() => scope.ServiceProvider.GetRequiredService<ComplianceWorkService>()
            .RequestClosureAsync(page.Task!.Id, "Acknowledge", "Done", "Close", actor.Identity!.Name, "Generic action"));
        var advice = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
        var draft = await advice.LoadCaseAsync(receipt.CaseId);
        draft.RiskMethodologyCode = ClientAdviceMethodologies.PreviousFormCorrectedBands;
        await advice.SaveDraftAsync(draft, actor.Identity!.Name);
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordResultsAsync(client.Id, receipt.CaseId, page.Request!.Version, "Wrong methodology", actor));
    }

    [Fact]
    public async Task Restricted_client_and_party_tasks_are_visible_only_to_current_approved_admin_coordinators()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var officer = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var client = await ClientAsync(scope, hidden: true);
        var service = scope.ServiceProvider.GetRequiredService<ClientAdvicePreparationService>();
        var receipt = await service.RequestAsync(client.Id, null, new() { Situation = "Restricted request." }, actor);
        Assert.Contains(await service.NotificationsAsync(actor), x => x.CaseId == receipt.CaseId);
        Assert.DoesNotContain(await service.NotificationsAsync(officer), x => x.CaseId == receipt.CaseId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoadAsync(client.Id, receipt.CaseId, officer));
        var page = await service.LoadAsync(client.Id, receipt.CaseId, actor);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scope.ServiceProvider.GetRequiredService<ComplianceWorkService>().LoadAsync(page.Task!.Id, officer));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Assert.True((await users.RemoveFromRoleAsync(user!, KcasRoles.ComplianceAdministrator)).Succeeded);
        Assert.DoesNotContain(await service.NotificationsAsync(actor), x => x.CaseId == receipt.CaseId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RecordResultsAsync(client.Id, receipt.CaseId, page.Request!.Version, "Revoked role", actor));
    }

    [Fact]
    public async Task Supported_preparation_proceeds_to_independent_review_and_neither_preparer_can_approve()
    {
        using var scope = factory.Services.CreateScope();
        var adviser = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var officer = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceAdministrator);
        var reviewer = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var client = await ClientAsync(scope);
        var preparation = scope.ServiceProvider.GetRequiredService<ClientAdvicePreparationService>();
        var receipt = await preparation.RequestAsync(client.Id, null, new() { Situation = "Review the existing portfolio." }, adviser);
        var page = await preparation.LoadAsync(client.Id, receipt.CaseId, adviser);
        var advice = scope.ServiceProvider.GetRequiredService<ClientAdviceService>();
        await advice.SaveDraftAsync(ClientAdviceServiceTests.Complete(await advice.LoadCaseAsync(receipt.CaseId)), officer.Identity!.Name);
        await preparation.RecordResultsAsync(client.Id, receipt.CaseId, page.Request!.Version, "Actual synthetic sources reviewed, deterministic risk answers saved and CAR prepared.", officer);
        Assert.Empty((await preparation.LoadAsync(client.Id, receipt.CaseId, adviser)).Blockers);
        await advice.SubmitForReviewAsync(receipt.CaseId, adviser.Identity!.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => advice.ApproveAsync(receipt.CaseId, adviser.Identity.Name, "Self approval"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => advice.ApproveAsync(receipt.CaseId, officer.Identity.Name, "Preparation account approval"));
        await advice.ApproveAsync(receipt.CaseId, reviewer.Identity!.Name, "Independent review of the supported proposal.");
        Assert.Equal(ClientAdviceStatuses.ApprovedForIssue, (await advice.LoadCaseAsync(receipt.CaseId)).Status);
    }

    [Fact]
    public async Task Transferred_request_audit_cannot_replace_current_local_task_provenance()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var client = await ClientAsync(scope);
        var service = scope.ServiceProvider.GetRequiredService<ClientAdvicePreparationService>();
        var receipt = await service.RequestAsync(client.Id, null, new() { Situation = "Local request." }, actor);
        var page = await service.LoadAsync(client.Id, receipt.CaseId, actor);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ComplianceAuditEvents.Add(new() { EntityType = nameof(ClientAdviceCase), EntityId = receipt.CaseId,
            Action = "AdviceCodexPreparationRequested", UserName = "Source account", Reason = "Transferred source history",
            NewValueJson = System.Text.Json.JsonSerializer.Serialize(new AdvicePreparationRequest { CaseId = receipt.CaseId, TaskId = page.Task!.Id,
                Situation = "Source request with colliding IDs", Version = Guid.NewGuid().ToString("N") }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) });
        await db.SaveChangesAsync();
        var loaded = await service.LoadAsync(client.Id, receipt.CaseId, actor);
        Assert.Equal(page.Request!.Version, loaded.Request!.Version);
        Assert.Equal("Local request.", loaded.Request.Situation);
    }

    [Fact]
    public async Task View_only_and_restricted_client_requests_fail_without_creating_an_advice_case()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var reader = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ReadOnly);
        var client = await ClientAsync(scope, hidden: true);
        var service = scope.ServiceProvider.GetRequiredService<ClientAdvicePreparationService>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestAsync(client.Id, null, new() { Situation = "Cannot prepare" }, reader));
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.ClientAdviceCases.AnyAsync(x => x.ClientId == client.Id));
        // A hidden client needs an administrator coordinator; regular advisers cannot access it.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestAsync(client.Id, null, new() { Situation = "Restricted request" }, actor));
    }

    private static async Task<Client> ClientAsync(IServiceScope scope, bool hidden = false)
    {
        var label = "Synthetic advice preparation " + Guid.NewGuid().ToString("N");
        var item = new Client { DisplayName = label, SurnameOrEntityName = label, KanaanId = "TEST", ClientFolder = "Synthetic authorised folder", ExcludeFromComplianceLists = hidden };
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Clients.Add(item); await db.SaveChangesAsync(); return item;
    }
}
