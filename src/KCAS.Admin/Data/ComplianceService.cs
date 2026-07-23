using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ComplianceService(ApplicationDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public async Task<ComplianceDashboardModel> LoadDashboardAsync()
    {
        var profile = await db.ComplianceProfiles.AsNoTracking().OrderBy(profile => profile.Id).LastOrDefaultAsync();
        var activeMethodology = await db.RiskMethodologyVersions.AsNoTracking()
            .Where(methodology => methodology.Status == ComplianceStatuses.Active)
            .OrderByDescending(methodology => methodology.EffectiveFrom)
            .FirstOrDefaultAsync();
        var pendingApprovals = await db.RiskMethodologyVersions.AsNoTracking()
            .CountAsync(methodology => methodology.Status == ComplianceStatuses.Review);
        var openTasks = await db.ComplianceTasks.AsNoTracking()
            .CountAsync(task => task.Status != ComplianceStatuses.Closed && task.Status != ComplianceStatuses.Withdrawn);
        var upcomingDocuments = await db.ControlledDocuments.AsNoTracking()
            .Where(document => document.NextReviewDate != null && document.NextReviewDate <= DateOnly.FromDateTime(DateTime.Today.AddDays(90)))
            .OrderBy(document => document.NextReviewDate)
            .Take(8)
            .ToListAsync();
        var recentAudit = await db.ComplianceAuditEvents.AsNoTracking()
            .OrderByDescending(audit => audit.TimestampUtc)
            .Take(12)
            .ToListAsync();

        return new ComplianceDashboardModel(profile, activeMethodology, pendingApprovals, openTasks, upcomingDocuments, recentAudit);
    }

    public async Task<ComplianceManageModel> LoadManageModelAsync()
    {
        return new ComplianceManageModel
        {
            Profile = ComplianceProfileModel.FromEntity(await db.ComplianceProfiles.AsNoTracking().OrderBy(profile => profile.Id).LastOrDefaultAsync()),
            GovernanceRoles = await db.GovernanceRoleAssignments.AsNoTracking().OrderBy(role => role.RoleType).ThenBy(role => role.PersonName).ToListAsync(),
            Documents = await db.ControlledDocuments.AsNoTracking().OrderBy(document => document.DocumentType).ThenBy(document => document.Title).ToListAsync(),
            ReferenceValues = await db.ComplianceReferenceValues.AsNoTracking().OrderBy(reference => reference.Category).ThenBy(reference => reference.SortOrder).ThenBy(reference => reference.Name).ToListAsync(),
            Methodologies = await db.RiskMethodologyVersions.AsNoTracking().Include(methodology => methodology.Factors).Include(methodology => methodology.Bands).OrderByDescending(methodology => methodology.CreatedAtUtc).ToListAsync(),
            Tasks = await db.ComplianceTasks.AsNoTracking().OrderBy(task => task.Status).ThenBy(task => task.DueDate).ToListAsync(),
            Evidence = await db.ComplianceEvidence.AsNoTracking().OrderBy(evidence => evidence.EvidenceType).ThenBy(evidence => evidence.Title).ToListAsync(),
            AuditEvents = await db.ComplianceAuditEvents.AsNoTracking().OrderByDescending(audit => audit.TimestampUtc).Take(100).ToListAsync()
        };
    }

    public async Task<int> SaveProfileAsync(ComplianceProfileModel model, string? userName, string reason)
    {
        RequireReason(reason);
        var legalName = Normalize(model.LegalName);
        if (string.IsNullOrWhiteSpace(legalName))
        {
            throw new ValidationException("Legal name is required.");
        }

        ComplianceProfile profile;
        string? oldJson = null;
        var action = "Created";
        if (model.Id is null)
        {
            profile = new ComplianceProfile();
            db.ComplianceProfiles.Add(profile);
        }
        else
        {
            profile = await db.ComplianceProfiles.SingleAsync(profile => profile.Id == model.Id.Value);
            oldJson = Snapshot(profile);
            action = "Updated";
        }

        profile.LegalName = legalName;
        profile.TradingName = Normalize(model.TradingName);
        profile.FspNumber = Normalize(model.FspNumber);
        profile.AccountableInstitutionNumber = Normalize(model.AccountableInstitutionNumber);
        profile.PrimaryContactName = Normalize(model.PrimaryContactName);
        profile.PrimaryContactEmail = Normalize(model.PrimaryContactEmail);
        profile.PrimaryContactPhone = Normalize(model.PrimaryContactPhone);
        profile.RegisteredAddress = Normalize(model.RegisteredAddress);
        profile.OperatingAddress = Normalize(model.OperatingAddress);
        profile.EffectiveFrom = model.EffectiveFrom;
        profile.EffectiveTo = model.EffectiveTo;
        profile.Status = Normalize(model.Status) ?? ComplianceStatuses.Draft;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        profile.UpdatedBy = Normalize(userName);
        await SaveWithAuditAsync(nameof(ComplianceProfile), () => profile.Id, action, oldJson, profile, userName, reason);
        return profile.Id;
    }

    public async Task<int> SaveGovernanceRoleAsync(GovernanceRoleModel model, string? userName, string reason)
    {
        RequireReason(reason);
        RequireValue(model.RoleType, "Role type is required.");
        RequireValue(model.PersonName, "Person name is required.");
        GovernanceRoleAssignment role;
        string? oldJson = null;
        var action = "Created";
        if (model.Id is null)
        {
            role = new GovernanceRoleAssignment();
            db.GovernanceRoleAssignments.Add(role);
        }
        else
        {
            role = await db.GovernanceRoleAssignments.SingleAsync(role => role.Id == model.Id.Value);
            oldJson = Snapshot(role);
            action = "Updated";
        }

        role.RoleType = Normalize(model.RoleType)!;
        role.PersonName = Normalize(model.PersonName)!;
        role.Email = Normalize(model.Email);
        role.Phone = Normalize(model.Phone);
        role.ResponsibilitySummary = Normalize(model.ResponsibilitySummary);
        role.StartDate = model.StartDate;
        role.EndDate = model.EndDate;
        role.IsActive = model.IsActive;
        role.UpdatedAtUtc = DateTime.UtcNow;
        role.UpdatedBy = Normalize(userName);
        await SaveWithAuditAsync(nameof(GovernanceRoleAssignment), () => role.Id, action, oldJson, role, userName, reason);
        return role.Id;
    }

    public async Task<int> SaveDocumentAsync(ControlledDocumentModel model, string? userName, string reason)
    {
        RequireReason(reason);
        RequireValue(model.DocumentType, "Document type is required.");
        RequireValue(model.Title, "Document title is required.");
        ControlledDocument document;
        string? oldJson = null;
        var action = "Created";
        if (model.Id is null)
        {
            document = new ControlledDocument();
            db.ControlledDocuments.Add(document);
        }
        else
        {
            document = await db.ControlledDocuments.SingleAsync(document => document.Id == model.Id.Value);
            oldJson = Snapshot(document);
            action = "Updated";
        }

        document.DocumentType = Normalize(model.DocumentType)!;
        document.Title = Normalize(model.Title)!;
        document.Owner = Normalize(model.Owner);
        document.VersionReference = Normalize(model.VersionReference);
        document.Status = Normalize(model.Status) ?? ComplianceStatuses.Draft;
        document.EffectiveDate = model.EffectiveDate;
        document.NextReviewDate = model.NextReviewDate;
        document.Location = Normalize(model.Location);
        document.Notes = Normalize(model.Notes);
        document.UpdatedAtUtc = DateTime.UtcNow;
        document.UpdatedBy = Normalize(userName);
        await SaveWithAuditAsync(nameof(ControlledDocument), () => document.Id, action, oldJson, document, userName, reason);
        return document.Id;
    }

    public async Task<int> SaveReferenceValueAsync(ComplianceReferenceValueModel model, string? userName, string reason)
    {
        RequireReason(reason);
        RequireValue(model.Category, "Reference category is required.");
        RequireValue(model.Code, "Reference code is required.");
        RequireValue(model.Name, "Reference name is required.");
        var category = Normalize(model.Category)!;
        var code = Normalize(model.Code)!;
        var duplicate = await db.ComplianceReferenceValues.AnyAsync(reference =>
            reference.Id != model.Id &&
            reference.IsActive &&
            reference.Category == category &&
            reference.Code == code);
        if (duplicate)
        {
            throw new InvalidOperationException("An active reference value with this category and code already exists.");
        }

        ComplianceReferenceValue referenceValue;
        string? oldJson = null;
        var action = "Created";
        if (model.Id is null)
        {
            referenceValue = new ComplianceReferenceValue();
            db.ComplianceReferenceValues.Add(referenceValue);
        }
        else
        {
            referenceValue = await db.ComplianceReferenceValues.SingleAsync(reference => reference.Id == model.Id.Value);
            oldJson = Snapshot(referenceValue);
            action = "Updated";
        }

        referenceValue.Category = category;
        referenceValue.Code = code;
        referenceValue.Name = Normalize(model.Name)!;
        referenceValue.Description = Normalize(model.Description);
        referenceValue.SortOrder = model.SortOrder;
        referenceValue.IsActive = model.IsActive;
        referenceValue.UpdatedAtUtc = DateTime.UtcNow;
        referenceValue.UpdatedBy = Normalize(userName);
        await SaveWithAuditAsync(nameof(ComplianceReferenceValue), () => referenceValue.Id, action, oldJson, referenceValue, userName, reason);
        return referenceValue.Id;
    }

    public async Task<int> SaveMethodologyAsync(RiskMethodologyModel model, string? userName, string reason)
    {
        RequireReason(reason);
        RequireValue(model.Name, "Methodology name is required.");
        RiskMethodologyVersion methodology;
        string? oldJson = null;
        var action = "Created";
        if (model.Id is null)
        {
            methodology = new RiskMethodologyVersion();
            db.RiskMethodologyVersions.Add(methodology);
        }
        else
        {
            methodology = await db.RiskMethodologyVersions
                .Include(methodology => methodology.Factors).ThenInclude(factor => factor.Options)
                .Include(methodology => methodology.Bands)
                .SingleAsync(methodology => methodology.Id == model.Id.Value);
            if (methodology.Status is ComplianceStatuses.Approved or ComplianceStatuses.Active or ComplianceStatuses.Superseded)
            {
                throw new InvalidOperationException("Approved, active and superseded methodologies cannot be edited. Create a new draft version.");
            }
            oldJson = Snapshot(methodology);
            action = "Updated";
            db.RiskFactorOptions.RemoveRange(methodology.Factors.SelectMany(factor => factor.Options));
            db.RiskFactorDefinitions.RemoveRange(methodology.Factors);
            db.RiskBands.RemoveRange(methodology.Bands);
        }

        methodology.Name = Normalize(model.Name)!;
        methodology.VersionLabel = Normalize(model.VersionLabel);
        methodology.Summary = Normalize(model.Summary);
        methodology.EffectiveFrom = model.EffectiveFrom;
        methodology.EffectiveTo = model.EffectiveTo;
        methodology.UpdatedBy = Normalize(userName);
        if (string.IsNullOrWhiteSpace(methodology.Status))
        {
            methodology.Status = ComplianceStatuses.Draft;
        }
        methodology.Factors = model.Factors
            .Where(factor => !string.IsNullOrWhiteSpace(factor.Code) || !string.IsNullOrWhiteSpace(factor.Name))
            .Select((factor, index) => new RiskFactorDefinition
            {
                Code = Normalize(factor.Code) ?? $"F{index + 1}",
                Name = Normalize(factor.Name) ?? Normalize(factor.Code) ?? $"Factor {index + 1}",
                Description = Normalize(factor.Description),
                Weight = factor.Weight,
                IsMandatoryHighRiskTrigger = factor.IsMandatoryHighRiskTrigger,
                SortOrder = factor.SortOrder == 0 ? index + 1 : factor.SortOrder,
                Options = factor.Options
                    .Where(option => !string.IsNullOrWhiteSpace(option.Code) || !string.IsNullOrWhiteSpace(option.Label))
                    .Select((option, optionIndex) => new RiskFactorOption
                    {
                        Code = Normalize(option.Code) ?? $"O{optionIndex + 1}",
                        Label = Normalize(option.Label) ?? Normalize(option.Code) ?? $"Option {optionIndex + 1}",
                        Score = option.Score,
                        TriggersHighRisk = option.TriggersHighRisk,
                        SortOrder = option.SortOrder == 0 ? optionIndex + 1 : option.SortOrder
                    }).ToList()
            }).ToList();
        methodology.Bands = model.Bands
            .Where(band => !string.IsNullOrWhiteSpace(band.Name))
            .Select((band, index) => new RiskBand
            {
                Name = Normalize(band.Name)!,
                MinimumScore = band.MinimumScore,
                MaximumScore = band.MaximumScore,
                SortOrder = band.SortOrder == 0 ? index + 1 : band.SortOrder
            }).ToList();

        await SaveWithAuditAsync(nameof(RiskMethodologyVersion), () => methodology.Id, action, oldJson, methodology, userName, reason);
        return methodology.Id;
    }

    public async Task SubmitMethodologyAsync(int methodologyId, string? userName, string reason)
    {
        var methodology = await LoadMethodologyForStatusChangeAsync(methodologyId);
        if (methodology.Status != ComplianceStatuses.Draft)
        {
            throw new InvalidOperationException("Only draft methodologies can be submitted for review.");
        }
        methodology.SubmittedAtUtc = DateTime.UtcNow;
        await ChangeMethodologyStatusAsync(methodology, ComplianceStatuses.Review, "Submitted", userName, reason);
    }

    public async Task ApproveMethodologyAsync(int methodologyId, string? userName, string reason)
    {
        var methodology = await LoadMethodologyForStatusChangeAsync(methodologyId);
        if (methodology.Status != ComplianceStatuses.Review)
        {
            throw new InvalidOperationException("Only methodologies in review can be approved.");
        }
        methodology.ApprovedAtUtc = DateTime.UtcNow;
        await ChangeMethodologyStatusAsync(methodology, ComplianceStatuses.Approved, "Approved", userName, reason);
        db.ComplianceApprovals.Add(new ComplianceApproval { TargetEntityType = nameof(RiskMethodologyVersion), TargetEntityId = methodology.Id, Decision = ComplianceStatuses.Approved, Approver = Normalize(userName), Reason = reason.Trim() });
        await db.SaveChangesAsync();
    }

    public async Task RejectMethodologyAsync(int methodologyId, string? userName, string reason)
    {
        var methodology = await LoadMethodologyForStatusChangeAsync(methodologyId);
        if (methodology.Status != ComplianceStatuses.Review)
        {
            throw new InvalidOperationException("Only methodologies in review can be rejected.");
        }
        await ChangeMethodologyStatusAsync(methodology, ComplianceStatuses.Rejected, "Rejected", userName, reason);
        db.ComplianceApprovals.Add(new ComplianceApproval { TargetEntityType = nameof(RiskMethodologyVersion), TargetEntityId = methodology.Id, Decision = ComplianceStatuses.Rejected, Approver = Normalize(userName), Reason = reason.Trim() });
        await db.SaveChangesAsync();
    }

    public async Task ActivateMethodologyAsync(int methodologyId, string? userName, string reason)
    {
        RequireReason(reason);
        var methodology = await LoadMethodologyForStatusChangeAsync(methodologyId);
        if (methodology.Status != ComplianceStatuses.Approved)
        {
            throw new InvalidOperationException("Only approved methodologies can be activated.");
        }
        var activeMethodologies = await db.RiskMethodologyVersions
            .Where(existing => existing.Id != methodology.Id && existing.Status == ComplianceStatuses.Active)
            .ToListAsync();
        foreach (var active in activeMethodologies)
        {
            var oldActiveJson = Snapshot(active);
            active.Status = ComplianceStatuses.Superseded;
            active.EffectiveTo = DateOnly.FromDateTime(DateTime.Today);
            active.UpdatedBy = Normalize(userName);
            db.ComplianceAuditEvents.Add(CreateAudit(nameof(RiskMethodologyVersion), active.Id, "Superseded", oldActiveJson, Snapshot(active), userName, reason));
        }
        methodology.ActivatedAtUtc = DateTime.UtcNow;
        await ChangeMethodologyStatusAsync(methodology, ComplianceStatuses.Active, "Activated", userName, reason);
    }

    public async Task<int> SaveTaskAsync(ComplianceTaskModel model, string? userName, string reason)
    {
        RequireReason(reason);
        RequireValue(model.Title, "Task title is required.");
        ComplianceTask task;
        string? oldJson = null;
        var action = "Created";
        if (model.Id is null)
        {
            task = new ComplianceTask();
            db.ComplianceTasks.Add(task);
        }
        else
        {
            task = await db.ComplianceTasks.SingleAsync(task => task.Id == model.Id.Value);
            oldJson = Snapshot(task);
            action = "Updated";
        }

        task.Title = Normalize(model.Title)!;
        task.Description = Normalize(model.Description);
        task.Owner = Normalize(model.Owner);
        task.DueDate = model.DueDate;
        task.Priority = Normalize(model.Priority) ?? "Normal";
        task.Status = Normalize(model.Status) ?? ComplianceStatuses.Draft;
        task.LinkedEntityType = Normalize(model.LinkedEntityType);
        task.LinkedEntityId = model.LinkedEntityId;
        task.ClosureNotes = Normalize(model.ClosureNotes);
        task.ClosedAtUtc = task.Status == ComplianceStatuses.Closed && task.ClosedAtUtc is null ? DateTime.UtcNow : task.ClosedAtUtc;
        task.UpdatedAtUtc = DateTime.UtcNow;
        task.UpdatedBy = Normalize(userName);
        await SaveWithAuditAsync(nameof(ComplianceTask), () => task.Id, action, oldJson, task, userName, reason);
        return task.Id;
    }

    public async Task<int> SaveEvidenceAsync(ComplianceEvidenceModel model, string? userName, string reason)
    {
        RequireReason(reason);
        RequireValue(model.EvidenceType, "Evidence type is required.");
        RequireValue(model.Title, "Evidence title is required.");
        ComplianceEvidence evidence;
        string? oldJson = null;
        var action = "Created";
        if (model.Id is null)
        {
            evidence = new ComplianceEvidence();
            db.ComplianceEvidence.Add(evidence);
        }
        else
        {
            evidence = await db.ComplianceEvidence.SingleAsync(evidence => evidence.Id == model.Id.Value);
            oldJson = Snapshot(evidence);
            action = "Updated";
        }

        evidence.EvidenceType = Normalize(model.EvidenceType)!;
        evidence.Title = Normalize(model.Title)!;
        evidence.Source = Normalize(model.Source);
        evidence.Location = Normalize(model.Location);
        evidence.ReceivedDate = model.ReceivedDate;
        evidence.VerifiedDate = model.VerifiedDate;
        evidence.ExpiryDate = model.ExpiryDate;
        evidence.Reviewer = Normalize(model.Reviewer);
        evidence.Notes = Normalize(model.Notes);
        evidence.LinkedEntityType = Normalize(model.LinkedEntityType);
        evidence.LinkedEntityId = model.LinkedEntityId;
        evidence.UpdatedAtUtc = DateTime.UtcNow;
        evidence.UpdatedBy = Normalize(userName);
        await SaveWithAuditAsync(nameof(ComplianceEvidence), () => evidence.Id, action, oldJson, evidence, userName, reason);
        return evidence.Id;
    }

    private async Task<RiskMethodologyVersion> LoadMethodologyForStatusChangeAsync(int methodologyId)
        => await db.RiskMethodologyVersions.SingleAsync(methodology => methodology.Id == methodologyId);

    private async Task ChangeMethodologyStatusAsync(RiskMethodologyVersion methodology, string status, string action, string? userName, string reason)
    {
        RequireReason(reason);
        var oldJson = Snapshot(methodology);
        methodology.Status = status;
        methodology.UpdatedBy = Normalize(userName);
        methodology.EffectiveFrom ??= DateOnly.FromDateTime(DateTime.Today);
        db.ComplianceAuditEvents.Add(CreateAudit(nameof(RiskMethodologyVersion), methodology.Id, action, oldJson, Snapshot(methodology), userName, reason));
        await db.SaveChangesAsync();
    }

    private async Task SaveWithAuditAsync(string entityType, Func<int> entityIdAccessor, string action, string? oldJson, object newValue, string? userName, string reason)
    {
        await db.SaveChangesAsync();
        db.ComplianceAuditEvents.Add(CreateAudit(entityType, entityIdAccessor(), action, oldJson, Snapshot(newValue), userName, reason));
        await db.SaveChangesAsync();
    }

    private static ComplianceAuditEvent CreateAudit(string entityType, int entityId, string action, string? oldJson, string? newJson, string? userName, string reason)
        => new()
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            OldValueJson = oldJson,
            NewValueJson = newJson,
            UserName = Normalize(userName),
            TimestampUtc = DateTime.UtcNow,
            Reason = reason.Trim()
        };

    private static string Snapshot(object value) => JsonSerializer.Serialize(value, JsonOptions);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("A reason is required.");
        }
    }
    private static void RequireValue(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(message);
        }
    }
}

