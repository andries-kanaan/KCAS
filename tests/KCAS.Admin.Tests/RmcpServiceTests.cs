using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class RmcpServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Rmcp_requires_complete_coverage_maps_material_risk_creates_gap_task_and_freezes_after_two_ki_approvals()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RmcpService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bra = await CreateApprovedBraAsync(db, "RMCP workflow", materialRisk: true);
        var id = await service.CreateDraftAsync(bra.Id, "preparer@example.test", "Create RMCP draft.");

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.SubmitAsync(id, "preparer@example.test", "Attempt incomplete submission."));

        var page = await service.LoadAsync(id);
        var edit = Complete(RmcpEditModel.FromEntity(page!.Version), bra.Items.Single().Id, withGap: true);
        await service.SaveDraftAsync(edit, "preparer@example.test", "Complete control register.");
        await service.SubmitAsync(id, "preparer@example.test", "Submit controlled RMCP.");

        var submitted = await db.RmcpVersions.AsNoTracking().Include(item => item.Controls).SingleAsync(item => item.Id == id);
        Assert.Equal(ComplianceStatuses.Review, submitted.Status);
        var gapControl = Assert.Single(submitted.Controls, item => item.HasGap);
        Assert.NotNull(gapControl.ComplianceTaskId);
        Assert.True(await db.ComplianceTasks.AnyAsync(item =>
            item.Id == gapControl.ComplianceTaskId &&
            item.LinkedEntityType == nameof(RmcpControl) &&
            item.LinkedEntityId == gapControl.Id));

        await service.ApproveAsync(id, "ki-one@example.test", "First KI approval.");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(id, "ki-one@example.test", "Duplicate KI approval."));
        await service.ApproveAsync(id, "ki-two@example.test", "Second KI approval.");

        var approved = await db.RmcpVersions.AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Equal(ComplianceStatuses.Approved, approved.Status);
        Assert.False(string.IsNullOrWhiteSpace(approved.SnapshotJson));
        using var snapshot = JsonDocument.Parse(approved.SnapshotJson!);
        Assert.Equal(9, snapshot.RootElement.GetProperty("controls").GetArrayLength());
        Assert.Equal(2, snapshot.RootElement.GetProperty("approvals").GetArrayLength());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveDraftAsync(edit, "preparer@example.test", "Try to edit approved version."));
        Assert.Equal(approved.SnapshotJson, (await service.LoadPrintableAsync(id)).SnapshotJson);
    }

    [Fact]
    public async Task Rmcp_rejects_risk_link_from_a_different_bra()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RmcpService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var selectedBra = await CreateApprovedBraAsync(db, "Selected BRA", materialRisk: false);
        var otherBra = await CreateApprovedBraAsync(db, "Other BRA", materialRisk: false);
        var id = await service.CreateDraftAsync(selectedBra.Id, "preparer@example.test", "Create link validation draft.");
        var page = await service.LoadAsync(id);
        var edit = Complete(RmcpEditModel.FromEntity(page!.Version), null, withGap: false);
        edit.Controls[0].BusinessRiskItemId = otherBra.Items.Single().Id;

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.SaveDraftAsync(edit, "preparer@example.test", "Attempt invalid BRA link."));
    }

    [Fact]
    public async Task Activating_replacement_rmcp_supersedes_previous_effective_version_and_sets_review_date()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RmcpService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bra = await CreateApprovedBraAsync(db, "Effective RMCP BRA", materialRisk: false);
        var firstId = await CreateApprovedRmcpAsync(service, bra.Id, "First");
        var effective = new DateOnly(2026, 7, 26);
        await service.ActivateAsync(firstId, effective, "ki-two@example.test", "Activate first RMCP.");
        var secondId = await CreateApprovedRmcpAsync(service, bra.Id, "Second");
        await service.ActivateAsync(secondId, effective, "ki-two@example.test", "Activate replacement RMCP.");

        var versions = await db.RmcpVersions.AsNoTracking()
            .Where(item => item.Id == firstId || item.Id == secondId)
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.Equal(ComplianceStatuses.Superseded, versions[0].Status);
        Assert.Equal(ComplianceStatuses.Active, versions[1].Status);
        Assert.Equal(effective.AddMonths(12), versions[1].NextReviewDate);
    }

    private static async Task<int> CreateApprovedRmcpAsync(RmcpService service, int braId, string label)
    {
        var id = await service.CreateDraftAsync(braId, "preparer@example.test", $"Create {label} RMCP.");
        var page = await service.LoadAsync(id);
        var edit = Complete(RmcpEditModel.FromEntity(page!.Version), null, withGap: false);
        edit.VersionReference = $"{label}-{Guid.NewGuid():N}";
        await service.SaveDraftAsync(edit, "preparer@example.test", $"Complete {label} RMCP.");
        await service.SubmitAsync(id, "preparer@example.test", $"Submit {label} RMCP.");
        await service.ApproveAsync(id, "ki-one@example.test", "First KI approval.");
        await service.ApproveAsync(id, "ki-two@example.test", "Second KI approval.");
        return id;
    }

    private static RmcpEditModel Complete(RmcpEditModel model, int? materialRiskId, bool withGap)
    {
        model.SignedDocumentLocation = @"C:\Compliance\RMCP\Signed RMCP.pdf";
        model.ApprovalResolutionLocation = @"C:\Compliance\RMCP\Signed resolution.pdf";
        foreach (var control in model.Controls)
        {
            control.ProcedureSummary = $"Kanaan procedure for {RmcpControlDomains.Display(control.Domain)}.";
            control.EvidenceExpectation = "A dated review record or supporting client file evidence.";
            control.MonitoringMethod = "KI review of exceptions and a periodic sample.";
            control.EscalationProcedure = "Escalate material exceptions to both KIs.";
        }
        model.Controls[0].BusinessRiskItemId = materialRiskId;
        if (withGap)
        {
            model.Controls[0].HasGap = true;
            model.Controls[0].GapDescription = "Refresh the documented procedure.";
            model.Controls[0].TreatmentOwner = "Administrator";
            model.Controls[0].TreatmentDueDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(1));
        }
        return model;
    }

    private static async Task<BusinessRiskAssessment> CreateApprovedBraAsync(ApplicationDbContext db, string label, bool materialRisk)
    {
        var bra = new BusinessRiskAssessment
        {
            Name = $"{label} {Guid.NewGuid():N}",
            AssessmentYear = DateTime.Today.Year,
            AsAtDate = DateOnly.FromDateTime(DateTime.Today),
            Status = ComplianceStatuses.Approved,
            Scope = "Test scope",
            SnapshotJson = "{}",
            Items =
            [
                new()
                {
                    Category = BusinessRiskCategories.Clients,
                    RiskStatement = "Material client risk",
                    EvidenceAndRationale = "Test evidence",
                    InherentScore = 6,
                    InherentRating = BusinessRiskRatings.High,
                    ResidualRating = materialRisk ? BusinessRiskRatings.High : BusinessRiskRatings.Low,
                    TreatmentDecision = materialRisk ? BusinessRiskTreatmentDecisions.Treat : BusinessRiskTreatmentDecisions.Accept,
                    Owner = "Key Individuals"
                }
            ]
        };
        db.BusinessRiskAssessments.Add(bra);
        await db.SaveChangesAsync();
        return bra;
    }
}
