using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientOnboardingServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Saved_prospect_is_pending_and_task_closure_without_results_does_not_clear_it()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        var actor = await ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var id = await scope.ServiceProvider.GetRequiredService<ClientOperationsService>().SaveClientAsync(new()
        { DisplayName = "Onboarding test " + Guid.NewGuid(), SurnameOrEntityName = "Synthetic prospect" });
        Assert.True((await db.Clients.AsNoTracking().SingleAsync(x => x.Id == id)).RequiresClientAcceptance);
        await service.RequestCodexAsync(id, "Initial check", actor);
        await service.RequestCodexAsync(id, "Duplicate visit", actor);
        var page = await service.LoadAsync(id, actor);
        Assert.Equal("Awaiting Codex review", page.Status);
        Assert.Equal(1, await db.ClientCodexReviewRequests.CountAsync(x => x.ClientId == id));
        Assert.Contains(await service.NotificationsAsync(actor), x => x.ClientId == id);
        var task = await db.ComplianceTasks.SingleAsync(x => x.Id == page.Request!.ComplianceTaskId);
        task.Status = ComplianceStatuses.Closed;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => service.ValidateRecordedResultsAsync(id, actor));
        await Assert.ThrowsAsync<ValidationException>(() => ClientOnboardingService.RequireAcceptedAsync(db, id));
        await Assert.ThrowsAsync<ValidationException>(() => scope.ServiceProvider.GetRequiredService<ClientOperationsService>()
            .SaveInvestmentAccountAsync(new() { ClientId = id, AccountNumber = "TEST-PENDING" }, "test"));
        Assert.False((await service.LoadAsync(id, actor)).IsAccepted);
    }

    [Fact]
    public async Task Supported_results_need_an_actual_KI_and_fresh_decision_snapshot()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        var actor = await ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var ki = await ActorAsync(scope, KcasRoles.Advisor);
        db.GovernanceRoleAssignments.Add(new() { RoleType = "Key Individual", PersonName = "Synthetic KI", Email = ki.Identity!.Name, IsActive = true });
        await db.SaveChangesAsync();
        var id = await ReadyAsync(scope, actor);
        await service.RequestCodexAsync(id, "Supported checks", actor);
        var page = await service.LoadAsync(id, actor);
        Assert.Empty(page.CheckBlockers);
        Assert.Empty(page.IntakeBlockers);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.DecideAsync(id, page.ContentHash, "Accepted", "Not a KI", actor));
        await Assert.ThrowsAsync<ValidationException>(() => service.DecideAsync(id, page.ContentHash, "Accepted", "Premature", ki));
        await service.ValidateRecordedResultsAsync(id, actor, "Supported synthetic review findings; actual KI decision remains pending.");
        var ready = await service.LoadAsync(id, ki);
        Assert.Equal("Ready for KI decision", ready.Status);
        Assert.Equal("Supported synthetic review findings; actual KI decision remains pending.", ready.Request!.CompletionSummary);
        var evidence = await db.ClientEvidenceItems.FirstAsync(x => x.ClientId == id && x.EvidenceType == "PepPip");
        evidence.Notes += " Updated source interpretation.";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ValidationException>(() => service.DecideAsync(id, ready.ContentHash, "Accepted", "Stale screen", ki));
        var changed = await service.LoadAsync(id, ki);
        await Assert.ThrowsAsync<ValidationException>(() => service.DecideAsync(id, changed.ContentHash, "Accepted", "Results need revalidation", ki));
        await service.ValidateRecordedResultsAsync(id, actor);
        ready = await service.LoadAsync(id, ki);
        Assert.Equal("Supported synthetic review findings; actual KI decision remains pending.", ready.Request!.CompletionSummary);
        await service.DecideAsync(id, ready.ContentHash, "Accepted", "Reviewed supported checks and proposed service", ki);
        Assert.True((await service.LoadAsync(id, ki)).IsAccepted);
        await ClientOnboardingService.RequireAcceptedAsync(db, id);
        var decision = await db.ClientAcceptanceDecisions.AsNoTracking().SingleAsync(x => x.ClientId == id && x.Decision == "Accepted");
        Assert.Equal(ki.FindFirstValue(ClaimTypes.NameIdentifier), decision.DecidedByUserId);
        Assert.Contains("screeningSources", decision.SnapshotJson);
        // Identity changes are not silently covered by the earlier acceptance.
        var client = await db.Clients.SingleAsync(x => x.Id == id);
        client.FullName += " changed";
        await db.SaveChangesAsync();
        Assert.False((await service.LoadAsync(id, ki)).IsAccepted);
        await Assert.ThrowsAsync<ValidationException>(() => ClientOnboardingService.RequireAcceptedAsync(db, id));
    }

    [Fact]
    public async Task Validated_check_results_do_not_close_handoff_while_disclosure_preparation_is_missing()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        var actor = await ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var id = await ReadyAsync(scope, actor);
        var profile = await db.ClientOnboardingProfiles.SingleAsync(x => x.ClientId == id);
        profile.DisclosureDeliveredAtUtc = null;
        await db.SaveChangesAsync();
        await service.RequestCodexAsync(id, "Research outstanding disclosures", actor);
        var error = await Assert.ThrowsAsync<ValidationException>(() => service.ValidateRecordedResultsAsync(id, actor));
        Assert.Contains("disclosures", error.Message);
        Assert.Equal("AwaitingCodex", (await service.LoadAsync(id, actor)).Request!.Status);
    }

    [Fact]
    public async Task Scope_change_unchecked_party_and_possible_sanctions_match_remain_blocked()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        var actor = await ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var id = await ReadyAsync(scope, actor);
        await service.RequestCodexAsync(id, "Initial scope", actor);
        var client = await db.Clients.SingleAsync(x => x.Id == id);
        client.RelatedParties.Add(new() { DisplayName = "Unchecked controller", ControlBasis = "New control", IsActive = true });
        await db.SaveChangesAsync();
        var page = await service.LoadAsync(id, actor);
        Assert.Contains(page.CheckBlockers, x => x.Contains("Unchecked controller"));
        Assert.Contains(page.CheckBlockers, x => x.Contains("scope changed"));
        await Assert.ThrowsAsync<ValidationException>(() => service.ValidateRecordedResultsAsync(id, actor));
        await service.RequestCodexAsync(id, "New party scope", actor);
        Assert.Equal(2, await db.ClientCodexReviewRequests.CountAsync(x => x.ClientId == id));
        var screening = await db.ClientEvidenceItems.SingleAsync(x => x.ClientId == id && x.EvidenceType == "SanctionsTfs");
        screening.ScreeningOutcome = ClientEvidenceScreeningOutcomes.PossibleMatch;
        await db.SaveChangesAsync();
        page = await service.LoadAsync(id, actor);
        Assert.Contains(page.CheckBlockers, x => x.Contains("resolve the SanctionsTfs match"));
        await Assert.ThrowsAsync<ValidationException>(() => service.ValidateRecordedResultsAsync(id, actor));
    }

    [Fact]
    public async Task Existing_relationships_are_not_reset_and_permissions_are_checked_from_current_records()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        var actor = await ActorAsync(scope, KcasRoles.Administrator);
        var client = new Client { DisplayName = "Existing " + Guid.NewGuid(), SurnameOrEntityName = "Existing", LifecycleStatus = ClientLifecycleStatuses.Historical };
        db.Clients.Add(client); await db.SaveChangesAsync();
        await ClientOnboardingService.RequireAcceptedAsync(db, client.Id);
        Assert.Empty((await service.LoadAsync(client.Id, actor)).Decisions);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Assert.True((await users.RemoveFromRoleAsync(user!, KcasRoles.Administrator)).Succeeded);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoadAsync(client.Id, actor));
    }

    [Fact]
    public async Task Existing_client_can_request_full_Codex_preparation_without_manual_intake()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        var actor = await ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var client = new Client { DisplayName = "Existing full review " + Guid.NewGuid(), SurnameOrEntityName = "Synthetic existing client",
            ClientFolder = "Synthetic authorised evidence location", LifecycleStatus = ClientLifecycleStatuses.Historical };
        db.Clients.Add(client); await db.SaveChangesAsync();
        await service.RequestCodexAsync(client.Id, "Prepare review from the folder", actor);
        var model = await service.LoadAsync(client.Id, actor);
        Assert.Null(model.Profile);
        Assert.Equal(ClientLifecycleStatuses.Historical, model.Client.LifecycleStatus);
        Assert.True(model.Client.RequiresClientAcceptance);
        Assert.Equal("Awaiting Codex review", model.Status);
        Assert.Contains("Synthetic authorised evidence location", model.Request!.Brief);
        Assert.Contains("Missing preparation is part of this Codex task", model.Request.Brief);
        Assert.Contains("Preparation gaps:", model.Request.Brief);
        Assert.Contains("actual initial disclosures", model.Request.Brief);
        Assert.Contains("The authorised KI records acceptance", model.Request.Brief);
        Assert.Contains(await service.NotificationsAsync(actor), x => x.ClientId == client.Id);
        var request = await db.ClientCodexReviewRequests.Include(x => x.Task).SingleAsync(x => x.ClientId == client.Id);
        var requestedAt = request.CreatedAtUtc;
        request.Brief = request.Task.Description = "Earlier limited check brief";
        await db.SaveChangesAsync();
        await service.RequestCodexAsync(client.Id, "Same pending review", actor);
        Assert.Single(await db.ClientCodexReviewRequests.Where(x => x.ClientId == client.Id).ToListAsync());
        model = await service.LoadAsync(client.Id, actor);
        Assert.Equal(requestedAt, model.Request!.CreatedAtUtc);
        Assert.Contains("Missing preparation is part of this Codex task", model.Request.Brief);
        Assert.Empty(await db.ClientAcceptanceDecisions.Where(x => x.ClientId == client.Id).ToListAsync());
        await Assert.ThrowsAsync<ValidationException>(() => service.ValidateRecordedResultsAsync(client.Id, actor));
    }

    [Fact]
    public async Task Prospect_without_investments_can_generate_and_finalise_risk_and_enhanced_acceptance_uses_one_KI_decision()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        var actor = await ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var ki = await ActorAsync(scope, KcasRoles.Advisor);
        db.GovernanceRoleAssignments.Add(new() { RoleType = "Key Individual", PersonName = "Synthetic enhanced KI", Email = ki.Identity!.Name, IsActive = true });
        await db.SaveChangesAsync();
        var id = await ReadyAsync(scope, actor);
        var client = await db.Clients.SingleAsync(x => x.Id == id);
        client.LifecycleStatus = ClientLifecycleStatuses.Unreviewed;
        var seeded = await db.ClientRiskAssessments.SingleAsync(x => x.ClientId == id);
        seeded.Status = ClientRiskAssessmentStatuses.Superseded;
        await db.SaveChangesAsync();
        var risk = scope.ServiceProvider.GetRequiredService<ClientRiskAssessmentService>();
        Assert.True((await risk.LoadAsync(id)).IsReadyForRiskAssessment);
        var draftId = await risk.CreateDraftAsync(id, "Codex test", "Evidence-supported prospect");
        var page = await risk.LoadAsync(id);
        var response = Assert.Single(page.Factors);
        await risk.SaveDraftAsync(draftId, new ClientRiskAssessmentEditModel
        {
            Narrative = "Supported synthetic prospect", StandardControlsApplied = true,
            Factors = [new(response.FactorId, response.SelectedOptionId ?? response.Options.First().Id, null, "Actual synthetic evidence supports this factor.")]
        }, "Codex test", "Confirm supported factor");
        await risk.FinaliseAsync(draftId, "Codex test", "Finalise prospect");
        Assert.Equal(ClientLifecycleStatuses.Unreviewed, (await db.Clients.AsNoTracking().SingleAsync(x => x.Id == id)).LifecycleStatus);
        Assert.Empty(await db.ClientInvestmentAccounts.Where(x => x.ClientId == id).ToListAsync());
        var assessment = await db.ClientRiskAssessments.SingleAsync(x => x.Id == draftId);
        assessment.Status = ClientRiskAssessmentStatuses.PendingKiApproval; assessment.RequiresEdd = true;
        var profile = await db.ClientOnboardingProfiles.SingleAsync(x => x.ClientId == id);
        profile.EnhancedMeasures = "Documented enhanced measures and follow-up";
        await db.SaveChangesAsync();
        await service.RequestCodexAsync(id, "Enhanced preparation", actor);
        await service.ValidateRecordedResultsAsync(id, actor);
        var ready = await service.LoadAsync(id, ki);
        await service.DecideAsync(id, ready.ContentHash, "Accepted", "Accept client and enhanced measures", ki);
        Assert.True((await service.LoadAsync(id, ki)).IsAccepted);
        Assert.Single(await db.ClientRiskAssessmentApprovals.Where(x => x.ClientRiskAssessmentId == draftId).ToListAsync());
        Assert.Single(await db.ClientAcceptanceDecisions.Where(x => x.ClientId == id && x.Decision == "Accepted").ToListAsync());
    }

    [Fact]
    public async Task Authenticated_summary_is_read_only_and_manual_findings_follow_the_same_gate()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        var actor = await ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var ki = await ActorAsync(scope, KcasRoles.Advisor);
        db.GovernanceRoleAssignments.Add(new() { RoleType = "Key Individual", PersonName = "Synthetic browser KI", Email = ki.Identity!.Name, IsActive = true });
        await db.SaveChangesAsync();
        var id = await ReadyAsync(scope, actor);
        foreach (var check in await db.ClientEvidenceItems.Where(x => x.ClientId == id).ToListAsync())
        { check.ScreeningPerformedBy = actor.Identity!.Name; check.Reviewer = actor.Identity.Name; }
        await db.SaveChangesAsync();
        await service.RequestCodexAsync(id, "Equivalent actual manual checks", actor);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var principal = await scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<ApplicationUser>>()
            .CreateAsync((await users.FindByIdAsync(actor.FindFirstValue(ClaimTypes.NameIdentifier)!))!);
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var ticket = options.TicketDataFormat.Protect(new AuthenticationTicket(principal, new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(15) }, IdentityConstants.ApplicationScheme));
        using var http = factory.CreateClient(new() { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={ticket}");
        var response = await http.GetAsync($"/clients/{id}/onboarding");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Awaiting Codex review", html);
        Assert.Contains("Update preparation", html);
        Assert.Contains("Synthetic evidence findings", html);
        Assert.Contains("Validate recorded results", html);
        Assert.DoesNotContain("Record KI decision", html);
        await service.ValidateRecordedResultsAsync(id, actor);
        Assert.Equal("Ready for KI decision", (await service.LoadAsync(id, ki)).Status);
        var qa = Environment.GetEnvironmentVariable("KCAS_ONBOARDING_QA_DIR");
        if (!string.IsNullOrWhiteSpace(qa))
        {
            Directory.CreateDirectory(qa);
            const string password = "Synthetic-QA-Only!42";
            foreach (var reviewer in new[] { actor, ki })
                Assert.True((await users.AddPasswordAsync((await users.FindByIdAsync(reviewer.FindFirstValue(ClaimTypes.NameIdentifier)!))!, password)).Succeeded);
            await File.WriteAllTextAsync(Path.Combine(qa, "synthetic-browser.json"), JsonSerializer.Serialize(new
                { ClientId = id, OfficerEmail = actor.Identity!.Name, KiEmail = ki.Identity!.Name, Password = password }));
        }
    }

    [Fact]
    public async Task Changed_linked_file_cannot_be_validated_as_the_verified_document()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        var actor = await ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var id = await ReadyAsync(scope, actor);
        var path = Path.Combine(Path.GetTempPath(), $"kcas-onboarding-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllTextAsync(path, "Synthetic initial evidence");
            var item = await db.ClientEvidenceItems.FirstAsync(x => x.ClientId == id && x.EvidenceType == "Identity");
            item.SourcePath = path; item.FileName = Path.GetFileName(path);
            item.FileSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path)));
            await db.SaveChangesAsync();
            await service.RequestCodexAsync(id, "Current file evidence", actor);
            Assert.Empty((await service.LoadAsync(id, actor)).CheckBlockers);
            await File.WriteAllTextAsync(path, "Different evidence contents");
            Assert.Contains((await service.LoadAsync(id, actor)).CheckBlockers, x => x.Contains("changed since verification"));
            await Assert.ThrowsAsync<ValidationException>(() => service.ValidateRecordedResultsAsync(id, actor));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    internal static async Task<ClaimsPrincipal> ActorAsync(IServiceScope scope, params string[] roles)
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = "onboarding-test-" + Guid.NewGuid().ToString("N") + "@example.test";
        var user = new ApplicationUser { Email = email, UserName = email, IsApproved = true };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddToRolesAsync(user, roles)).Succeeded);
        return new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id), new Claim(ClaimTypes.Name, email)], "Test"));
    }

    internal static async Task<int> ReadyAsync(IServiceScope scope, ClaimsPrincipal actor)
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var evidence = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var service = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        var name = "Synthetic ready " + Guid.NewGuid().ToString("N");
        var client = new Client { FullName = name, DisplayName = name, SurnameOrEntityName = name, RequiresClientAcceptance = true, LifecycleStatus = ClientLifecycleStatuses.Current };
        db.Clients.Add(client); await db.SaveChangesAsync();
        var readiness = await evidence.LoadClientReadinessAsync(client.Id);
        foreach (var requirement in readiness.Requirements)
            db.ClientEvidenceItems.Add(new()
            {
                ClientId = client.Id, ClientEvidenceRequirementId = requirement.RequirementId, EvidenceType = requirement.EvidenceType,
                Title = "Synthetic supported " + requirement.Title, VerifiedDate = DateOnly.FromDateTime(DateTime.Today), ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
                Status = ClientEvidenceStatuses.Verified, OwnershipStatus = ClientEvidenceOwnershipStatuses.Confirmed, SelectionStatus = ClientEvidenceSelectionStatuses.Current,
                Reviewer = "Codex", Notes = "Synthetic evidence findings", ScreeningSubjectName = name, ScreeningSubjectType = ClientEvidenceScreeningSubjectTypes.Client,
                ScreeningPerformedBy = ClientEvidenceScreeningPerformers.Codex, ScreeningReviewedAtUtc = DateTime.UtcNow.AddMinutes(-1), ScreeningReviewDate = DateOnly.FromDateTime(DateTime.Today),
                ScreeningSources = "Synthetic official-source fixture", ScreeningOutcome = requirement.EvidenceType == "AdverseInformation" ? ClientEvidenceScreeningOutcomes.NoneFound : ClientEvidenceScreeningOutcomes.NoMatch
            });
        var methodology = new RiskMethodologyVersion { Name = name, VersionLabel = "v1", Status = ComplianceStatuses.Active };
        var factor = new RiskFactorDefinition { Code = "CLIENT", Name = "Client", Weight = 1 };
        var option = new RiskFactorOption { Code = "LOW", Label = "Low", Score = 1 }; factor.Options.Add(option);
        methodology.Factors.Add(factor); methodology.Bands.Add(new() { Name = "Low", MinimumScore = 0, MaximumScore = 3, ReviewMonths = 12 });
        db.RiskMethodologyVersions.Add(methodology);
        var assessment = new ClientRiskAssessment { ClientId = client.Id, MethodologyVersion = methodology, Status = ClientRiskAssessmentStatuses.Finalised,
            FinalRating = "Low", StandardControlsApplied = true, Narrative = "Synthetic supported findings", SnapshotJson = "{}", NextReviewDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)) };
        assessment.Responses.Add(new() { FactorDefinition = factor, SelectedOption = option, Explanation = "Supported test factor", ConfirmedBy = "Codex", ConfirmedAtUtc = DateTime.UtcNow });
        db.ClientRiskAssessments.Add(assessment); await db.SaveChangesAsync();
        await service.SavePreparationAsync(client.Id, new() { RequestedService = "Discretionary investment management", ResponsibleRepresentative = "Synthetic representative",
            PurposeAndProposedFunds = "Proposed contribution from evidenced savings", DisclosureVersion = "Synthetic v1", DisclosureDeliveredAtUtc = DateTime.UtcNow.AddMinutes(-5),
            DisclosureDeliveryReference = "Synthetic delivery proof" }, "Capture actual test preparation", actor);
        return client.Id;
    }
}
