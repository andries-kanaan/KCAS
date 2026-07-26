using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ComplianceWorkService(ApplicationDbContext db)
{
    private static readonly JsonSerializerOptions AuditOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<ComplianceWorkListItem>> LoadWorklistAsync(
        string? search = null,
        string? taskType = null,
        string? status = null,
        string? view = null)
    {
        var query = db.ComplianceTasks.AsNoTracking()
            .Include(item => item.Client)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(item => item.Title.Contains(term) ||
                                        (item.Owner != null && item.Owner.Contains(term)) ||
                                        (item.Client != null && item.Client.DisplayName.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(taskType))
        {
            query = query.Where(item => item.TaskType == taskType);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(item => item.Status == status);
        }
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (view == ComplianceWorkViews.Overdue)
        {
            query = query.Where(item => item.DueDate < today &&
                                        item.Status != ComplianceStatuses.Closed &&
                                        item.Status != ComplianceStatuses.Withdrawn);
        }
        else if (view == ComplianceWorkViews.HighPriority)
        {
            query = query.Where(item => item.Priority == "High");
        }
        else if (view == ComplianceWorkViews.Unresolved)
        {
            query = query.Where(item => item.Status != ComplianceStatuses.Closed &&
                                        item.Status != ComplianceStatuses.Withdrawn);
        }

        return await query.OrderBy(item => item.Status == ComplianceStatuses.Closed)
            .ThenBy(item => item.DueDate)
            .ThenByDescending(item => item.Priority == "High")
            .Select(item => new ComplianceWorkListItem(
                item.Id,
                item.TaskType,
                item.Title,
                item.Owner,
                item.DueDate,
                item.Priority,
                item.Status,
                item.ClientId,
                item.Client == null ? null : item.Client.DisplayName,
                item.DueDate < today && item.Status != ComplianceStatuses.Closed && item.Status != ComplianceStatuses.Withdrawn))
            .ToListAsync();
    }

    public async Task<ComplianceWorkPageModel?> LoadAsync(int id)
    {
        var task = await db.ComplianceTasks.AsNoTracking()
            .Include(item => item.Client)
            .Include(item => item.ClientRiskAssessment)
            .Include(item => item.BusinessRiskAssessment)
            .Include(item => item.RmcpVersion)
            .Include(item => item.RmcpControl)
            .SingleOrDefaultAsync(item => item.Id == id);
        if (task is null)
        {
            return null;
        }
        var approvals = await LoadClosureApprovalsAsync(id);
        return new(task, approvals, RequiredClosureApprovals(task));
    }

    public async Task<ComplianceWorkOptions> LoadOptionsAsync()
        => new(
            await db.Clients.AsNoTracking().Where(item => item.IsActive).OrderBy(item => item.DisplayName)
                .Select(item => new ComplianceWorkOption(item.Id, item.DisplayName)).ToListAsync(),
            await db.BusinessRiskAssessments.AsNoTracking()
                .Where(item => item.Status == ComplianceStatuses.Active || item.Status == ComplianceStatuses.Approved)
                .OrderByDescending(item => item.AssessmentYear)
                .Select(item => new ComplianceWorkOption(item.Id, item.Name)).ToListAsync(),
            await db.RmcpVersions.AsNoTracking()
                .Where(item => item.Status == ComplianceStatuses.Active || item.Status == ComplianceStatuses.Approved)
                .OrderByDescending(item => item.Id)
                .Select(item => new ComplianceWorkOption(item.Id, item.Title + " " + item.VersionReference)).ToListAsync(),
            await db.RmcpControls.AsNoTracking()
                .Where(item => item.RmcpVersion!.Status == ComplianceStatuses.Active || item.RmcpVersion.Status == ComplianceStatuses.Approved)
                .OrderBy(item => item.Code)
                .Select(item => new ComplianceWorkOption(item.Id, item.Code + " " + item.Title)).ToListAsync());

    public async Task<int> SaveAsync(ComplianceWorkEditModel model, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var type = Allowed(model.TaskType, ComplianceTaskTypes.All, "task type");
        ComplianceTask task;
        string action;
        string? oldJson = null;
        if (model.Id is null)
        {
            task = new ComplianceTask { CreatedAtUtc = DateTime.UtcNow };
            db.ComplianceTasks.Add(task);
            action = "Created";
        }
        else
        {
            task = await db.ComplianceTasks.SingleAsync(item => item.Id == model.Id.Value);
            if (task.Status is ComplianceStatuses.Closed or ComplianceStatuses.Withdrawn or ComplianceWorkStatuses.PendingClosure)
            {
                throw new InvalidOperationException("Closed, withdrawn or pending-closure work cannot be edited.");
            }
            oldJson = JsonSerializer.Serialize(AuditSummary(task), AuditOptions);
            action = "Updated";
        }

        task.TaskType = type;
        task.Title = Required(model.Title, "Title", 240);
        task.Description = NormalizeOrNull(model.Description);
        task.Owner = Required(model.Owner, "Owner", 191);
        task.DueDate = model.DueDate ?? throw new ValidationException("Due date is required.");
        task.Priority = Allowed(model.Priority, ComplianceWorkPriorities.All, "priority");
        task.Status = model.Status is ComplianceWorkStatuses.Open or ComplianceWorkStatuses.InProgress
            ? model.Status
            : ComplianceWorkStatuses.Open;
        task.ClientId = model.ClientId;
        task.ClientRiskAssessmentId = model.ClientRiskAssessmentId;
        task.BusinessRiskAssessmentId = model.BusinessRiskAssessmentId;
        task.RmcpVersionId = model.RmcpVersionId;
        task.RmcpControlId = model.RmcpControlId;
        task.EvidenceSummary = NormalizeOrNull(model.EvidenceSummary);
        task.Outcome = NormalizeOrNull(model.Outcome);
        task.UpdatedAtUtc = DateTime.UtcNow;
        task.UpdatedBy = user;
        SetLegacyLink(task);
        await db.SaveChangesAsync();
        db.ComplianceAuditEvents.Add(CreateAudit(task.Id, action, user, reason, oldJson, AuditSummary(task)));
        await db.SaveChangesAsync();
        return task.Id;
    }

    public async Task<int> GeneratePeriodicReviewTasksAsync(DateOnly throughDate, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var assessments = await db.ClientRiskAssessments.AsNoTracking()
            .Include(item => item.Client)
            .Where(item => (item.Status == ClientRiskAssessmentStatuses.Approved ||
                            item.Status == ClientRiskAssessmentStatuses.Finalised) &&
                           item.NextReviewDate != null &&
                           item.NextReviewDate <= throughDate)
            .ToListAsync();
        var assessmentIds = assessments.Select(item => item.Id).ToList();
        var existing = await db.ComplianceTasks.AsNoTracking()
            .Where(item => item.TaskType == ComplianceTaskTypes.PeriodicReview &&
                           item.ClientRiskAssessmentId != null &&
                           assessmentIds.Contains(item.ClientRiskAssessmentId.Value) &&
                           item.Status != ComplianceStatuses.Closed &&
                           item.Status != ComplianceStatuses.Withdrawn)
            .Select(item => item.ClientRiskAssessmentId!.Value)
            .ToHashSetAsync();
        var created = 0;
        foreach (var assessment in assessments.Where(item => !existing.Contains(item.Id)))
        {
            var task = new ComplianceTask
            {
                TaskType = ComplianceTaskTypes.PeriodicReview,
                Title = $"Periodic client review: {assessment.Client!.DisplayName}",
                Description = $"Review approved client risk assessment #{assessment.Id}.",
                Owner = "Administrator",
                DueDate = assessment.NextReviewDate,
                Priority = assessment.FinalRating == BusinessRiskRatings.High ? "High" : "Normal",
                Status = ComplianceWorkStatuses.Open,
                ClientId = assessment.ClientId,
                ClientRiskAssessmentId = assessment.Id,
                LinkedEntityType = nameof(ClientRiskAssessment),
                LinkedEntityId = assessment.Id,
                UpdatedBy = user
            };
            db.ComplianceTasks.Add(task);
            await db.SaveChangesAsync();
            db.ComplianceAuditEvents.Add(CreateAudit(task.Id, "GeneratedFromClientReviewDate", user, reason, null, AuditSummary(task)));
            created++;
        }
        await db.SaveChangesAsync();
        return created;
    }

    public async Task EscalateAsync(int id, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var task = await LoadMutableAsync(id);
        var oldJson = JsonSerializer.Serialize(AuditSummary(task), AuditOptions);
        task.Priority = "High";
        task.EscalatedAtUtc = DateTime.UtcNow;
        task.EscalatedBy = user;
        task.UpdatedAtUtc = DateTime.UtcNow;
        task.UpdatedBy = user;
        db.ComplianceAuditEvents.Add(CreateAudit(id, "Escalated", user, reason, oldJson, AuditSummary(task)));
        await db.SaveChangesAsync();
    }

    public async Task RequestClosureAsync(int id, string evidenceSummary, string outcome, string closureReason, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var task = await LoadMutableAsync(id);
        task.EvidenceSummary = Required(evidenceSummary, "Closure evidence");
        task.Outcome = Required(outcome, "Outcome");
        task.ClosureReason = Required(closureReason, "Closure reason");
        task.Status = ComplianceWorkStatuses.PendingClosure;
        task.ClosureRequestedAtUtc = DateTime.UtcNow;
        task.ClosureRequestedBy = user;
        task.UpdatedAtUtc = DateTime.UtcNow;
        task.UpdatedBy = user;
        db.ComplianceAuditEvents.Add(CreateAudit(id, "ClosureRequested", user, reason, null, AuditSummary(task)));
        await db.SaveChangesAsync();
    }

    public async Task ApproveClosureAsync(int id, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var task = await db.ComplianceTasks.SingleAsync(item => item.Id == id);
        if (task.Status != ComplianceWorkStatuses.PendingClosure)
        {
            throw new InvalidOperationException("Only pending-closure work can be approved.");
        }
        var approvals = await LoadClosureApprovalsAsync(id);
        if (approvals.Any(item => string.Equals(item.Approver, user, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The same approver cannot approve closure twice.");
        }
        db.ComplianceApprovals.Add(new ComplianceApproval
        {
            TargetEntityType = nameof(ComplianceTask),
            TargetEntityId = id,
            Decision = ComplianceStatuses.Closed,
            Approver = user,
            Reason = reason.Trim()
        });
        var count = approvals.Count + 1;
        if (count >= RequiredClosureApprovals(task))
        {
            task.Status = ComplianceStatuses.Closed;
            task.ClosedAtUtc = DateTime.UtcNow;
            task.ClosedBy = user;
        }
        db.ComplianceAuditEvents.Add(CreateAudit(id, "ClosureApprovalRecorded", user, reason, null, new
        {
            task.Status,
            ApprovalCount = count,
            RequiredApprovals = RequiredClosureApprovals(task)
        }));
        await db.SaveChangesAsync();
    }

    public static int RequiredClosureApprovals(ComplianceTask task)
        => task.Priority == "High" || ComplianceTaskTypes.Material.Contains(task.TaskType, StringComparer.Ordinal) ? 2 : 1;

    private async Task<ComplianceTask> LoadMutableAsync(int id)
    {
        var task = await db.ComplianceTasks.SingleAsync(item => item.Id == id);
        if (task.Status is ComplianceStatuses.Closed or ComplianceStatuses.Withdrawn)
        {
            throw new InvalidOperationException("Closed or withdrawn work cannot be changed.");
        }
        return task;
    }

    private async Task<List<ComplianceApproval>> LoadClosureApprovalsAsync(int id)
        => await db.ComplianceApprovals.AsNoTracking()
            .Where(item => item.TargetEntityType == nameof(ComplianceTask) &&
                           item.TargetEntityId == id &&
                           item.Decision == ComplianceStatuses.Closed)
            .OrderBy(item => item.DecidedAtUtc)
            .ToListAsync();

    private static void SetLegacyLink(ComplianceTask task)
    {
        if (task.RmcpControlId is not null) { task.LinkedEntityType = nameof(RmcpControl); task.LinkedEntityId = task.RmcpControlId; }
        else if (task.RmcpVersionId is not null) { task.LinkedEntityType = nameof(RmcpVersion); task.LinkedEntityId = task.RmcpVersionId; }
        else if (task.BusinessRiskAssessmentId is not null) { task.LinkedEntityType = nameof(BusinessRiskAssessment); task.LinkedEntityId = task.BusinessRiskAssessmentId; }
        else if (task.ClientRiskAssessmentId is not null) { task.LinkedEntityType = nameof(ClientRiskAssessment); task.LinkedEntityId = task.ClientRiskAssessmentId; }
        else if (task.ClientId is not null) { task.LinkedEntityType = nameof(Client); task.LinkedEntityId = task.ClientId; }
    }

    private static object AuditSummary(ComplianceTask task) => new
    {
        task.Id, task.TaskType, task.Title, task.Owner, task.DueDate, task.Priority, task.Status,
        task.ClientId, task.ClientRiskAssessmentId, task.BusinessRiskAssessmentId, task.RmcpVersionId, task.RmcpControlId,
        task.EvidenceSummary, task.Outcome, task.ClosureReason, task.EscalatedAtUtc, task.ClosedAtUtc
    };
    private static ComplianceAuditEvent CreateAudit(int id, string action, string user, string reason, string? oldJson, object value)
        => new()
        {
            EntityType = nameof(ComplianceTask), EntityId = id, Action = action, UserName = user,
            Reason = reason.Trim(), OldValueJson = oldJson,
            NewValueJson = JsonSerializer.Serialize(value, AuditOptions), TimestampUtc = DateTime.UtcNow
        };
    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ValidationException("A reason is required.");
    }
    private static string RequireUser(string? user)
        => string.IsNullOrWhiteSpace(user) ? throw new ValidationException("The current user identity is required.") : user.Trim();
    private static string Required(string? value, string label, int? max = null)
    {
        var result = value?.Trim();
        if (string.IsNullOrWhiteSpace(result)) throw new ValidationException($"{label} is required.");
        if (max is not null && result.Length > max) throw new ValidationException($"{label} cannot exceed {max} characters.");
        return result;
    }
    private static string Allowed(string? value, IReadOnlyList<string> allowed, string label)
        => allowed.Contains(value ?? "", StringComparer.Ordinal) ? value! : throw new ValidationException($"Select a valid {label}.");
    private static string? NormalizeOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ComplianceWorkListItem(int Id, string TaskType, string Title, string? Owner, DateOnly? DueDate,
    string Priority, string Status, int? ClientId, string? ClientName, bool IsOverdue);
public sealed record ComplianceWorkPageModel(ComplianceTask Task, IReadOnlyList<ComplianceApproval> ClosureApprovals, int RequiredClosureApprovals);
public sealed record ComplianceWorkOption(int Id, string Label);
public sealed record ComplianceWorkOptions(IReadOnlyList<ComplianceWorkOption> Clients, IReadOnlyList<ComplianceWorkOption> Bras,
    IReadOnlyList<ComplianceWorkOption> RmcpVersions, IReadOnlyList<ComplianceWorkOption> RmcpControls);

public sealed class ComplianceWorkEditModel
{
    public int? Id { get; set; }
    public string TaskType { get; set; } = ComplianceTaskTypes.Remediation;
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Owner { get; set; } = "";
    public DateOnly? DueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
    public string Priority { get; set; } = "Normal";
    public string Status { get; set; } = ComplianceWorkStatuses.Open;
    public int? ClientId { get; set; }
    public int? ClientRiskAssessmentId { get; set; }
    public int? BusinessRiskAssessmentId { get; set; }
    public int? RmcpVersionId { get; set; }
    public int? RmcpControlId { get; set; }
    public string? EvidenceSummary { get; set; }
    public string? Outcome { get; set; }

    public static ComplianceWorkEditModel FromEntity(ComplianceTask task) => new()
    {
        Id = task.Id, TaskType = task.TaskType, Title = task.Title, Description = task.Description, Owner = task.Owner ?? "",
        DueDate = task.DueDate, Priority = task.Priority, Status = task.Status, ClientId = task.ClientId,
        ClientRiskAssessmentId = task.ClientRiskAssessmentId, BusinessRiskAssessmentId = task.BusinessRiskAssessmentId,
        RmcpVersionId = task.RmcpVersionId, RmcpControlId = task.RmcpControlId,
        EvidenceSummary = task.EvidenceSummary, Outcome = task.Outcome
    };
}

public static class ComplianceWorkPriorities
{
    public static readonly IReadOnlyList<string> All = ["Low", "Normal", "High"];
}
public static class ComplianceWorkViews
{
    public const string Overdue = "Overdue";
    public const string HighPriority = "HighPriority";
    public const string Unresolved = "Unresolved";
}
