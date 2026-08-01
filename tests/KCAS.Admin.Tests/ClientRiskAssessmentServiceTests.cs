using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
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
        await ActivateMethodologyAsync(compliance, db, "Routine methodology");
        var clientId = await CreateReadyClientAsync(db, evidence, "Routine Risk Client");

        var assessmentId = await service.CreateDraftAsync(clientId, "rep@example.test", "Start routine assessment.");
        var generatedDraft = await db.ClientRiskAssessments.AsNoTracking()
            .Include(item => item.Responses)
            .SingleAsync(item => item.Id == assessmentId);
        Assert.True(generatedDraft.StandardControlsApplied);
        Assert.StartsWith("System-generated proposal", generatedDraft.Narrative);
        Assert.All(generatedDraft.Responses, item => Assert.NotNull(item.RiskFactorOptionId));
        Assert.All(generatedDraft.Responses, item => Assert.False(string.IsNullOrWhiteSpace(item.Explanation)));
        Assert.All(generatedDraft.Responses, item => Assert.Null(item.ConfirmedAtUtc));
        Assert.True(await db.ComplianceAuditEvents.AsNoTracking().AnyAsync(item =>
            item.EntityType == nameof(ClientRiskAssessment) &&
            item.EntityId == assessmentId &&
            item.Action == "ProposalGenerated"));
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
    public async Task Elevated_assessment_requires_one_authorised_ki_approval()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var compliance = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var evidence = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var service = scope.ServiceProvider.GetRequiredService<ClientRiskAssessmentService>();
        await ActivateMethodologyAsync(compliance, db, "Elevated methodology");
        var clientId = await CreateReadyClientAsync(db, evidence, "Elevated Risk Client");

        var assessmentId = await service.CreateDraftAsync(clientId, "rep@example.test", "Start elevated assessment.");
        var page = await service.LoadAsync(clientId);
        var edit = BuildEdit(page, useHighOption: true);
        edit.HasPepExposure = true;
        await service.SaveDraftAsync(assessmentId, edit, "rep@example.test", "Complete elevated assessment.");
        await service.FinaliseAsync(assessmentId, "rep@example.test", "Escalate elevated assessment.");

        await service.ApproveAsync(assessmentId, "ki-one@example.test", "KI accepts EDD relationship.");

        var assessment = await db.ClientRiskAssessments.AsNoTracking()
            .Include(item => item.Approvals)
            .SingleAsync(item => item.Id == assessmentId);
        Assert.Equal(ClientRiskAssessmentStatuses.Approved, assessment.Status);
        Assert.Single(assessment.Approvals);
        Assert.NotNull(assessment.ApprovedAtUtc);

        var audit = await db.ComplianceAuditEvents.AsNoTracking()
            .SingleAsync(item =>
                item.EntityType == nameof(ClientRiskAssessment) &&
                item.EntityId == assessmentId &&
                item.Action == "ApprovedByKI");
        Assert.Equal("ki-one@example.test", audit.UserName);
    }

    [Fact]
    public async Task Blocking_evidence_and_sanctions_prevent_finalisation()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var compliance = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var service = scope.ServiceProvider.GetRequiredService<ClientRiskAssessmentService>();
        await ActivateMethodologyAsync(compliance, db, "Blocking methodology");
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

    [Fact]
    public async Task Existing_empty_draft_can_generate_a_proposal_and_then_be_deleted()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var compliance = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var evidence = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var service = scope.ServiceProvider.GetRequiredService<ClientRiskAssessmentService>();
        await ActivateMethodologyAsync(compliance, db, "Existing draft methodology");
        var clientId = await CreateClientAsync(db, "Existing Empty Draft Client");
        var assessmentId = await service.CreateDraftAsync(clientId, "rep@example.test", "Create draft before readiness.");

        var emptyResponses = await db.ClientRiskAssessmentResponses.AsNoTracking()
            .Where(item => item.ClientRiskAssessmentId == assessmentId)
            .ToListAsync();
        Assert.All(emptyResponses, item => Assert.Null(item.RiskFactorOptionId));

        var readiness = await evidence.LoadClientReadinessAsync(clientId);
        foreach (var requirement in readiness.Requirements)
        {
            await evidence.CreateExceptionAsync(
                clientId,
                requirement.RequirementId,
                "Test-only accepted evidence exception.",
                DateOnly.FromDateTime(DateTime.Today.AddMonths(1)),
                "ki-one@example.test",
                "Complete readiness for proposal generation.");
        }

        await service.GenerateProposalAsync(
            assessmentId,
            "rep@example.test",
            "Populate the existing empty draft from completed records.");
        var generatedResponses = await db.ClientRiskAssessmentResponses.AsNoTracking()
            .Where(item => item.ClientRiskAssessmentId == assessmentId)
            .ToListAsync();
        Assert.All(generatedResponses, item => Assert.NotNull(item.RiskFactorOptionId));
        Assert.All(generatedResponses, item => Assert.Null(item.ConfirmedAtUtc));

        await service.DeleteDraftAsync(assessmentId, "rep@example.test", "Delete the obsolete test draft.");
        Assert.False(await db.ClientRiskAssessments.AsNoTracking().AnyAsync(item => item.Id == assessmentId));
        Assert.True(await db.ComplianceAuditEvents.AsNoTracking().AnyAsync(item =>
            item.EntityType == nameof(ClientRiskAssessment) &&
            item.EntityId == assessmentId &&
            item.Action == "DraftDeleted" &&
            item.OldValueJson != null &&
            item.NewValueJson == null));
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

    [Fact]
    public async Task Submitted_methodology_can_be_used_provisionally_before_ki_signoff()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var compliance = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var evidence = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var service = scope.ServiceProvider.GetRequiredService<ClientRiskAssessmentService>();

        foreach (var active in await db.RiskMethodologyVersions
                     .Where(item =>
                         item.Status == ComplianceStatuses.Active ||
                         item.Status == ComplianceStatuses.Approved ||
                         item.Status == ComplianceStatuses.Review)
                     .ToListAsync())
        {
            active.Status = ComplianceStatuses.Superseded;
        }
        await db.SaveChangesAsync();

        var methodologyId = await CreateMethodologyAsync(compliance, "Provisional methodology");
        await compliance.SubmitMethodologyAsync(methodologyId, "compliance@example.test", "Submit for operational use and later KI sign-off.");
        var clientId = await CreateReadyClientAsync(db, evidence, "Provisional Risk Client");

        var page = await service.LoadAsync(clientId);
        Assert.True(page.HasUsableMethodology);
        Assert.True(page.IsMethodologyProvisional);
        Assert.Equal(ComplianceStatuses.Review, page.MethodologyStatus);

        var assessmentId = await service.CreateDraftAsync(clientId, "compliance@example.test", "Start provisional assessment.");
        page = await service.LoadAsync(clientId);
        await service.SaveDraftAsync(assessmentId, BuildEdit(page, useHighOption: false), "compliance@example.test", "Complete provisional assessment.");
        await service.FinaliseAsync(assessmentId, "compliance@example.test", "Finalise without blocking on later KI sign-off.");

        var assessment = await db.ClientRiskAssessments.AsNoTracking().SingleAsync(item => item.Id == assessmentId);
        Assert.Equal(methodologyId, assessment.RiskMethodologyVersionId);
        Assert.Equal(ClientRiskAssessmentStatuses.Finalised, assessment.Status);
    }

    [Fact]
    public async Task Register_includes_completed_and_unassessed_current_clients_and_exports_them()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var compliance = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var evidence = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var service = scope.ServiceProvider.GetRequiredService<ClientRiskAssessmentService>();
        await ActivateMethodologyAsync(compliance, db, "Register methodology");
        var prefix = $"Register {Guid.NewGuid():N}";
        var completedClientId = await CreateReadyClientAsync(db, evidence, $"{prefix} completed");
        var outstandingClientId = await CreateClientAsync(db, $"{prefix} outstanding");

        var assessmentId = await service.CreateDraftAsync(
            completedClientId,
            "compliance@example.test",
            "Start completed register assessment.");
        var page = await service.LoadAsync(completedClientId);
        await service.SaveDraftAsync(
            assessmentId,
            BuildEdit(page, useHighOption: false),
            "compliance@example.test",
            "Complete register assessment.");
        await service.FinaliseAsync(
            assessmentId,
            "compliance@example.test",
            "Finalise register assessment.");

        var query = new ClientRiskRegisterQuery(
            prefix,
            null,
            null,
            null,
            null,
            null,
            null,
            DateOnly.FromDateTime(DateTime.Today));
        var register = await service.LoadRegisterAsync(query);

        Assert.Equal(2, register.Rows.Count);
        var completed = Assert.Single(register.Rows, item => item.ClientId == completedClientId);
        Assert.Equal(ClientRiskCoverageStates.Completed, completed.CoverageState);
        Assert.True(completed.IsReadyForAssessment);
        Assert.Equal(ClientRiskAssessmentStatuses.Finalised, completed.Status);
        var outstanding = Assert.Single(register.Rows, item => item.ClientId == outstandingClientId);
        Assert.Equal(ClientRiskCoverageStates.Outstanding, outstanding.CoverageState);
        Assert.False(outstanding.IsReadyForAssessment);
        Assert.True(outstanding.EvidenceBlockerCount > 0);
        Assert.Null(outstanding.AssessmentId);
        Assert.True(register.Summary.TotalCurrentClients >= 2);
        Assert.True(register.Summary.CompletedCount >= 1);
        Assert.True(register.Summary.OutstandingCount >= 1);

        var outstandingOnly = await service.LoadRegisterAsync(query with
        {
            CoverageState = ClientRiskCoverageStates.Outstanding
        });
        Assert.Single(outstandingOnly.Rows);
        Assert.Equal(outstandingClientId, outstandingOnly.Rows[0].ClientId);

        var csv = Encoding.UTF8.GetString(await service.ExportRegisterCsvAsync(query));
        Assert.Contains($"{prefix} completed", csv);
        Assert.Contains($"{prefix} outstanding", csv);
        Assert.Contains("EvidenceBlockers", csv);

        using var snapshot = JsonDocument.Parse(await service.ExportRegisterSnapshotAsync(query));
        Assert.Equal(1, snapshot.RootElement.GetProperty("schemaVersion").GetInt32());
        var exportedClientIds = snapshot.RootElement.GetProperty("clients")
            .EnumerateArray()
            .Select(item => item.GetProperty("clientId").GetInt32())
            .ToList();
        Assert.Contains(completedClientId, exportedClientIds);
        Assert.Contains(outstandingClientId, exportedClientIds);
    }

    private static async Task ActivateMethodologyAsync(ComplianceService service, ApplicationDbContext db, string name)
    {
        var id = await CreateMethodologyAsync(service, name);
        await service.SubmitMethodologyAsync(id, "preparer@example.test", "Submit test methodology.");
        if (!await db.GovernanceRoleAssignments.AnyAsync(item =>
                item.Email == "ki-one@example.test" &&
                item.IsActive &&
                item.RoleType == "Key Individual"))
        {
            db.GovernanceRoleAssignments.Add(new GovernanceRoleAssignment
            {
                RoleType = "Key Individual",
                PersonName = "KI One",
                Email = "ki-one@example.test",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }
        await service.ApproveMethodologyAsync(id, "ki-one@example.test", "KI methodology sign-off.");
        await service.ActivateMethodologyAsync(id, "compliance@example.test", "Activate test methodology.");
    }

    private static Task<int> CreateMethodologyAsync(ComplianceService service, string name)
        => service.SaveMethodologyAsync(new RiskMethodologyModel
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
