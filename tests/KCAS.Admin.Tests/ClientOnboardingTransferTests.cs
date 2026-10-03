using System.Security.Claims;
using System.Text.Json;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientOnboardingTransferTests(KcasWebApplicationFactory factory)
{
    private const string Password = "synthetic-transfer";

    [Fact]
    public async Task Completed_source_transfers_findings_and_original_KI_history_then_allows_live_confirmation()
    {
        using var scope = factory.Services.CreateScope();
        var source = await SourceAsync(scope, accepted: true);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transfer = scope.ServiceProvider.GetRequiredService<ClientReviewTransferService>();
        var onboarding = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        var originalDecision = await db.ClientAcceptanceDecisions.AsNoTracking().SingleAsync(x => x.ClientId == source.ClientId);
        var export = await transfer.ExportAsync(source.ClientId, Password, "source@example.test", "Synthetic completed acceptance.");
        var bytes = await File.ReadAllBytesAsync(export.StoragePath);
        var liveId = await LiveAsync(scope, source.ClientId);
        var preview = await transfer.PreviewAsync(bytes, Password);
        Assert.Equal(liveId, preview.TargetClientId);
        Assert.True(preview.CanApply, string.Join("; ", preview.Conflicts));
        Assert.Single(preview.Package.Onboarding!.Decisions);
        await transfer.ApplyAsync(bytes, Password, "live-importer@example.test", "Apply actual source findings/history.");
        var live = await onboarding.LoadAsync(liveId, source.Actor);
        Assert.True(live.IsReady, string.Join("; ", live.CheckBlockers.Concat(live.IntakeBlockers)));
        Assert.False(live.IsAccepted);
        Assert.Equal("ResultsRecorded", live.Request!.Status);
        Assert.Equal(live.ContentHash, live.Request.CompletedContentHash);
        Assert.Equal("Synthetic supported Codex findings.", live.Request.CompletionSummary);
        Assert.Equal("Synthetic v1", live.Profile!.DisclosureVersion);
        Assert.Equal(originalDecision.SnapshotJson, live.Decisions.Single().SnapshotJson);
        Assert.Equal(originalDecision.DecidedAtUtc, live.Decisions.Single().DecidedAtUtc);
        Assert.NotNull(live.Decisions.Single().ImportSourceReference);
        Assert.Equal("live-importer@example.test", live.ImportedReview!.ImportedBy);
        Assert.All(live.ImportedReview.EvidenceIds.Values, id => Assert.Contains(live.Evidence.EvidenceItems, x => x.Id == id));
        Assert.DoesNotContain(live.ImportedReview.EvidenceIds.Values, id => preview.Package.Onboarding.EvidenceReferences.ContainsKey(id));
        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() => ClientOnboardingService.RequireAcceptedAsync(db, liveId));
        var liveKi = await KiAsync(scope);
        await onboarding.DecideAsync(liveId, live.ContentHash, "Accepted", "Review source decision and unchanged current live findings.", liveKi);
        Assert.True((await onboarding.LoadAsync(liveId, source.Actor)).IsAccepted);
        await ClientOnboardingService.RequireAcceptedAsync(db, liveId);
        Assert.Equal(2, await db.ClientAcceptanceDecisions.CountAsync(x => x.ClientId == liveId));
        var reexport = await transfer.ExportAsync(liveId, Password, "live-exporter@example.test", "Retain original source history on re-export.");
        var forwarded = (await transfer.PreviewAsync(await File.ReadAllBytesAsync(reexport.StoragePath), Password)).Package.Onboarding!;
        Assert.Equal(2, forwarded.Decisions.Count);
        var original = preview.Package.Onboarding.Decisions.Single();
        var retained = forwarded.Decisions.Single(x => x.SourceKey == original.SourceKey);
        Assert.Equal(original.OriginEnvironment, retained.OriginEnvironment);
        Assert.Equal(original.DecidedBy, retained.DecidedBy);
        Assert.Equal(original.SnapshotJson, retained.SnapshotJson);
        Assert.Equal(original.SnapshotSha256, retained.SnapshotSha256);
    }

    [Fact]
    public async Task Partial_source_retains_gaps_and_assigns_a_real_live_task()
    {
        using var scope = factory.Services.CreateScope();
        var source = await SourceAsync(scope, partial: true);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transfer = scope.ServiceProvider.GetRequiredService<ClientReviewTransferService>();
        var export = await transfer.ExportAsync(source.ClientId, Password, "source@example.test", "Synthetic partial acceptance.", includePartial: true);
        var liveId = await LiveAsync(scope, source.ClientId);
        var liveActor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        (await db.Users.SingleAsync(x => x.Id == source.Actor.FindFirstValue(ClaimTypes.NameIdentifier))).IsApproved = false;
        await db.SaveChangesAsync();
        await transfer.ApplyAsync(await File.ReadAllBytesAsync(export.StoragePath), Password, "live@example.test", "Retain partial work.");
        var service = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        var model = await service.LoadAsync(liveId, liveActor);
        Assert.False(model.IsReady);
        Assert.False(model.IsAccepted);
        Assert.Equal("AwaitingCodex", model.Request!.Status);
        Assert.Null(model.Request.CompletedContentHash);
        Assert.Contains(liveActor.FindFirstValue(ClaimTypes.NameIdentifier)!, JsonSerializer.Deserialize<string[]>(model.Request.RecipientUserIdsJson)!);
        Assert.Contains(await service.NotificationsAsync(liveActor), x => x.ClientId == liveId);
        Assert.Single(model.ImportedReview!.Source.Reviews);
        Assert.Empty(model.Decisions);
    }

    [Fact]
    public async Task New_source_revision_preserves_history_and_later_live_edits_are_blocked()
    {
        using var scope = factory.Services.CreateScope();
        var source = await SourceAsync(scope, accepted: true);
        var transfer = scope.ServiceProvider.GetRequiredService<ClientReviewTransferService>();
        var onboarding = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        var export = await transfer.ExportAsync(source.ClientId, Password, "source@example.test", "First synthetic revision.");
        var bytes = await File.ReadAllBytesAsync(export.StoragePath);
        var liveId = await LiveAsync(scope, source.ClientId);
        await transfer.ApplyAsync(bytes, Password, "live@example.test", "First import.");
        Assert.True((await transfer.PreviewAsync(bytes, Password)).AlreadyApplied);
        var package = (await transfer.PreviewAsync(bytes, Password)).Package;
        package.PackageId = Guid.NewGuid().ToString("N"); package.CreatedAtUtc = DateTime.UtcNow;
        package.Onboarding!.Preparation!.Version = Guid.NewGuid().ToString("N");
        package.Onboarding.Preparation.DisclosureVersion = "Synthetic revised disclosure";
        package.Onboarding.CurrentResultsValidated = false;
        package.Onboarding.RevisionHash = ClientOnboardingTransfer.RevisionHash(package.Onboarding);
        var newer = Encrypt(package);
        Assert.True((await transfer.PreviewAsync(newer, Password)).CanApply);
        await transfer.ApplyAsync(newer, Password, "live@example.test", "Apply clean source revision.");
        var live = await onboarding.LoadAsync(liveId, source.Actor);
        Assert.Single(live.Decisions);
        Assert.Equal("Synthetic revised disclosure", live.Profile!.DisclosureVersion);
        await onboarding.SavePreparationAsync(liveId, new() { Version = live.Profile.Version, RequestedService = live.Profile.RequestedService,
            ResponsibleRepresentative = "Actual live edit", PurposeAndProposedFunds = live.Profile.PurposeAndProposedFunds,
            DisclosureVersion = live.Profile.DisclosureVersion, DisclosureDeliveredAtUtc = live.Profile.DisclosureDeliveredAtUtc,
            DisclosureDeliveryReference = live.Profile.DisclosureDeliveryReference }, "Locally recorded preparation.", source.Actor);
        package.PackageId = Guid.NewGuid().ToString("N"); package.CreatedAtUtc = DateTime.UtcNow;
        var conflict = await transfer.PreviewAsync(Encrypt(package), Password);
        Assert.False(conflict.CanApply);
        Assert.Contains(conflict.Conflicts, x => x.Contains("changed after the previous import"));
        Assert.Equal("Actual live edit", (await onboarding.LoadAsync(liveId, source.Actor)).Profile!.ResponsibleRepresentative);
    }

    [Theory]
    [InlineData("Snapshot")]
    [InlineData("Evidence")]
    public async Task Invalid_source_snapshot_or_missing_support_is_rejected_at_preview(string failure)
    {
        using var scope = factory.Services.CreateScope();
        var source = await SourceAsync(scope, accepted: true);
        var transfer = scope.ServiceProvider.GetRequiredService<ClientReviewTransferService>();
        var export = await transfer.ExportAsync(source.ClientId, Password, "source@example.test", "Source integrity fixture.");
        var package = (await transfer.PreviewAsync(await File.ReadAllBytesAsync(export.StoragePath), Password)).Package;
        await LiveAsync(scope, source.ClientId);
        if (failure == "Snapshot") package.Onboarding!.Decisions[0].SnapshotJson = "{}";
        else package.Onboarding!.EvidenceReferences[1] = "missing-evidence";
        package.Onboarding!.RevisionHash = ClientOnboardingTransfer.RevisionHash(package.Onboarding);
        var preview = await transfer.PreviewAsync(Encrypt(package), Password);
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Conflicts, x => x.Contains(failure == "Snapshot" ? "source provenance" : "Acceptance evidence references"));
    }

    [Fact]
    public async Task First_import_cannot_replace_native_acceptance_work()
    {
        using var scope = factory.Services.CreateScope();
        var source = await SourceAsync(scope);
        var transfer = scope.ServiceProvider.GetRequiredService<ClientReviewTransferService>();
        var export = await transfer.ExportAsync(source.ClientId, Password, "source@example.test", "Source preparation.");
        var liveId = await LiveAsync(scope, source.ClientId);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.ClientOnboardingProfiles.Add(new() { ClientId = liveId, RequestedService = "Native live service", UpdatedBy = "live@example.test" });
        await db.SaveChangesAsync();
        var preview = await transfer.PreviewAsync(await File.ReadAllBytesAsync(export.StoragePath), Password);
        Assert.False(preview.CanApply);
        Assert.Contains(preview.Conflicts, x => x.Contains("locally recorded acceptance work"));
        Assert.Equal("Native live service", (await db.ClientOnboardingProfiles.SingleAsync(x => x.ClientId == liveId)).RequestedService);
    }

    private static byte[] Encrypt(ClientReviewPackage package) => ClientReviewTransferService.Encrypt(JsonSerializer.SerializeToUtf8Bytes(package, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Password);

    private static async Task<(int ClientId, ClaimsPrincipal Actor)> SourceAsync(IServiceScope scope, bool accepted = false, bool partial = false)
    {
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator, KcasRoles.ComplianceAdministrator);
        var id = await ClientOnboardingServiceTests.ReadyAsync(scope, actor);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var client = await db.Clients.SingleAsync(x => x.Id == id);
        client.LegacyClientId = 1000000 + id; client.LifecycleStatus = ClientLifecycleStatuses.Historical;
        client.LifecycleReviewedAtUtc = DateTime.UtcNow; client.LifecycleReason = "Synthetic history; no investment data.";
        if (partial) (await db.ClientRiskAssessments.SingleAsync(x => x.ClientId == id)).Status = ClientRiskAssessmentStatuses.Draft;
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<ClientOnboardingService>();
        await service.RequestCodexAsync(id, "Synthetic actual review scope.", actor);
        if (!partial) await service.ValidateRecordedResultsAsync(id, actor, "Synthetic supported Codex findings.");
        if (accepted) await service.DecideAsync(id, (await service.LoadAsync(id, actor)).ContentHash, "Accepted", "Actual synthetic source KI decision.", await KiAsync(scope));
        return (id, actor);
    }

    private static async Task<ClaimsPrincipal> KiAsync(IServiceScope scope)
    {
        var ki = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Advisor);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.GovernanceRoleAssignments.Add(new() { RoleType = "Key Individual", PersonName = "Synthetic KI", Email = ki.Identity!.Name, IsActive = true });
        await db.SaveChangesAsync();
        return ki;
    }

    private static async Task<int> LiveAsync(IServiceScope scope, int sourceId)
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = await db.Clients.SingleAsync(x => x.Id == sourceId);
        var live = new Client { LegacyClientId = source.LegacyClientId, KanaanId = source.KanaanId, FullName = source.FullName,
            DisplayName = source.DisplayName, SurnameOrEntityName = source.SurnameOrEntityName, ClientCategory = source.ClientCategory };
        source.LegacyClientId = null; source.KanaanId = null;
        await db.SaveChangesAsync();
        db.Clients.Add(live); await db.SaveChangesAsync();
        return live.Id;
    }
}
