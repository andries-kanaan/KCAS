using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class RmcpService(ApplicationDbContext db)
{
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<RmcpDashboardModel> LoadDashboardAsync()
        => new(
            await db.RmcpVersions.AsNoTracking()
                .Include(item => item.BusinessRiskAssessment)
                .OrderByDescending(item => item.Id)
                .ToListAsync(),
            await db.BusinessRiskAssessments.AsNoTracking()
                .Where(item => item.Status == ComplianceStatuses.Approved ||
                               item.Status == ComplianceStatuses.Active ||
                               item.Status == ComplianceStatuses.Superseded)
                .OrderByDescending(item => item.AssessmentYear)
                .ToListAsync());

    public async Task<RmcpPageModel?> LoadAsync(int id)
    {
        var version = await db.RmcpVersions.AsNoTracking()
            .Include(item => item.BusinessRiskAssessment)
            .ThenInclude(item => item!.Items)
            .Include(item => item.Controls.OrderBy(control => control.SortOrder))
            .ThenInclude(item => item.BusinessRiskItem)
            .SingleOrDefaultAsync(item => item.Id == id);
        if (version is null)
        {
            return null;
        }

        var approvals = await LoadApprovalsAsync(id);
        return new RmcpPageModel(version, approvals);
    }

    public async Task<int> CreateDraftAsync(int businessRiskAssessmentId, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var bra = await db.BusinessRiskAssessments.AsNoTracking().SingleOrDefaultAsync(item => item.Id == businessRiskAssessmentId)
            ?? throw new KeyNotFoundException("Business Risk Assessment not found.");
        if (bra.Status is not (ComplianceStatuses.Approved or ComplianceStatuses.Active or ComplianceStatuses.Superseded))
        {
            throw new InvalidOperationException("Select an approved or effective BRA.");
        }

        var version = new RmcpVersion
        {
            BusinessRiskAssessmentId = bra.Id,
            Title = "Kanaan Risk Management and Compliance Programme",
            VersionReference = $"Working draft {DateTime.UtcNow:yyyy}",
            Scope = "Kanaan Trust's accountable-institution and financial-services activities.",
            Owner = "Key Individuals",
            ChangeSummary = "Initial controlled KCAS version.",
            PreparedBy = user,
            UpdatedBy = user,
            Controls = RmcpControlDomains.All.Select((domain, index) => new RmcpControl
            {
                Domain = domain,
                Code = $"RMCP-{index + 1:00}",
                Title = RmcpControlDomains.Display(domain),
                Owner = "Key Individuals",
                Frequency = "Ongoing",
                SortOrder = index + 1
            }).ToList()
        };
        db.RmcpVersions.Add(version);
        await db.SaveChangesAsync();
        await AuditAsync(version.Id, "Created", user, reason, AuditSummary(version));
        return version.Id;
    }

    public async Task SaveDraftAsync(RmcpEditModel model, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var version = await db.RmcpVersions.Include(item => item.Controls).SingleAsync(item => item.Id == model.Id);
        EnsureStatus(version, ComplianceStatuses.Draft);
        var oldJson = JsonSerializer.Serialize(AuditSummary(version), SnapshotOptions);

        version.Title = Required(model.Title, "RMCP title", 191);
        version.VersionReference = Required(model.VersionReference, "Version reference", 64);
        version.Scope = Required(model.Scope, "Scope");
        version.Owner = Required(model.Owner, "Owner", 191);
        if (model.ReviewMonths is < 1 or > 60)
        {
            throw new ValidationException("Review cycle must be between 1 and 60 months.");
        }
        version.ReviewMonths = model.ReviewMonths;
        version.SignedDocumentLocation = Normalize(model.SignedDocumentLocation);
        version.ApprovalResolutionLocation = Normalize(model.ApprovalResolutionLocation);
        version.ChangeSummary = Required(model.ChangeSummary, "Change summary");
        version.UpdatedAtUtc = DateTime.UtcNow;
        version.UpdatedBy = user;

        var validRiskIds = await db.BusinessRiskItems.AsNoTracking()
            .Where(item => item.BusinessRiskAssessmentId == version.BusinessRiskAssessmentId)
            .Select(item => item.Id)
            .ToHashSetAsync();
        if (model.Controls.Any(item => item.BusinessRiskItemId is not null && !validRiskIds.Contains(item.BusinessRiskItemId.Value)))
        {
            throw new ValidationException("A linked BRA risk does not belong to this RMCP's Business Risk Assessment.");
        }

        db.RmcpControls.RemoveRange(version.Controls);
        version.Controls = model.Controls.Select((item, index) => new RmcpControl
        {
            Domain = Allowed(item.Domain, RmcpControlDomains.All, "control domain"),
            Code = Required(item.Code, "Control code", 64),
            Title = Required(item.Title, "Control title", 191),
            BusinessRiskItemId = item.BusinessRiskItemId,
            ProcedureSummary = Normalize(item.ProcedureSummary),
            Owner = Required(item.Owner, "Control owner", 191),
            Frequency = Required(item.Frequency, "Monitoring frequency", 64),
            EvidenceExpectation = Normalize(item.EvidenceExpectation),
            MonitoringMethod = Normalize(item.MonitoringMethod),
            EscalationProcedure = Normalize(item.EscalationProcedure),
            HasGap = item.HasGap,
            GapDescription = NormalizeOrNull(item.GapDescription),
            TreatmentOwner = NormalizeOrNull(item.TreatmentOwner),
            TreatmentDueDate = item.TreatmentDueDate,
            SortOrder = index + 1
        }).ToList();
        db.ComplianceAuditEvents.Add(CreateAudit(version.Id, "Updated", user, reason, oldJson, AuditSummary(version)));
        await db.SaveChangesAsync();
    }

    public async Task SubmitAsync(int id, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var version = await db.RmcpVersions
            .Include(item => item.Controls)
            .Include(item => item.BusinessRiskAssessment).ThenInclude(item => item!.Items)
            .SingleAsync(item => item.Id == id);
        EnsureStatus(version, ComplianceStatuses.Draft);
        ValidateForSubmission(version);

        foreach (var control in version.Controls.Where(item => item.HasGap))
        {
            var task = new ComplianceTask
            {
                Title = $"RMCP treatment: {control.Code} {control.Title}",
                Description = control.GapDescription,
                Owner = control.TreatmentOwner,
                DueDate = control.TreatmentDueDate,
                Priority = "High",
                Status = ComplianceStatuses.Draft,
                LinkedEntityType = nameof(RmcpControl),
                LinkedEntityId = control.Id,
                UpdatedBy = user
            };
            db.ComplianceTasks.Add(task);
            await db.SaveChangesAsync();
            control.ComplianceTaskId = task.Id;
        }

        version.Status = ComplianceStatuses.Review;
        version.SubmittedAtUtc = DateTime.UtcNow;
        version.UpdatedAtUtc = DateTime.UtcNow;
        version.UpdatedBy = user;
        db.ComplianceAuditEvents.Add(CreateAudit(id, "Submitted", user, reason, null, AuditSummary(version)));
        await db.SaveChangesAsync();
    }

    public async Task ApproveAsync(int id, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var version = await db.RmcpVersions
            .Include(item => item.Controls).ThenInclude(item => item.BusinessRiskItem)
            .Include(item => item.BusinessRiskAssessment)
            .SingleAsync(item => item.Id == id);
        EnsureStatus(version, ComplianceStatuses.Review);
        var approvals = await LoadApprovalsAsync(id);
        if (approvals.Any(item => string.Equals(item.Approver, user, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The same KI cannot approve this RMCP twice.");
        }

        var approval = new ComplianceApproval
        {
            TargetEntityType = nameof(RmcpVersion),
            TargetEntityId = id,
            Decision = ComplianceStatuses.Approved,
            Approver = user,
            Reason = reason.Trim()
        };
        db.ComplianceApprovals.Add(approval);
        approvals.Add(approval);
        if (approvals.Count >= 2)
        {
            version.Status = ComplianceStatuses.Approved;
            version.ApprovedAtUtc = DateTime.UtcNow;
            version.SnapshotJson = CreateFrozenSnapshot(version, approvals);
        }

        db.ComplianceAuditEvents.Add(CreateAudit(id, "KIApprovalRecorded", user, reason, null, new
        {
            version.Status,
            ApprovalCount = approvals.Count
        }));
        await db.SaveChangesAsync();
    }

    public async Task ActivateAsync(int id, DateOnly effectiveDate, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var version = await db.RmcpVersions.SingleAsync(item => item.Id == id);
        EnsureStatus(version, ComplianceStatuses.Approved);
        if (string.IsNullOrWhiteSpace(version.SnapshotJson))
        {
            throw new InvalidOperationException("The approved RMCP has no frozen snapshot.");
        }

        foreach (var prior in await db.RmcpVersions.Where(item => item.Id != id && item.Status == ComplianceStatuses.Active).ToListAsync())
        {
            prior.Status = ComplianceStatuses.Superseded;
            prior.UpdatedAtUtc = DateTime.UtcNow;
            db.ComplianceAuditEvents.Add(CreateAudit(prior.Id, "Superseded", user, reason, null, new { prior.Status }));
        }

        version.Status = ComplianceStatuses.Active;
        version.EffectiveDate = effectiveDate;
        version.NextReviewDate = effectiveDate.AddMonths(version.ReviewMonths);
        version.ActivatedAtUtc = DateTime.UtcNow;
        version.UpdatedAtUtc = DateTime.UtcNow;
        version.UpdatedBy = user;
        db.ComplianceAuditEvents.Add(CreateAudit(id, "Activated", user, reason, null, new
        {
            version.Status,
            version.EffectiveDate,
            version.NextReviewDate
        }));
        await db.SaveChangesAsync();
    }

    public async Task<RmcpPrintableModel> LoadPrintableAsync(int id)
    {
        var version = await db.RmcpVersions.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id)
            ?? throw new KeyNotFoundException("RMCP version not found.");
        if (version.Status is not (ComplianceStatuses.Approved or ComplianceStatuses.Active or ComplianceStatuses.Superseded) ||
            string.IsNullOrWhiteSpace(version.SnapshotJson))
        {
            throw new InvalidOperationException("Only a frozen approved RMCP can be printed.");
        }
        return new(version.Title, version.Status, version.SnapshotJson);
    }

    private async Task<List<ComplianceApproval>> LoadApprovalsAsync(int id)
        => await db.ComplianceApprovals.AsNoTracking()
            .Where(item => item.TargetEntityType == nameof(RmcpVersion) &&
                           item.TargetEntityId == id &&
                           item.Decision == ComplianceStatuses.Approved)
            .OrderBy(item => item.DecidedAtUtc)
            .ToListAsync();

    private static void ValidateForSubmission(RmcpVersion version)
    {
        Required(version.SignedDocumentLocation, "Signed Word/PDF source");
        Required(version.ApprovalResolutionLocation, "Approval resolution location");
        foreach (var domain in RmcpControlDomains.All)
        {
            if (!version.Controls.Any(item => item.Domain == domain))
            {
                throw new ValidationException($"Add a control for {RmcpControlDomains.Display(domain)}.");
            }
        }
        foreach (var control in version.Controls)
        {
            Required(control.ProcedureSummary, $"{control.Code} procedure");
            Required(control.EvidenceExpectation, $"{control.Code} evidence expectation");
            Required(control.MonitoringMethod, $"{control.Code} monitoring method");
            Required(control.EscalationProcedure, $"{control.Code} escalation procedure");
            if (control.HasGap &&
                (string.IsNullOrWhiteSpace(control.GapDescription) ||
                 string.IsNullOrWhiteSpace(control.TreatmentOwner) ||
                 control.TreatmentDueDate is null))
            {
                throw new ValidationException($"{control.Code} requires a gap description, treatment owner and due date.");
            }
        }

        var materialRiskIds = version.BusinessRiskAssessment!.Items
            .Where(item => item.ResidualRating == BusinessRiskRatings.High ||
                           item.TreatmentDecision != BusinessRiskTreatmentDecisions.Accept)
            .Select(item => item.Id)
            .ToHashSet();
        var mappedRiskIds = version.Controls.Where(item => item.BusinessRiskItemId is not null)
            .Select(item => item.BusinessRiskItemId!.Value)
            .ToHashSet();
        var unmapped = materialRiskIds.Except(mappedRiskIds).Count();
        if (unmapped > 0)
        {
            throw new ValidationException($"{unmapped} material BRA risk(s) still require a linked control.");
        }
    }

    private static string CreateFrozenSnapshot(RmcpVersion version, IReadOnlyList<ComplianceApproval> approvals)
        => JsonSerializer.Serialize(new
        {
            version.Id,
            version.Title,
            version.VersionReference,
            version.Status,
            version.Scope,
            version.Owner,
            version.ReviewMonths,
            version.SignedDocumentLocation,
            version.ApprovalResolutionLocation,
            version.ChangeSummary,
            Bra = new
            {
                version.BusinessRiskAssessmentId,
                version.BusinessRiskAssessment!.Name,
                version.BusinessRiskAssessment.AsAtDate,
                version.BusinessRiskAssessment.SnapshotJson
            },
            Controls = version.Controls.OrderBy(item => item.SortOrder).Select(item => new
            {
                item.Domain,
                item.Code,
                item.Title,
                item.BusinessRiskItemId,
                BraRisk = item.BusinessRiskItem?.RiskStatement,
                item.ProcedureSummary,
                item.Owner,
                item.Frequency,
                item.EvidenceExpectation,
                item.MonitoringMethod,
                item.EscalationProcedure,
                item.HasGap,
                item.GapDescription,
                item.TreatmentOwner,
                item.TreatmentDueDate,
                item.ComplianceTaskId
            }),
            Approvals = approvals.Select(item => new { item.Approver, item.Reason, item.DecidedAtUtc })
        }, SnapshotOptions);

    private static object AuditSummary(RmcpVersion version) => new
    {
        version.Id,
        version.BusinessRiskAssessmentId,
        version.Title,
        version.VersionReference,
        version.Status,
        version.Owner,
        version.ReviewMonths,
        version.SignedDocumentLocation,
        version.ApprovalResolutionLocation,
        Controls = version.Controls.Select(item => new
        {
            item.Domain,
            item.Code,
            item.Title,
            item.BusinessRiskItemId,
            item.HasGap,
            item.ComplianceTaskId
        })
    };

    private async Task AuditAsync(int id, string action, string user, string reason, object value)
    {
        db.ComplianceAuditEvents.Add(CreateAudit(id, action, user, reason, null, value));
        await db.SaveChangesAsync();
    }

    private static ComplianceAuditEvent CreateAudit(int id, string action, string user, string reason, string? oldJson, object value)
        => new()
        {
            EntityType = nameof(RmcpVersion),
            EntityId = id,
            Action = action,
            UserName = user,
            Reason = reason.Trim(),
            TimestampUtc = DateTime.UtcNow,
            OldValueJson = oldJson,
            NewValueJson = JsonSerializer.Serialize(value, SnapshotOptions)
        };

    private static void EnsureStatus(RmcpVersion version, string status)
    {
        if (version.Status != status)
        {
            throw new InvalidOperationException($"This action requires a {status} RMCP.");
        }
    }
    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ValidationException("A reason is required.");
    }
    private static string RequireUser(string? user)
        => string.IsNullOrWhiteSpace(user) ? throw new ValidationException("The current user identity is required.") : user.Trim();
    private static string Required(string? value, string label, int? max = null)
    {
        var result = Normalize(value);
        if (string.IsNullOrWhiteSpace(result)) throw new ValidationException($"{label} is required.");
        if (max is not null && result.Length > max) throw new ValidationException($"{label} cannot exceed {max} characters.");
        return result;
    }
    private static string Allowed(string? value, IReadOnlyList<string> allowed, string label)
        => allowed.Contains(value ?? "", StringComparer.Ordinal) ? value! : throw new ValidationException($"Select a valid {label}.");
    private static string Normalize(string? value) => value?.Trim() ?? "";
    private static string? NormalizeOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record RmcpDashboardModel(IReadOnlyList<RmcpVersion> Versions, IReadOnlyList<BusinessRiskAssessment> EligibleBras);
public sealed record RmcpPageModel(RmcpVersion Version, IReadOnlyList<ComplianceApproval> Approvals);
public sealed record RmcpPrintableModel(string Title, string Status, string SnapshotJson);

public sealed class RmcpEditModel
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string VersionReference { get; set; } = "";
    public string Scope { get; set; } = "";
    public string Owner { get; set; } = "";
    public int ReviewMonths { get; set; } = 12;
    public string SignedDocumentLocation { get; set; } = "";
    public string ApprovalResolutionLocation { get; set; } = "";
    public string ChangeSummary { get; set; } = "";
    public List<RmcpControlEditModel> Controls { get; set; } = [];

    public static RmcpEditModel FromEntity(RmcpVersion version) => new()
    {
        Id = version.Id,
        Title = version.Title,
        VersionReference = version.VersionReference,
        Scope = version.Scope,
        Owner = version.Owner,
        ReviewMonths = version.ReviewMonths,
        SignedDocumentLocation = version.SignedDocumentLocation,
        ApprovalResolutionLocation = version.ApprovalResolutionLocation,
        ChangeSummary = version.ChangeSummary,
        Controls = version.Controls.OrderBy(item => item.SortOrder).Select(RmcpControlEditModel.FromEntity).ToList()
    };
}

