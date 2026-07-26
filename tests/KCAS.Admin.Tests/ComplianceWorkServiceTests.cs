using System.ComponentModel.DataAnnotations;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ComplianceWorkServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Ordinary_work_requires_evidence_and_authorised_approval_before_closure()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ComplianceWorkService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = await service.SaveAsync(new ComplianceWorkEditModel
        {
            TaskType = ComplianceTaskTypes.Training,
            Title = "Complete annual FICA refresher",
            Owner = "Administrator",
            DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(10))
        }, "preparer@example.test", "Create training work.");

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.RequestClosureAsync(id, "", "Completed", "Training complete", "preparer@example.test", "Request closure."));
        await service.RequestClosureAsync(id, "Attendance register and course certificate.", "Completed successfully.",
            "Required training was completed.", "preparer@example.test", "Submit closure evidence.");
        Assert.Equal(ComplianceWorkStatuses.PendingClosure,
            await db.ComplianceTasks.Where(item => item.Id == id).Select(item => item.Status).SingleAsync());

        await service.ApproveClosureAsync(id, "approver@example.test", "Approve evidenced closure.");
        var closed = await db.ComplianceTasks.AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Equal(ComplianceStatuses.Closed, closed.Status);
        Assert.NotNull(closed.ClosedAtUtc);
        Assert.Equal("approver@example.test", closed.ClosedBy);
    }

    [Fact]
    public async Task Material_or_high_work_requires_two_distinct_closure_approvals()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ComplianceWorkService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = await service.SaveAsync(new ComplianceWorkEditModel
        {
            TaskType = ComplianceTaskTypes.ScreeningEscalation,
            Title = "Review screening alert",
            Owner = "Key Individuals",
            Priority = "High",
            DueDate = DateOnly.FromDateTime(DateTime.Today)
        }, "preparer@example.test", "Create screening escalation.");
        await service.RequestClosureAsync(id, "Screening evidence and identity comparison.", "False positive.",
            "Alert was resolved after human review.", "preparer@example.test", "Request screening closure.");

        await service.ApproveClosureAsync(id, "ki-one@example.test", "First KI closure approval.");
        Assert.Equal(ComplianceWorkStatuses.PendingClosure,
            await db.ComplianceTasks.Where(item => item.Id == id).Select(item => item.Status).SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApproveClosureAsync(id, "ki-one@example.test", "Duplicate approval."));
        await service.ApproveClosureAsync(id, "ki-two@example.test", "Second KI closure approval.");
        Assert.Equal(ComplianceStatuses.Closed,
            await db.ComplianceTasks.Where(item => item.Id == id).Select(item => item.Status).SingleAsync());
    }

    [Fact]
    public async Task Periodic_review_generation_is_due_date_driven_and_idempotent()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ComplianceWorkService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var client = new Client
        {
            DisplayName = $"Due review client {Guid.NewGuid():N}",
            SurnameOrEntityName = "Review"
        };
        var methodology = new RiskMethodologyVersion
        {
            Name = $"Review method {Guid.NewGuid():N}",
            Status = ComplianceStatuses.Draft
        };
        db.AddRange(client, methodology);
        await db.SaveChangesAsync();
        var assessment = new ClientRiskAssessment
        {
            ClientId = client.Id,
            RiskMethodologyVersionId = methodology.Id,
            Status = ClientRiskAssessmentStatuses.Approved,
            FinalRating = BusinessRiskRatings.High,
            NextReviewDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1))
        };
        db.ClientRiskAssessments.Add(assessment);
        await db.SaveChangesAsync();

        var through = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
        Assert.Equal(1, await service.GeneratePeriodicReviewTasksAsync(through, "administrator@example.test", "Generate due reviews."));
        Assert.Equal(0, await service.GeneratePeriodicReviewTasksAsync(through, "administrator@example.test", "Repeat due review generation."));
        var task = await db.ComplianceTasks.AsNoTracking().SingleAsync(item => item.ClientRiskAssessmentId == assessment.Id);
        Assert.Equal(ComplianceTaskTypes.PeriodicReview, task.TaskType);
        Assert.Equal("High", task.Priority);
        Assert.Equal(client.Id, task.ClientId);

        var overdue = await service.LoadWorklistAsync(view: ComplianceWorkViews.Overdue);
        Assert.Contains(overdue, item => item.Id == task.Id && item.IsOverdue);
    }

    [Fact]
    public async Task Escalation_marks_work_high_and_is_audited()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ComplianceWorkService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var id = await service.SaveAsync(new ComplianceWorkEditModel
        {
            TaskType = ComplianceTaskTypes.Finding,
            Title = "Resolve sample finding",
            Owner = "Administrator",
            DueDate = DateOnly.FromDateTime(DateTime.Today.AddDays(5))
        }, "preparer@example.test", "Create finding.");
        await service.EscalateAsync(id, "ki@example.test", "Finding is now material.");

        var task = await db.ComplianceTasks.AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Equal("High", task.Priority);
        Assert.Equal("ki@example.test", task.EscalatedBy);
        Assert.True(await db.ComplianceAuditEvents.AnyAsync(item =>
            item.EntityType == nameof(ComplianceTask) && item.EntityId == id && item.Action == "Escalated"));
    }
}
