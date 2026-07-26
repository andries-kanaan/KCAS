using System.ComponentModel.DataAnnotations;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientRiskAssessmentServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Routine_ready_client_can_be_finalised_without_ki_approval()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var compliance = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var evidence = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var service = scope.ServiceProvider.GetRequiredService<ClientRiskAssessmentService>();
        await ActivateMethodologyAsync(compliance, "Routine methodology");
        var clientId = await CreateReadyClientAsync(db, evidence, "Routine Risk Client");

        var assessmentId = await service.CreateDraftAsync(clientId, "rep@example.test", "Start routine assessment.");
        var page = await service.LoadAsync(clientId);
        await service.SaveDraftAsync(assessmentId, BuildEdit(page, useHighOption: false), "rep@example.test", "Complete routine assessment.");
        await service.FinaliseAsync(assessmentId, "rep@example.test", "Finalise routine assessment.");

        var assessment = await db.ClientRiskAssessments.AsNoTracking().SingleAsync(item => item.Id == assessmentId);
        Assert.Equal(ClientRiskAssessmentStatuses.Finalised, assessment.Status);
        Assert.Equal("Low", assessment.FinalRating);
        Assert.NotNull(assessment.SnapshotJson);
        Assert.NotNull(assessment.NextReviewDate);

        var reassessmentId = await service.StartReassessmentAsync(
            assessmentId,
            ClientRiskReviewTriggerTypes.PeriodicReview,
            "Scheduled technical reassessment test.",
            "rep@example.test",
            "Start reassessment.");
        var copiedResponses = await db.ClientRiskAssessmentResponses.AsNoTracking()
            .Where(item => item.ClientRiskAssessmentId == reassessmentId)
            .ToListAsync();
        Assert.All(copiedResponses, item => Assert.NotNull(item.RiskFactorOptionId));
        Assert.All(copiedResponses, item => Assert.Null(item.ConfirmedAtUtc));
        Assert.True(await db.ComplianceTasks.AnyAsync(item =>
            item.ClientRiskAssessmentId == reassessmentId &&
            item.TaskType == ComplianceTaskTypes.PeriodicReview &&
            item.Status == ComplianceWorkStatuses.Open));
        await Assert.ThrowsAsync<ValidationException>(() =>
            service.FinaliseAsync(reassessmentId, "rep@example.test", "Attempt without reconfirming."));

        var reassessmentPage = await service.LoadAsync(clientId);
        await service.SaveDraftAsync(reassessmentId, BuildEdit(reassessmentPage, useHighOption: false), "rep@example.test", "Reconfirm copied answers.");
        await service.FinaliseAsync(reassessmentId, "rep@example.test", "Finalise periodic reassessment.");

        var states = await db.ClientRiskAssessments.AsNoTracking()
            .Where(item => item.Id == assessmentId || item.Id == reassessmentId)
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.Equal(ClientRiskAssessmentStatuses.Superseded, states[0].Status);
        Assert.Equal(ClientRiskAssessmentStatuses.Finalised, states[1].Status);
        Assert.Equal(assessmentId, states[1].PreviousAssessmentId);
        var printable = await service.LoadPrintableAsync(clientId, reassessmentId);
        Assert.Equal(ClientRiskReviewTriggerTypes.PeriodicReview, printable.ReviewTriggerType);
    }

    [Fact]
    public async Task Elevated_assessment_requires_two_distinct_ki_approvals()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var compliance = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var evidence = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var service = scope.ServiceProvider.GetRequiredService<ClientRiskAssessmentService>();
        await ActivateMethodologyAsync(compliance, "Elevated methodology");
        var clientId = await CreateReadyClientAsync(db, evidence, "Elevated Risk Client");

        var assessmentId = await service.CreateDraftAsync(clientId, "rep@example.test", "Start elevated assessment.");
        var page = await service.LoadAsync(clientId);
        var edit = BuildEdit(page, useHighOption: true);
        edit.HasPepExposure = true;
        await service.SaveDraftAsync(assessmentId, edit, "rep@example.test", "Complete elevated assessment.");
        await service.FinaliseAsync(assessmentId, "rep@example.test", "Escalate elevated assessment.");

        await service.ApproveAsync(assessmentId, "ki-one@example.test", "First KI accepts EDD relationship.");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveAsync(assessmentId, "ki-one@example.test", "Try duplicate approval."));
        await service.ApproveAsync(assessmentId, "ki-two@example.test", "Second KI accepts EDD relationship.");

        var assessment = await db.ClientRiskAssessments.AsNoTracking()
            .Include(item => item.Approvals)
            .SingleAsync(item => item.Id == assessmentId);
        Assert.Equal(ClientRiskAssessmentStatuses.Approved, assessment.Status);
        Assert.Equal(2, assessment.Approvals.Count);
    }

    [Fact]
    public async Task Blocking_evidence_and_sanctions_prevent_finalisation()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var compliance = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var service = scope.ServiceProvider.GetRequiredService<ClientRiskAssessmentService>();
        await ActivateMethodologyAsync(compliance, "Blocking methodology");
        var clientId = await CreateClientAsync(db, "Blocked Risk Client");
        var assessmentId = await service.CreateDraftAsync(clientId, "rep@example.test", "Start blocked assessment.");
        var page = await service.LoadAsync(clientId);
        var edit = BuildEdit(page, useHighOption: false);
        edit.HasSanctionsConcern = true;
        await service.SaveDraftAsync(assessmentId, edit, "rep@example.test", "Record sanctions concern.");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.FinaliseAsync(assessmentId, "rep@example.test", "Attempt finalisation."));
        Assert.Contains("blocking evidence", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ClientRiskAssessmentEditModel BuildEdit(ClientRiskAssessmentPageModel page, bool useHighOption)
        => new()
        {
            StandardControlsApplied = true,
            Narrative = "Assessment completed against verified client information.",
            Factors = page.Factors.Select((factor, index) =>
            {
                var option = useHighOption && index == 0 ? factor.Options.Last() : factor.Options.First();
                return new ClientRiskFactorInput(factor.FactorId, option.Id, null, $"Selected {option.Label} based on the client profile.");
            }).ToList()
        };

    private static async Task ActivateMethodologyAsync(ComplianceService service, string name)
    {
        var id = await service.SaveMethodologyAsync(new RiskMethodologyModel
        {
            Name = $"{name} {Guid.NewGuid():N}",
            VersionLabel = "v1",
            Factors =
            [
                new()
                {
                    Code = "CLIENT",
                    Name = "Client risk",
                    Weight = 1,
                    Options =
                    [
                        new() { Code = "LOW", Label = "Low", Score = 1 },
                        new() { Code = "HIGH", Label = "High", Score = 3, TriggersHighRisk = true }
                    ]
                }
            ],
            Bands =
            [
                new() { Name = "Low", MinimumScore = 0, MaximumScore = 2, ReviewMonths = 60 },
                new() { Name = "High", MinimumScore = 3, MaximumScore = 3, ReviewMonths = 12 }
            ]
        }, "preparer@example.test", "Create test methodology.");
        await service.SubmitMethodologyAsync(id, "preparer@example.test", "Submit test methodology.");
        await service.ApproveMethodologyAsync(id, "ki-one@example.test", "First KI approval.");
        await service.ApproveMethodologyAsync(id, "ki-two@example.test", "Second KI approval.");
        await service.ActivateMethodologyAsync(id, "ki-one@example.test", "Activate test methodology.");
    }

    private static async Task<int> CreateReadyClientAsync(
        ApplicationDbContext db,
        ClientEvidenceReadinessService evidence,
        string displayName)
    {
        var clientId = await CreateClientAsync(db, displayName);
        var readiness = await evidence.LoadClientReadinessAsync(clientId);
        foreach (var requirement in readiness.Requirements)
        {
            await evidence.CreateExceptionAsync(
                clientId,
                requirement.RequirementId,
                "Test-only accepted evidence exception.",
                DateOnly.FromDateTime(DateTime.Today.AddMonths(1)),
                "ki-one@example.test",
                "Prepare test client for risk assessment.");
        }
        return clientId;
    }

    private static async Task<int> CreateClientAsync(ApplicationDbContext db, string displayName)
    {
        var client = new Client
        {
            KanaanId = $"RISK-{Guid.NewGuid():N}"[..20],
            FullName = displayName,
            SurnameOrEntityName = displayName,
            DisplayName = displayName,
            ClientCategory = ClientCategories.NaturalPerson,
            ClientCategorySource = ClientCategorySources.Manual,
            LifecycleStatus = ClientLifecycleStatuses.Current
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }
}
