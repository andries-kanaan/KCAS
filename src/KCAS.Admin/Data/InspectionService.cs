using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class InspectionService(ApplicationDbContext db)
{
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public Task<List<InspectionCase>> LoadListAsync()
        => db.InspectionCases.AsNoTracking()
            .Include(item => item.Items)
            .Include(item => item.ReadinessChecks)
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync();

    public Task<InspectionCase?> LoadAsync(int id)
        => db.InspectionCases.AsNoTracking()
            .Include(item => item.Items.OrderBy(request => request.SortOrder))
            .Include(item => item.ReadinessChecks.OrderBy(check => check.CheckType))
            .SingleOrDefaultAsync(item => item.Id == id);

    public async Task<int> CreateDraftAsync(InspectionCaseEditModel model, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        ValidateDates(model.RequestDate, model.DueDate, model.AsAtDate);
        var inspection = new InspectionCase
        {
            Reference = Required(model.Reference, "Reference", 96),
            Title = Required(model.Title, "Title", 240),
            RequestingAuthority = Required(model.RequestingAuthority, "Requesting authority", 191),
            AsAtDate = model.AsAtDate,
            RequestDate = model.RequestDate,
            DueDate = model.DueDate,
            Scope = Required(model.Scope, "Scope"),
            Coordinator = Required(model.Coordinator, "Coordinator", 191),
            Notes = NormalizeOrNull(model.Notes),
            CreatedBy = user,
            UpdatedBy = user,
            ReadinessChecks = InspectionReadinessCheckTypes.All.Select(type => new InspectionReadinessCheck
            {
                CheckType = type
            }).ToList()
        };
        db.InspectionCases.Add(inspection);
        await db.SaveChangesAsync();
        await AuditAsync(inspection.Id, "Created", user, reason, Summary(inspection));
        return inspection.Id;
    }

    public async Task SaveDraftAsync(InspectionCaseEditModel model, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var inspection = await LoadMutableAsync(model.Id ?? throw new ValidationException("Inspection case ID is required."));
        ValidateDates(model.RequestDate, model.DueDate, model.AsAtDate);
        var oldJson = JsonSerializer.Serialize(Summary(inspection), SnapshotOptions);
        inspection.Reference = Required(model.Reference, "Reference", 96);
        inspection.Title = Required(model.Title, "Title", 240);
        inspection.RequestingAuthority = Required(model.RequestingAuthority, "Requesting authority", 191);
        inspection.AsAtDate = model.AsAtDate;
        inspection.RequestDate = model.RequestDate;
        inspection.DueDate = model.DueDate;
        inspection.Scope = Required(model.Scope, "Scope");
        inspection.Coordinator = Required(model.Coordinator, "Coordinator", 191);
        inspection.Notes = NormalizeOrNull(model.Notes);
        inspection.UpdatedAtUtc = DateTime.UtcNow;
        inspection.UpdatedBy = user;
        db.ComplianceAuditEvents.Add(CreateAudit(inspection.Id, "Updated", user, reason, oldJson, Summary(inspection)));
        await db.SaveChangesAsync();
    }

    public async Task<int> SaveRequestItemAsync(int inspectionId, InspectionRequestItemEditModel model, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var inspection = await LoadMutableAsync(inspectionId);
        var category = Allowed(model.Category, InspectionEvidenceCategories.All, "evidence category");
        var title = Required(model.Title, "Request item title", 240);
        var owner = Required(model.Owner, "Request item owner", 191);
        var status = Allowed(model.Status, InspectionItemEditModelStatuses.All, "request status");
        var evidenceTitle = NormalizeOrNull(model.EvidenceTitle);
        var evidenceLocation = NormalizeOrNull(model.EvidenceLocation);
        if (status == InspectionItemStatuses.Ready)
        {
            Required(evidenceTitle, "Evidence title");
            Required(evidenceLocation, "Evidence location");
        }
        InspectionRequestItem item;
        string action;
        if (model.Id is null)
        {
            item = new InspectionRequestItem
            {
                InspectionCaseId = inspectionId,
                SortOrder = inspection.Items.Count + 1
            };
            db.InspectionRequestItems.Add(item);
            action = "RequestItemCreated";
        }
        else
        {
            item = await db.InspectionRequestItems.SingleAsync(row => row.Id == model.Id && row.InspectionCaseId == inspectionId);
            action = "RequestItemUpdated";
        }

        item.Category = category;
        item.Title = title;
        item.Description = NormalizeOrNull(model.Description);
        item.Owner = owner;
        item.DueDate = model.DueDate;
        item.Status = status;
        item.EvidenceTitle = evidenceTitle;
        item.EvidenceLocation = evidenceLocation;
        item.LinkedEntityType = NormalizeOrNull(model.LinkedEntityType);
        item.LinkedEntityId = model.LinkedEntityId;
        item.ReviewNotes = NormalizeOrNull(model.ReviewNotes);
        if (item.Status == InspectionItemStatuses.Ready)
        {
            item.CompletedAtUtc ??= DateTime.UtcNow;
            item.CompletedBy = user;
        }
        else
        {
            item.CompletedAtUtc = null;
            item.CompletedBy = null;
        }
        await db.SaveChangesAsync();
        db.ComplianceAuditEvents.Add(CreateAudit(inspectionId, action, user, reason, null, new
        {
            item.Id, item.Category, item.Title, item.Owner, item.DueDate, item.Status,
            item.EvidenceTitle, item.EvidenceLocation, item.LinkedEntityType, item.LinkedEntityId
        }));
        await db.SaveChangesAsync();
        return item.Id;
    }

    public async Task RecordReadinessCheckAsync(int inspectionId, int checkId, string status, string? evidenceLocation,
        string? notes, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var validatedStatus = Allowed(status, InspectionCheckStatuses.All, "check status");
        var validatedEvidence = NormalizeOrNull(evidenceLocation);
        var validatedNotes = NormalizeOrNull(notes);
        if (validatedStatus is InspectionCheckStatuses.Passed or InspectionCheckStatuses.Failed)
        {
            Required(validatedEvidence, "Readiness evidence location");
            Required(validatedNotes, "Readiness check notes");
        }
        await LoadMutableAsync(inspectionId);
        var check = await db.InspectionReadinessChecks.SingleAsync(item => item.Id == checkId && item.InspectionCaseId == inspectionId);
        check.Status = validatedStatus;
        check.EvidenceLocation = validatedEvidence;
        check.Notes = validatedNotes;
        if (check.Status is InspectionCheckStatuses.Passed or InspectionCheckStatuses.Failed)
        {
            check.TestedAtUtc = DateTime.UtcNow;
            check.TestedBy = user;
        }
        else
        {
            check.TestedAtUtc = null;
            check.TestedBy = null;
        }
        db.ComplianceAuditEvents.Add(CreateAudit(inspectionId, "ReadinessCheckRecorded", user, reason, null, new
        {
            check.Id, check.CheckType, check.Status, check.EvidenceLocation, check.Notes, check.TestedAtUtc, check.TestedBy
        }));
        await db.SaveChangesAsync();
    }

    public async Task FreezeAsync(int id, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var inspection = await db.InspectionCases
            .Include(item => item.Items)
            .Include(item => item.ReadinessChecks)
            .SingleAsync(item => item.Id == id);
        if (inspection.Status is InspectionStatuses.Frozen or InspectionStatuses.Closed)
        {
            throw new InvalidOperationException("This inspection record is already frozen.");
        }
        if (inspection.Items.Count == 0)
        {
            throw new ValidationException("Add at least one inspection request item.");
        }
        if (inspection.Items.Any(item => item.Status is not (InspectionItemStatuses.Ready or InspectionItemStatuses.NotApplicable)))
        {
            throw new ValidationException("Every request item must be ready or marked not applicable.");
        }
        if (inspection.ReadinessChecks.Any(item => item.Status != InspectionCheckStatuses.Passed))
        {
            throw new ValidationException("Every access, recovery, performance and rollout readiness check must have recorded passing evidence.");
        }

        inspection.Status = InspectionStatuses.Frozen;
        inspection.FrozenAtUtc = DateTime.UtcNow;
        inspection.UpdatedAtUtc = DateTime.UtcNow;
        inspection.UpdatedBy = user;
        inspection.SnapshotJson = await BuildSnapshotAsync(inspection);
        db.ComplianceAuditEvents.Add(CreateAudit(id, "Frozen", user, reason, null, new
        {
            inspection.Status, inspection.AsAtDate, inspection.FrozenAtUtc,
            RequestItems = inspection.Items.Count, Checks = inspection.ReadinessChecks.Count
        }));
        await db.SaveChangesAsync();
    }

    public async Task CloseAsync(int id, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var inspection = await db.InspectionCases.SingleAsync(item => item.Id == id);
        if (inspection.Status != InspectionStatuses.Frozen || string.IsNullOrWhiteSpace(inspection.SnapshotJson))
        {
            throw new InvalidOperationException("Only a frozen inspection pack can be closed.");
        }
        inspection.Status = InspectionStatuses.Closed;
        inspection.UpdatedAtUtc = DateTime.UtcNow;
        inspection.UpdatedBy = user;
        db.ComplianceAuditEvents.Add(CreateAudit(id, "Closed", user, reason, null, new { inspection.Status }));
        await db.SaveChangesAsync();
    }

    public async Task<InspectionPrintableModel> LoadPrintableAsync(int id)
    {
        var inspection = await db.InspectionCases.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id)
            ?? throw new KeyNotFoundException("Inspection case not found.");
        if (inspection.Status is not (InspectionStatuses.Frozen or InspectionStatuses.Closed) ||
            string.IsNullOrWhiteSpace(inspection.SnapshotJson))
        {
            throw new InvalidOperationException("Only a frozen inspection pack can be exported.");
        }
        return new(inspection.Reference, inspection.Title, inspection.Status, inspection.SnapshotJson);
    }

    private async Task<string> BuildSnapshotAsync(InspectionCase inspection)
    {
        var cutoff = inspection.AsAtDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var clients = await db.Clients.AsNoTracking()
            .Where(item => item.CreatedAtUtc <= cutoff)
            .OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.KanaanId, item.DisplayName, item.ClientCategory, item.IsActive })
            .ToListAsync();
        var assessments = await db.ClientRiskAssessments.AsNoTracking()
            .Where(item => item.CreatedAtUtc <= cutoff && item.Status != ClientRiskAssessmentStatuses.Draft)
            .OrderBy(item => item.ClientId).ThenBy(item => item.Id)
            .Select(item => new
            {
                item.Id, item.ClientId, item.Status, item.FinalRating, item.EffectiveDate, item.NextReviewDate,
                item.RiskMethodologyVersionId, HasFrozenRecord = item.SnapshotJson != null
            }).ToListAsync();
        var bras = await db.BusinessRiskAssessments.AsNoTracking()
            .Where(item => item.ApprovedAtUtc != null && item.ApprovedAtUtc <= cutoff)
            .OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.Name, item.AssessmentYear, item.AsAtDate, item.Status, HasFrozenRecord = item.SnapshotJson != null })
            .ToListAsync();
        var rmcps = await db.RmcpVersions.AsNoTracking()
            .Where(item => item.ApprovedAtUtc != null && item.ApprovedAtUtc <= cutoff)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id, item.Title, item.VersionReference, item.Status, item.EffectiveDate, item.NextReviewDate,
                item.SignedDocumentLocation, item.ApprovalResolutionLocation, HasFrozenRecord = item.SnapshotJson != null
            }).ToListAsync();
        var approvals = await db.ComplianceApprovals.AsNoTracking()
            .Where(item => item.DecidedAtUtc <= cutoff)
            .OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.TargetEntityType, item.TargetEntityId, item.Decision, item.Approver, item.DecidedAtUtc, item.Reason })
            .ToListAsync();
        var work = await db.ComplianceTasks.AsNoTracking()
            .Where(item => item.CreatedAtUtc <= cutoff)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id, item.TaskType, item.Title, item.Owner, item.DueDate, item.Priority, item.Status,
                item.ClientId, item.BusinessRiskAssessmentId, item.RmcpVersionId, item.RmcpControlId,
                item.EvidenceSummary, item.Outcome, item.ClosedAtUtc
            }).ToListAsync();
        var documents = await db.ControlledDocuments.AsNoTracking()
            .Where(item => item.EffectiveDate == null || item.EffectiveDate <= inspection.AsAtDate)
            .OrderBy(item => item.Id)
            .Select(item => new { item.Id, item.DocumentType, item.Title, item.VersionReference, item.Status, item.EffectiveDate, item.Location })
            .ToListAsync();
        var evidence = await db.ComplianceEvidence.AsNoTracking()
            .Where(item => item.VerifiedDate != null && item.VerifiedDate <= inspection.AsAtDate)
            .OrderBy(item => item.Id)
            .Select(item => new
            {
                item.Id, item.EvidenceType, item.Title, item.Location, item.VerifiedDate,
                item.LinkedEntityType, item.LinkedEntityId
            }).ToListAsync();
        var auditCount = await db.ComplianceAuditEvents.AsNoTracking().CountAsync(item => item.TimestampUtc <= cutoff);

        return JsonSerializer.Serialize(new
        {
            inspection.Id,
            inspection.Reference,
            inspection.Title,
            inspection.RequestingAuthority,
            inspection.AsAtDate,
            inspection.RequestDate,
            inspection.DueDate,
            inspection.Scope,
            inspection.Coordinator,
            inspection.Notes,
            FrozenAtUtc = DateTime.UtcNow,
            RequestItems = inspection.Items.OrderBy(item => item.SortOrder).Select(item => new
            {
                item.Category, item.Title, item.Description, item.Owner, item.DueDate, item.Status,
                item.EvidenceTitle, item.EvidenceLocation, item.LinkedEntityType, item.LinkedEntityId,
                item.ReviewNotes, item.CompletedAtUtc, item.CompletedBy
            }),
            ReadinessChecks = inspection.ReadinessChecks.OrderBy(item => item.CheckType).Select(item => new
            {
                item.CheckType, item.Status, item.EvidenceLocation, item.Notes, item.TestedAtUtc, item.TestedBy
            }),
            EvidenceIndex = new
            {
                Clients = clients,
                ClientAssessments = assessments,
                BusinessRiskAssessments = bras,
                RmcpVersions = rmcps,
                Approvals = approvals,
                Training = work.Where(item => item.TaskType == ComplianceTaskTypes.Training),
                MonitoringAndRemediation = work,
                ControlledDocuments = documents,
                ComplianceEvidence = evidence,
                AuditEventCount = auditCount
            },
            Summary = new
            {
                ClientCount = clients.Count,
                ActiveClientCount = clients.Count(item => item.IsActive),
                FrozenClientAssessmentCount = assessments.Count(item => item.HasFrozenRecord),
                ApprovedBraCount = bras.Count,
                ApprovedRmcpCount = rmcps.Count,
                OpenWorkCount = work.Count(item => item.Status != ComplianceStatuses.Closed && item.Status != ComplianceStatuses.Withdrawn),
                ClosedTrainingCount = work.Count(item => item.TaskType == ComplianceTaskTypes.Training && item.Status == ComplianceStatuses.Closed)
            }
        }, SnapshotOptions);
    }

    private async Task<InspectionCase> LoadMutableAsync(int id)
    {
        var inspection = await db.InspectionCases.Include(item => item.Items).SingleAsync(item => item.Id == id);
        if (inspection.Status is InspectionStatuses.Frozen or InspectionStatuses.Closed)
        {
            throw new InvalidOperationException("Frozen or closed inspection records are immutable.");
        }
        return inspection;
    }

    private async Task AuditAsync(int id, string action, string user, string reason, object value)
    {
        db.ComplianceAuditEvents.Add(CreateAudit(id, action, user, reason, null, value));
        await db.SaveChangesAsync();
    }
    private static ComplianceAuditEvent CreateAudit(int id, string action, string user, string reason, string? oldJson, object value)
        => new()
        {
            EntityType = nameof(InspectionCase), EntityId = id, Action = action, UserName = user,
            Reason = reason.Trim(), TimestampUtc = DateTime.UtcNow, OldValueJson = oldJson,
            NewValueJson = JsonSerializer.Serialize(value, SnapshotOptions)
        };
    private static object Summary(InspectionCase inspection) => new
    {
        inspection.Id, inspection.Reference, inspection.Title, inspection.RequestingAuthority, inspection.AsAtDate,
        inspection.RequestDate, inspection.DueDate, inspection.Status, inspection.Coordinator
    };
    private static void ValidateDates(DateOnly requestDate, DateOnly dueDate, DateOnly asAtDate)
    {
        if (dueDate < requestDate) throw new ValidationException("Due date cannot be before the request date.");
        if (asAtDate > dueDate) throw new ValidationException("As-at date cannot be after the inspection due date.");
    }
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

