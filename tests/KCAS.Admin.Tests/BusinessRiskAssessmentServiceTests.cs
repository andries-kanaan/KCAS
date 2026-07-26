using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class BusinessRiskAssessmentServiceTests(KcasWebApplicationFactory factory)
{
    [Theory]
    [InlineData(1, "Low")]
    [InlineData(2, "Low")]
    [InlineData(3, "Standard")]
    [InlineData(4, "Standard")]
    [InlineData(6, "High")]
    [InlineData(9, "High")]
    public void Matrix_uses_proportional_three_by_three_bands(int score, string expected)
        => Assert.Equal(expected, BusinessRiskAssessmentService.RatingFor(score));

    [Fact]
    public async Task Bra_requires_complete_categories_and_two_distinct_ki_approvals_then_freezes()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BusinessRiskAssessmentService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var year = DateTime.UtcNow.Year;
        var id = await service.CreateDraftAsync(year, DateOnly.FromDateTime(DateTime.UtcNow), "preparer@example.test", "Create annual test BRA.");

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.SubmitAsync(id, "preparer@example.test", "Attempt incomplete submission."));

        var assessment = await service.LoadAsync(id);
        var edit = Complete(BusinessRiskAssessmentEditModel.FromEntity(assessment!));
        await service.SaveDraftAsync(edit, "preparer@example.test", "Complete the six-category BRA.");
        await service.SubmitAsync(id, "preparer@example.test", "Submit completed BRA.");

        await service.ApproveAsync(id, "ki-one@example.test", "First KI approval.");
        Assert.Equal(ComplianceStatuses.Review,
            await db.BusinessRiskAssessments.Where(item => item.Id == id).Select(item => item.Status).SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(id, "ki-one@example.test", "Duplicate approval."));

        await service.ApproveAsync(id, "ki-two@example.test", "Second KI approval.");
        var approved = await db.BusinessRiskAssessments.AsNoTracking().Include(item => item.Approvals).SingleAsync(item => item.Id == id);
        Assert.Equal(ComplianceStatuses.Approved, approved.Status);
        Assert.Equal(2, approved.Approvals.Count);
        Assert.False(string.IsNullOrWhiteSpace(approved.SnapshotJson));
        Assert.False(string.IsNullOrWhiteSpace(approved.PortfolioSnapshotJson));
        using (var frozen = JsonDocument.Parse(approved.SnapshotJson))
        {
            Assert.False(string.IsNullOrWhiteSpace(frozen.RootElement.GetProperty("managementJudgement").GetString()));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveDraftAsync(edit, "preparer@example.test", "Try to edit an approved BRA."));
        var printable = await service.LoadPrintableAsync(id);
        Assert.Equal(approved.SnapshotJson, printable.SnapshotJson);
    }

    [Fact]
    public async Task Portfolio_snapshot_uses_approved_client_assessments_as_at_date()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BusinessRiskAssessmentService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var asAt = DateOnly.FromDateTime(DateTime.UtcNow);
        var before = await db.ClientRiskAssessments.CountAsync(item =>
            item.Status == ClientRiskAssessmentStatuses.Approved &&
            item.EffectiveDate != null && item.EffectiveDate <= asAt);
        var client = new Client
        {
            DisplayName = $"BRA snapshot client {Guid.NewGuid():N}",
            SurnameOrEntityName = "Snapshot",
            ClientCategory = ClientCategories.Trust
        };
        var methodology = new RiskMethodologyVersion
        {
            Name = $"BRA snapshot method {Guid.NewGuid():N}",
            Status = ComplianceStatuses.Draft
        };
        db.AddRange(client, methodology);
        await db.SaveChangesAsync();
        db.ClientRiskAssessments.Add(new ClientRiskAssessment
        {
            ClientId = client.Id,
            RiskMethodologyVersionId = methodology.Id,
            Status = ClientRiskAssessmentStatuses.Approved,
            EffectiveDate = asAt,
            FinalRating = BusinessRiskRatings.Standard
        });
        await db.SaveChangesAsync();

        var id = await service.CreateDraftAsync(DateTime.UtcNow.Year, asAt, "preparer@example.test", "Create portfolio snapshot test.");
        var assessment = await service.LoadAsync(id);
        await service.SaveDraftAsync(Complete(BusinessRiskAssessmentEditModel.FromEntity(assessment!)), "preparer@example.test", "Complete snapshot test.");
        await service.SubmitAsync(id, "preparer@example.test", "Freeze portfolio evidence.");
        var submitted = await service.LoadAsync(id);
        using var snapshot = JsonDocument.Parse(submitted!.PortfolioSnapshotJson!);

        Assert.Equal(before + 1, snapshot.RootElement.GetProperty("approvedClientAssessmentCount").GetInt32());
        Assert.True(snapshot.RootElement.GetProperty("clientsByCategory").GetProperty(ClientCategories.Trust).GetInt32() >= 1);
    }

    [Fact]
    public async Task Activating_new_approved_bra_supersedes_previous_effective_version()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<BusinessRiskAssessmentService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var firstId = await CreateApprovedAsync(service, "First");
        await service.ActivateAsync(firstId, "ki-two@example.test", "Make first BRA effective.");
        var secondId = await CreateApprovedAsync(service, "Second");
        await service.ActivateAsync(secondId, "ki-two@example.test", "Make replacement BRA effective.");

        Assert.Equal(ComplianceStatuses.Superseded,
            await db.BusinessRiskAssessments.Where(item => item.Id == firstId).Select(item => item.Status).SingleAsync());
        Assert.Equal(ComplianceStatuses.Active,
            await db.BusinessRiskAssessments.Where(item => item.Id == secondId).Select(item => item.Status).SingleAsync());
    }

    private static async Task<int> CreateApprovedAsync(BusinessRiskAssessmentService service, string label)
    {
        var id = await service.CreateDraftAsync(DateTime.UtcNow.Year, DateOnly.FromDateTime(DateTime.UtcNow), "preparer@example.test", $"Create {label} BRA.");
        var assessment = await service.LoadAsync(id);
        var edit = Complete(BusinessRiskAssessmentEditModel.FromEntity(assessment!));
        edit.Name += $" {label} {Guid.NewGuid():N}";
        await service.SaveDraftAsync(edit, "preparer@example.test", $"Complete {label} BRA.");
        await service.SubmitAsync(id, "preparer@example.test", $"Submit {label} BRA.");
        await service.ApproveAsync(id, "ki-one@example.test", "First KI approval.");
        await service.ApproveAsync(id, "ki-two@example.test", "Second KI approval.");
        return id;
    }

    private static BusinessRiskAssessmentEditModel Complete(BusinessRiskAssessmentEditModel model)
    {
        model.ManagementJudgement = "The overall risk is manageable for Kanaan's small and stable operating model.";
        model.Limitations = "Client operational verification is deferred to the controlled population phase.";
        model.RiskTolerance = "No tolerance for sanctions breaches; controlled tolerance for documented standard risks.";
        foreach (var item in model.Items)
        {
            item.RiskStatement = $"{BusinessRiskCategories.Display(item.Category)} risk could affect Kanaan.";
            item.EvidenceAndRationale = "Supported by the frozen portfolio snapshot and management review.";
            item.Likelihood = 2;
            item.Impact = 2;
            item.KeyControls = "KI oversight, documented procedures and periodic review.";
            item.ControlEffectiveness = BusinessRiskControlEffectiveness.Effective;
            item.ResidualRating = BusinessRiskRatings.Low;
            item.ResidualRationale = "The controls reduce the risk to a proportionate residual level.";
            item.TreatmentDecision = BusinessRiskTreatmentDecisions.Accept;
            item.Owner = "Key Individuals";
        }
        return model;
    }
}
