using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientReviewTransferServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Completed_review_exports_previews_applies_once_and_rejects_duplicate_import()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientReviewTransferService>();
        var compliance = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var methodology = await db.RiskMethodologyVersions
            .Include(item => item.Factors).ThenInclude(item => item.Options)
            .Where(item =>
                item.Status == ComplianceStatuses.Review ||
                item.Status == ComplianceStatuses.Approved ||
                item.Status == ComplianceStatuses.Active)
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync();
        if (methodology is null)
        {
            var methodologyId = await compliance.CreateKanaanStarterMethodologyAsync(
                "reviewer@example.test",
                "Create transfer test methodology.");
            await compliance.SubmitMethodologyAsync(
                methodologyId,
                "reviewer@example.test",
                "Make transfer test methodology available.");
            methodology = await db.RiskMethodologyVersions
                .Include(item => item.Factors).ThenInclude(item => item.Options)
                .SingleAsync(item => item.Id == methodologyId);
        }
        var today = DateOnly.FromDateTime(DateTime.Today);
        var client = new Client
        {
            LegacyClientId = 99123,
            KanaanId = "TRANSFER-99123",
            DisplayName = "Transfer Pilot",
            SurnameOrEntityName = "Transfer / Pilot: Unsafe?",
            LifecycleStatus = ClientLifecycleStatuses.Current,
            LifecycleReason = "Current relationship confirmed for transfer test.",
            LifecycleReviewedAtUtc = DateTime.UtcNow,
            LifecycleReviewedBy = "reviewer@example.test",
            IsActive = true
        };
        var evidence = new ClientEvidenceItem
        {
            Client = client,
            EvidenceType = "Identity",
            Title = "Verified identity",
            FileName = "identity.pdf",
            FileSha256 = new string('a', 64),
            VerifiedDate = today,
            Reviewer = "reviewer@example.test",
            Status = ClientEvidenceStatuses.Verified,
            OwnershipStatus = ClientEvidenceOwnershipStatuses.Confirmed,
            SelectionStatus = ClientEvidenceSelectionStatuses.Current
        };
        client.EvidenceItems.Add(evidence);
        var assessment = new ClientRiskAssessment
        {
            Client = client,
            MethodologyVersion = methodology,
            Status = ClientRiskAssessmentStatuses.Finalised,
            CalculatedScore = methodology.Factors.Sum(factor => factor.Options.First().Score),
            CalculatedRating = "Standard",
            FinalRating = "Standard",
            StandardControlsApplied = true,
            Narrative = "Completed transfer test assessment.",
            EffectiveDate = today,
            NextReviewDate = today.AddYears(3),
            PreparedBy = "reviewer@example.test",
            FinalisedBy = "reviewer@example.test",
            FinalisedAtUtc = DateTime.UtcNow,
            Responses = methodology.Factors.Select(factor =>
            {
                var option = factor.Options.OrderBy(item => item.SortOrder).First();
                return new ClientRiskAssessmentResponse
                {
                    FactorDefinition = factor,
                    SelectedOption = option,
                    EvidenceItem = evidence,
                    Score = option.Score,
                    WeightedScore = option.Score * factor.Weight,
                    Explanation = $"Confirmed {factor.Name}.",
                    ConfirmedAtUtc = DateTime.UtcNow,
                    ConfirmedBy = "reviewer@example.test"
                };
            }).ToList()
        };
        client.RiskAssessments.Add(assessment);
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        const string passphrase = "transfer-test-passphrase";
        var export = await service.ExportAsync(
            client.Id, passphrase, "reviewer@example.test", "Prepare live transfer test.");
        Assert.Matches(
            @"^KCAS-review-C\d+-Transfer-Pilot-Unsafe-\d{8}-[a-f0-9]{12}\.kcas-review$",
            export.FileName);
        var encrypted = await File.ReadAllBytesAsync(export.StoragePath);

        db.ClientRiskAssessments.Remove(assessment);
        db.ClientEvidenceItems.Remove(evidence);
        client.LifecycleStatus = ClientLifecycleStatuses.Unreviewed;
        client.LifecycleReason = null;
        client.LifecycleReviewedAtUtc = null;
        client.LifecycleReviewedBy = null;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var preview = await service.PreviewAsync(encrypted, passphrase);
        Assert.True(preview.CanApply);
        Assert.Equal(client.Id, preview.TargetClientId);
        Assert.Equal(1, preview.NewEvidenceCount);

        var imported = await service.ApplyAsync(
            encrypted,
            passphrase,
            "live-importer@example.test",
            "Approved after matching the client and methodology.");
        Assert.Equal(client.Id, imported.ClientId);

        var restoredClient = await db.Clients.AsNoTracking().SingleAsync(item => item.Id == client.Id);
        Assert.Equal(ClientLifecycleStatuses.Current, restoredClient.LifecycleStatus);
        Assert.Single(await db.ClientEvidenceItems.AsNoTracking()
            .Where(item => item.ClientId == client.Id).ToListAsync());
        Assert.Single(await db.ClientRiskAssessments.AsNoTracking()
            .Where(item => item.ClientId == client.Id).ToListAsync());
        Assert.Contains(await db.ComplianceAuditEvents.AsNoTracking().ToListAsync(), item =>
            item.Action == "ClientReviewPackageApplied" &&
            item.UserName == "live-importer@example.test");

        var duplicatePreview = await service.PreviewAsync(encrypted, passphrase);
        Assert.True(duplicatePreview.AlreadyApplied);
        Assert.False(duplicatePreview.CanApply);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(
            encrypted,
            passphrase,
            "live-importer@example.test",
            "Attempt duplicate."));
    }

    [Fact]
    public async Task Preview_rejects_wrong_passphrase()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientReviewTransferService>();

        var exception = await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(
            () => service.PreviewAsync([1, 2, 3, 4], "wrong-passphrase-long"));

        Assert.Contains("package", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