public sealed class RmcpControlEditModel
{
    public string Domain { get; set; } = "";
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int? BusinessRiskItemId { get; set; }
    public string ProcedureSummary { get; set; } = "";
    public string Owner { get; set; } = "";
    public string Frequency { get; set; } = "";
    public string EvidenceExpectation { get; set; } = "";
    public string MonitoringMethod { get; set; } = "";
    public string EscalationProcedure { get; set; } = "";
    public bool HasGap { get; set; }
    public string? GapDescription { get; set; }
    public string? TreatmentOwner { get; set; }
    public DateOnly? TreatmentDueDate { get; set; }

    public static RmcpControlEditModel FromEntity(RmcpControl item) => new()
    {
        Domain = item.Domain,
        Code = item.Code,
        Title = item.Title,
        BusinessRiskItemId = item.BusinessRiskItemId,
        ProcedureSummary = item.ProcedureSummary,
        Owner = item.Owner,
        Frequency = item.Frequency,
        EvidenceExpectation = item.EvidenceExpectation,
        MonitoringMethod = item.MonitoringMethod,
        EscalationProcedure = item.EscalationProcedure,
        HasGap = item.HasGap,
        GapDescription = item.GapDescription,
        TreatmentOwner = item.TreatmentOwner,
        TreatmentDueDate = item.TreatmentDueDate
    };
}
