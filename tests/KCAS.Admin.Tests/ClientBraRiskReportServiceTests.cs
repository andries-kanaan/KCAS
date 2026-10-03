using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using KCAS.Admin.Data;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientBraRiskReportServiceTests(KcasWebApplicationFactory factory)
{
    [Theory]
    [InlineData(1, 1, 1, "Low")]
    [InlineData(1, 2, 2, "Low")]
    [InlineData(1, 3, 3, "Moderate")]
    [InlineData(2, 2, 4, "Moderate")]
    [InlineData(2, 3, 6, "High")]
    [InlineData(3, 3, 9, "High")]
    public void Matrix_has_whole_number_inputs_and_explicit_bands(int likelihood, int impact, int score, string band)
    {
        Assert.Equal(score, ClientBraRiskMethod.Score(likelihood, impact));
        Assert.Equal(band, ClientBraRiskMethod.Rating(score));
        Assert.Null(ClientBraRiskMethod.Score(0, impact));
        Assert.Null(ClientBraRiskMethod.Score(likelihood, 4));
        Assert.Equal("Not assessed", ClientBraRiskMethod.Rating(5));
    }

    [Fact]
    public async Task Proposed_report_retains_history_performer_and_unchanged_formal_rating()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator);
        var id = await ClientOnboardingServiceTests.ReadyAsync(scope, actor);
        var service = scope.ServiceProvider.GetRequiredService<ClientBraRiskReportService>();
        var edit = Edit(await service.LoadAsync(id, actor));
        var firstId = await service.RecordAsync(id, edit, "Synthetic evidenced proposal.", actor);
        var saved = await service.LoadAsync(id, actor);
        Assert.True(saved.IsCurrent);
        Assert.Equal("Codex", saved.Report!.PerformedBy);
        Assert.NotEqual("Codex", saved.Report.RecordedBy);
        Assert.Equal("Low", saved.ClientReview.Assessment!.FinalRating);
        Assert.False(saved.ClientReview.IsAccepted);
        Assert.Equal(6, saved.Report.ReadContent().Scenarios[0].InherentScore);
        Assert.Equal(3, saved.Report.ReadContent().Scenarios[0].ResidualScore);
        var firstJson = saved.Report.ContentJson;
        var next = Edit(saved);
        next.Content.Limitations = "Updated synthetic limitation.";
        await service.RecordAsync(id, next, "Update justified proposal.", actor);
        var latest = await service.LoadAsync(id, actor);
        Assert.Equal(2, latest.History.Count);
        Assert.Equal(firstJson, latest.History.Single(x => x.Id == firstId).ContentJson);
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordAsync(id, next, "Stale writer.", actor));
    }

    [Fact]
    public async Task Changed_checks_mark_report_stale_and_cannot_be_saved_from_an_old_page()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator);
        var id = await ClientOnboardingServiceTests.ReadyAsync(scope, actor);
        var service = scope.ServiceProvider.GetRequiredService<ClientBraRiskReportService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await service.RecordAsync(id, Edit(await service.LoadAsync(id, actor)), "Synthetic initial proposal.", actor);
        var before = await service.LoadAsync(id, actor);
        var edit = Edit(before);
        var item = await db.ClientEvidenceItems.FirstAsync(x => x.ClientId == id);
        item.Notes += " Material finding changed.";
        await db.SaveChangesAsync();
        Assert.False((await service.LoadAsync(id, actor)).IsCurrent);
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordAsync(id, edit, "Old checks.", actor));
    }

    [Theory]
    [InlineData("MissingReason")]
    [InlineData("ImpactWithoutBasis")]
    [InlineData("ForeignEvidence")]
    [InlineData("ExpiredEvidence")]
    [InlineData("UnresolvedScreen")]
    [InlineData("RepeatedRisk")]
    public async Task Unsupported_or_incomplete_judgements_are_rejected(string failure)
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator);
        var id = await ClientOnboardingServiceTests.ReadyAsync(scope, actor);
        var service = scope.ServiceProvider.GetRequiredService<ClientBraRiskReportService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (failure is "ExpiredEvidence" or "UnresolvedScreen")
        {
            var item = await db.ClientEvidenceItems.FirstAsync(x => x.ClientId == id && x.EvidenceType == "Identity");
            if (failure == "ExpiredEvidence") item.ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
            else item.EscalationRequired = true;
            await db.SaveChangesAsync();
        }
        var edit = Edit(await service.LoadAsync(id, actor));
        if (failure == "MissingReason") edit.Content.Scenarios[0].InherentLikelihoodReason = "";
        if (failure == "ImpactWithoutBasis") edit.Content.Scenarios[0].ResidualImpact = 1;
        if (failure == "RepeatedRisk") edit.Content.Scenarios[1].RiskType = "ML";
        if (failure == "ForeignEvidence")
        {
            var other = await ClientOnboardingServiceTests.ReadyAsync(scope, actor);
            edit.Content.Scenarios[0].Evidence[0].EvidenceItemId = await db.ClientEvidenceItems.Where(x => x.ClientId == other).Select(x => x.Id).FirstAsync();
        }
        await Assert.ThrowsAsync<ValidationException>(() => service.RecordAsync(id, edit, "Unsupported proposal.", actor));
        Assert.Empty((await service.LoadAsync(id, actor)).History);
    }

    [Fact]
    public async Task Read_only_user_cannot_record_and_hidden_client_remains_restricted()
    {
        using var scope = factory.Services.CreateScope();
        var admin = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator);
        var viewer = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.ComplianceReadOnly);
        var id = await ClientOnboardingServiceTests.ReadyAsync(scope, admin);
        var service = scope.ServiceProvider.GetRequiredService<ClientBraRiskReportService>();
        var edit = Edit(await service.LoadAsync(id, admin));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RecordAsync(id, edit, "Wrong role.", viewer));
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Clients.SingleAsync(x => x.Id == id)).ExcludeFromComplianceLists = true;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoadAsync(id, viewer));
    }

    [Fact]
    public async Task Partial_transfer_remaps_report_evidence_and_retains_source_provenance_without_live_clearance()
    {
        using var scope = factory.Services.CreateScope();
        var actor = await ClientOnboardingServiceTests.ActorAsync(scope, KcasRoles.Administrator);
        var id = await ClientOnboardingServiceTests.ReadyAsync(scope, actor);
        var reports = scope.ServiceProvider.GetRequiredService<ClientBraRiskReportService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transfers = scope.ServiceProvider.GetRequiredService<ClientReviewTransferService>();
        var client = await db.Clients.SingleAsync(x => x.Id == id);
        client.LegacyClientId = 1000000 + id;
        client.LifecycleStatus = ClientLifecycleStatuses.Historical;
        client.LifecycleReviewedAtUtc = DateTime.UtcNow;
        client.LifecycleReason = "Synthetic historical transfer test.";
        await db.SaveChangesAsync();
        await reports.RecordAsync(id, Edit(await reports.LoadAsync(id, actor)), "Synthetic source proposal.", actor);
        // Partial source/live separation: retain the client but remove source-only review records.
        (await db.ClientRiskAssessments.SingleAsync(x => x.ClientId == id)).Status = ClientRiskAssessmentStatuses.Draft;
        await db.SaveChangesAsync();
        const string password = "synthetic-transfer";
        var export = await transfers.ExportAsync(id, password, "exporter@example.test", "Transfer partial evidence and proposal.", includePartial: true);
        var encrypted = await File.ReadAllBytesAsync(export.StoragePath);
        var source = await transfers.PreviewAsync(encrypted, password);
        Assert.NotNull(source.Package.BraRiskReport);
        var evidenceIds = source.Package.BraRiskReport!.Content.Scenarios.SelectMany(x => x.Evidence).Select(x => x.EvidenceItemId).ToHashSet();
        db.ClientBraRiskReports.RemoveRange(await db.ClientBraRiskReports.Where(x => x.ClientId == id).ToListAsync());
        db.ClientRiskAssessments.RemoveRange(await db.ClientRiskAssessments.Where(x => x.ClientId == id).ToListAsync());
        db.ClientOnboardingProfiles.RemoveRange(await db.ClientOnboardingProfiles.Where(x => x.ClientId == id).ToListAsync());
        db.ClientEvidenceItems.RemoveRange(await db.ClientEvidenceItems.Where(x => x.ClientId == id).ToListAsync());
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var preview = await transfers.PreviewAsync(encrypted, password);
        Assert.True(preview.CanApply, string.Join("; ", preview.Conflicts));
        await transfers.ApplyAsync(encrypted, password, "importer@example.test", "Apply source proposal without claiming a live recheck.");
        var imported = await reports.LoadAsync(id, actor);
        Assert.False(imported.IsCurrent);
        Assert.NotNull(imported.Report!.ImportPackageId);
        Assert.Equal("Codex", imported.Report.PerformedBy);
        Assert.False(imported.ClientReview.IsAccepted);
        Assert.All(imported.Report.ReadContent().Scenarios.SelectMany(x => x.Evidence), x => Assert.DoesNotContain(x.EvidenceItemId, evidenceIds));
    }

    internal static ClientBraRiskEdit Edit(ClientBraRiskModel model)
    {
        var evidenceId = model.ClientReview.Evidence.EvidenceItems.First(x => x.EvidenceType == "Identity").Id;
        return new() { PreviousReportId = model.History.FirstOrDefault()?.Id, SourceContentHash = model.ClientReview.ChecksContentHash,
            BraReference = "Synthetic BRA v1, method section 3; proposed client linkage.", PerformedBy = "Codex",
            Content = new() { Scope = "Synthetic existing client scenario.", Limitations = "Synthetic controlled test evidence only.",
                Scenarios = ClientBraRiskMethod.RiskTypes.Select(type => new ClientBraRiskScenario {
                    RiskType = type, Scenario = $"Synthetic {type} misuse scenario.", InherentLikelihood = type == "ML" ? 2 : 1, InherentImpact = 3,
                    InherentLikelihoodReason = "Synthetic exposure before verification.", InherentImpactReason = "Serious consequences if misuse occurs.",
                    ResidualLikelihood = 1, ResidualImpact = 3, ResidualLikelihoodReason = "Specific synthetic verified control addresses likelihood.",
                    ResidualImpactReason = "Consequence remains serious if the control fails.", ControlsAndEffect = "Synthetic identity control and documented review.",
                    Evidence = [new() { EvidenceItemId = evidenceId }]
                }).ToList() } };
    }
}