public sealed class InspectionCaseEditModel
{
    public int? Id { get; set; }
    public string Reference { get; set; } = "";
    public string Title { get; set; } = "";
    public string RequestingAuthority { get; set; } = "FSCA/FIC";
    public DateOnly AsAtDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly RequestDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public DateOnly DueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
    public string Scope { get; set; } = "";
    public string Coordinator { get; set; } = "";
    public string? Notes { get; set; }

    public static InspectionCaseEditModel FromEntity(InspectionCase item) => new()
    {
        Id = item.Id, Reference = item.Reference, Title = item.Title, RequestingAuthority = item.RequestingAuthority,
        AsAtDate = item.AsAtDate, RequestDate = item.RequestDate, DueDate = item.DueDate,
        Scope = item.Scope, Coordinator = item.Coordinator, Notes = item.Notes
    };
}

public sealed class InspectionRequestItemEditModel
{
    public int? Id { get; set; }
    public string Category { get; set; } = InspectionEvidenceCategories.Other;
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Owner { get; set; } = "";
    public DateOnly DueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(30));
    public string Status { get; set; } = InspectionItemStatuses.Open;
    public string? EvidenceTitle { get; set; }
    public string? EvidenceLocation { get; set; }
    public string? LinkedEntityType { get; set; }
    public int? LinkedEntityId { get; set; }
    public string? ReviewNotes { get; set; }

    public static InspectionRequestItemEditModel FromEntity(InspectionRequestItem item) => new()
    {
        Id = item.Id, Category = item.Category, Title = item.Title, Description = item.Description,
        Owner = item.Owner, DueDate = item.DueDate, Status = item.Status, EvidenceTitle = item.EvidenceTitle,
        EvidenceLocation = item.EvidenceLocation, LinkedEntityType = item.LinkedEntityType,
        LinkedEntityId = item.LinkedEntityId, ReviewNotes = item.ReviewNotes
    };
}

public static class InspectionItemEditModelStatuses
{
    public static readonly IReadOnlyList<string> All =
    [
        InspectionItemStatuses.Open, InspectionItemStatuses.InProgress,
        InspectionItemStatuses.Ready, InspectionItemStatuses.NotApplicable
    ];
}

public sealed record InspectionPrintableModel(string Reference, string Title, string Status, string SnapshotJson);
