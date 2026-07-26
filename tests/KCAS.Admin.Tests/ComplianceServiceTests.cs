using System.ComponentModel.DataAnnotations;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ComplianceServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Profile_changes_require_reason_and_write_audit()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await Assert.ThrowsAsync<ValidationException>(() => service.SaveProfileAsync(new ComplianceProfileModel
        {
            LegalName = "Kanaan Trust"
        }, "compliance@example.test", ""));

        var profileId = await service.SaveProfileAsync(new ComplianceProfileModel
        {
            LegalName = "Kanaan Trust",
            FspNumber = "528"
        }, "compliance@example.test", "Initial compliance setup.");

        var audit = await db.ComplianceAuditEvents.AsNoTracking()
            .SingleAsync(audit => audit.EntityType == nameof(ComplianceProfile) && audit.EntityId == profileId);

        Assert.Equal("Created", audit.Action);
        Assert.Equal("compliance@example.test", audit.UserName);
        Assert.Equal("Initial compliance setup.", audit.Reason);
    }

    [Fact]
    public async Task Reference_values_prevent_duplicate_active_category_codes()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var suffix = Guid.NewGuid().ToString("N");

        await service.SaveReferenceValueAsync(new ComplianceReferenceValueModel
        {
            Category = "ClientType",
            Code = $"TRUST-{suffix}",
            Name = "Trust"
        }, "compliance@example.test", "Add trust client type.");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveReferenceValueAsync(new ComplianceReferenceValueModel
        {
            Category = "ClientType",
            Code = $"TRUST-{suffix}",
            Name = "Trust duplicate"
        }, "compliance@example.test", "Try duplicate."));
    }

    [Fact]
    public async Task Methodology_approval_flow_activates_one_version_and_supersedes_previous()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var firstId = await CreateApprovedMethodologyAsync(service, "Methodology One");
        await service.ActivateMethodologyAsync(firstId, "approver@example.test", "Activate first version.");

        var secondId = await CreateApprovedMethodologyAsync(service, "Methodology Two");
        await service.ActivateMethodologyAsync(secondId, "approver@example.test", "Activate second version.");

        var methodologies = await db.RiskMethodologyVersions.AsNoTracking()
            .Where(methodology => methodology.Id == firstId || methodology.Id == secondId)
            .OrderBy(methodology => methodology.Id)
            .ToListAsync();

        Assert.Equal(ComplianceStatuses.Superseded, methodologies[0].Status);
        Assert.Equal(ComplianceStatuses.Active, methodologies[1].Status);
        Assert.Single(methodologies, methodology => methodology.Status == ComplianceStatuses.Active);
    }

    [Fact]
    public async Task Approved_methodology_cannot_be_edited_directly()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ComplianceService>();

        var methodologyId = await CreateApprovedMethodologyAsync(service, "Locked Methodology");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveMethodologyAsync(new RiskMethodologyModel
        {
            Id = methodologyId,
            Name = "Edited Locked Methodology"
        }, "compliance@example.test", "Try direct edit."));
    }

    [Fact]
    public async Task Tasks_and_evidence_write_audit_events()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var taskId = await service.SaveTaskAsync(new ComplianceTaskModel
        {
            Title = "Review RMCP",
            Owner = "Compliance",
            Status = ComplianceStatuses.Review
        }, "compliance@example.test", "Create review task.");
        var evidenceId = await service.SaveEvidenceAsync(new ComplianceEvidenceModel
        {
            EvidenceType = "BoardResolution",
            Title = "Board approval resolution",
            Location = "RMCP and Policy Approval"
        }, "compliance@example.test", "Record evidence location.");

        Assert.True(await db.ComplianceAuditEvents.AnyAsync(audit => audit.EntityType == nameof(ComplianceTask) && audit.EntityId == taskId));
        Assert.True(await db.ComplianceAuditEvents.AnyAsync(audit => audit.EntityType == nameof(ComplianceEvidence) && audit.EntityId == evidenceId));
    }

    [Fact]
    public async Task Legacy_task_editor_cannot_bypass_evidence_gated_closure()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ComplianceService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveTaskAsync(new ComplianceTaskModel
        {
            Title = "Attempt direct closure",
            Status = ComplianceStatuses.Closed
        }, "compliance@example.test", "Try to bypass work-register closure."));
    }

    private static async Task<int> CreateApprovedMethodologyAsync(ComplianceService service, string name)
    {
        var methodologyId = await service.SaveMethodologyAsync(new RiskMethodologyModel
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
                    Options = [new() { Code = "LOW", Label = "Low", Score = 1 }]
                }
            ],
            Bands = [new() { Name = "Low", MinimumScore = 0, MaximumScore = 2 }]
        }, "preparer@example.test", "Create methodology.");

        await service.SubmitMethodologyAsync(methodologyId, "preparer@example.test", "Submit for review.");
        await service.ApproveMethodologyAsync(methodologyId, "ki-one@example.test", "First KI approval.");
        await service.ApproveMethodologyAsync(methodologyId, "ki-two@example.test", "Second KI approval.");
        return methodologyId;
    }

    [Fact]
    public async Task Methodology_requires_two_distinct_ki_approvals()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var methodologyId = await service.SaveMethodologyAsync(new RiskMethodologyModel
        {
            Name = $"Two KI methodology {Guid.NewGuid():N}",
            Factors =
            [
                new()
                {
                    Code = "CLIENT",
                    Name = "Client risk",
                    Weight = 1,
                    Options = [new() { Code = "LOW", Label = "Low", Score = 1 }]
                }
            ],
            Bands = [new() { Name = "Low", MinimumScore = 0, MaximumScore = 2 }]
        }, "preparer@example.test", "Create two-KI test.");
        await service.SubmitMethodologyAsync(methodologyId, "preparer@example.test", "Submit two-KI test.");
        await service.ApproveMethodologyAsync(methodologyId, "ki-one@example.test", "First KI approval.");

        Assert.Equal(
            ComplianceStatuses.Review,
            await db.RiskMethodologyVersions.Where(item => item.Id == methodologyId).Select(item => item.Status).SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveMethodologyAsync(methodologyId, "ki-one@example.test", "Duplicate first KI approval."));

        await service.ApproveMethodologyAsync(methodologyId, "ki-two@example.test", "Second KI approval.");
        Assert.Equal(
            ComplianceStatuses.Approved,
            await db.RiskMethodologyVersions.Where(item => item.Id == methodologyId).Select(item => item.Status).SingleAsync());
    }

    [Fact]
    public async Task Kanaan_starter_methodology_is_created_as_draft()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var methodologyId = await service.CreateKanaanStarterMethodologyAsync(
            "compliance@example.test",
            "Create the controlled starter draft.");

        var methodology = await db.RiskMethodologyVersions.AsNoTracking()
            .Include(item => item.Factors).ThenInclude(factor => factor.Options)
            .Include(item => item.Bands)
            .SingleAsync(item => item.Id == methodologyId);
        Assert.Equal(ComplianceStatuses.Draft, methodology.Status);
        Assert.Equal(6, methodology.Factors.Count);
        Assert.All(methodology.Factors, factor => Assert.Equal(3, factor.Options.Count));
        Assert.Equal(new[] { "Low", "Standard", "High" }, methodology.Bands.OrderBy(item => item.SortOrder).Select(item => item.Name));
    }
}