public sealed record ComplianceDashboardModel(
    ComplianceProfile? Profile,
    RiskMethodologyVersion? ActiveMethodology,
    int PendingApprovals,
    int OpenTasks,
    IReadOnlyList<ControlledDocument> UpcomingDocuments,
    IReadOnlyList<ComplianceAuditEvent> RecentAudit);

public sealed class ComplianceManageModel
{
    public ComplianceProfileModel Profile { get; set; } = new();
    public IReadOnlyList<GovernanceRoleAssignment> GovernanceRoles { get; set; } = [];
    public IReadOnlyList<ControlledDocument> Documents { get; set; } = [];
    public IReadOnlyList<ComplianceReferenceValue> ReferenceValues { get; set; } = [];
    public IReadOnlyList<RiskMethodologyVersion> Methodologies { get; set; } = [];
    public IReadOnlyList<ComplianceTask> Tasks { get; set; } = [];
    public IReadOnlyList<ComplianceEvidence> Evidence { get; set; } = [];
    public IReadOnlyList<ComplianceAuditEvent> AuditEvents { get; set; } = [];
}

public sealed class ComplianceProfileModel
{
    public int? Id { get; set; }
    public string? LegalName { get; set; }
    public string? TradingName { get; set; }
    public string? FspNumber { get; set; }
    public string? AccountableInstitutionNumber { get; set; }
    public string? PrimaryContactName { get; set; }
    public string? PrimaryContactEmail { get; set; }
    public string? PrimaryContactPhone { get; set; }
    public string? RegisteredAddress { get; set; }
    public string? OperatingAddress { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string Status { get; set; } = ComplianceStatuses.Draft;

    public static ComplianceProfileModel FromEntity(ComplianceProfile? profile) => profile is null ? new() : new()
    {
        Id = profile.Id,
        LegalName = profile.LegalName,
        TradingName = profile.TradingName,
        FspNumber = profile.FspNumber,
        AccountableInstitutionNumber = profile.AccountableInstitutionNumber,
        PrimaryContactName = profile.PrimaryContactName,
        PrimaryContactEmail = profile.PrimaryContactEmail,
        PrimaryContactPhone = profile.PrimaryContactPhone,
        RegisteredAddress = profile.RegisteredAddress,
        OperatingAddress = profile.OperatingAddress,
        EffectiveFrom = profile.EffectiveFrom,
        EffectiveTo = profile.EffectiveTo,
        Status = profile.Status
    };
}

public sealed class GovernanceRoleModel
{
    public int? Id { get; set; }
    public string? RoleType { get; set; }
    public string? PersonName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? ResponsibilitySummary { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ControlledDocumentModel
{
    public int? Id { get; set; }
    public string? DocumentType { get; set; }
    public string? Title { get; set; }
    public string? Owner { get; set; }
    public string? VersionReference { get; set; }
    public string Status { get; set; } = ComplianceStatuses.Draft;
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? NextReviewDate { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
}

public sealed class ComplianceReferenceValueModel
{
    public int? Id { get; set; }
    public string? Category { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class RiskMethodologyModel
{
    public int? Id { get; set; }
    public string? Name { get; set; }
    public string? VersionLabel { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Summary { get; set; }
    public List<RiskFactorModel> Factors { get; set; } = [];
    public List<RiskBandModel> Bands { get; set; } = [];
}

public sealed class RiskFactorModel
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal Weight { get; set; }
    public bool IsMandatoryHighRiskTrigger { get; set; }
    public int SortOrder { get; set; }
    public List<RiskFactorOptionModel> Options { get; set; } = [];
}

public sealed class RiskFactorOptionModel
{
    public string? Code { get; set; }
    public string? Label { get; set; }
    public int Score { get; set; }
    public bool TriggersHighRisk { get; set; }
    public int SortOrder { get; set; }
}

public sealed class RiskBandModel
{
    public string? Name { get; set; }
    public decimal MinimumScore { get; set; }
    public decimal? MaximumScore { get; set; }
    public int SortOrder { get; set; }
}

public sealed class ComplianceTaskModel
{
    public int? Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Owner { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Priority { get; set; } = "Normal";
    public string Status { get; set; } = ComplianceStatuses.Draft;
    public string? LinkedEntityType { get; set; }
    public int? LinkedEntityId { get; set; }
    public string? ClosureNotes { get; set; }
}

public sealed class ComplianceEvidenceModel
{
    public int? Id { get; set; }
    public string? EvidenceType { get; set; }
    public string? Title { get; set; }
    public string? Source { get; set; }
    public string? Location { get; set; }
    public DateOnly? ReceivedDate { get; set; }
    public DateOnly? VerifiedDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? Reviewer { get; set; }
    public string? Notes { get; set; }
    public string? LinkedEntityType { get; set; }
    public int? LinkedEntityId { get; set; }
}
