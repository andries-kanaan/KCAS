using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class BusinessRiskAssessmentService(ApplicationDbContext db)
{
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public Task<List<BusinessRiskAssessment>> LoadListAsync()
        => db.BusinessRiskAssessments.AsNoTracking()
            .Include(item => item.Approvals)
            .OrderByDescending(item => item.AssessmentYear)
            .ThenByDescending(item => item.Id)
            .ToListAsync();

    public Task<BusinessRiskAssessment?> LoadAsync(int id)
        => db.BusinessRiskAssessments.AsNoTracking()
            .Include(item => item.Items.OrderBy(risk => risk.SortOrder))
            .Include(item => item.Approvals.OrderBy(approval => approval.ApprovedAtUtc))
            .SingleOrDefaultAsync(item => item.Id == id);

    public async Task<int> CreateDraftAsync(int assessmentYear, DateOnly asAtDate, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        if (assessmentYear is < 2000 or > 2200)
        {
            throw new ValidationException("Enter a valid assessment year.");
        }

        var assessment = new BusinessRiskAssessment
        {
            Name = $"Kanaan Business Risk Assessment {assessmentYear}",
            AssessmentYear = assessmentYear,
            AsAtDate = asAtDate,
            Scope = "Kanaan Trust's financial-services activities, clients, products, delivery channels, geographies and operating environment.",
            MethodologyNarrative = "Each risk is assessed on a proportional 3-by-3 likelihood and impact matrix. Client-risk results are evidence inputs and are not averaged into the business-risk result.",
            PreparedBy = user,
            UpdatedBy = user,
            Items = BusinessRiskCategories.All.Select((category, index) => new BusinessRiskItem
            {
                Category = category,
                SortOrder = index + 1,
                Owner = "Key Individuals"
            }).ToList()
        };
        db.BusinessRiskAssessments.Add(assessment);
        await db.SaveChangesAsync();
        await AuditAsync(assessment.Id, "Created", user, reason, AuditSummary(assessment));
        return assessment.Id;
    }

    public async Task SaveDraftAsync(BusinessRiskAssessmentEditModel model, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var assessment = await db.BusinessRiskAssessments
            .Include(item => item.Items)
            .SingleAsync(item => item.Id == model.Id);
        EnsureStatus(assessment, ComplianceStatuses.Draft);
        var oldJson = JsonSerializer.Serialize(AuditSummary(assessment), SnapshotOptions);

        assessment.Name = Required(model.Name, "BRA name", 191);
        assessment.AssessmentYear = model.AssessmentYear;
        assessment.AsAtDate = model.AsAtDate;
        assessment.Scope = Normalize(model.Scope);
        assessment.MethodologyNarrative = Normalize(model.MethodologyNarrative);
        assessment.ManagementJudgement = Normalize(model.ManagementJudgement);
        assessment.Limitations = Normalize(model.Limitations);
        assessment.RiskTolerance = Normalize(model.RiskTolerance);
        assessment.UpdatedAtUtc = DateTime.UtcNow;
        assessment.UpdatedBy = user;

        db.BusinessRiskItems.RemoveRange(assessment.Items);
        assessment.Items = model.Items.Select((item, index) =>
        {
            var score = ValidateMatrixValue(item.Likelihood, nameof(item.Likelihood)) *
                        ValidateMatrixValue(item.Impact, nameof(item.Impact));
            return new BusinessRiskItem
            {
                Category = Required(item.Category, "Risk category", 48),
                RiskStatement = Normalize(item.RiskStatement),
                EvidenceAndRationale = Normalize(item.EvidenceAndRationale),
                Likelihood = item.Likelihood,
                Impact = item.Impact,
                InherentScore = score,
                InherentRating = RatingFor(score),
                KeyControls = Normalize(item.KeyControls),
                ControlEffectiveness = Allowed(item.ControlEffectiveness, BusinessRiskControlEffectiveness.All, "control effectiveness"),
                ResidualRating = Allowed(item.ResidualRating, BusinessRiskRatings.All, "residual rating"),
                ResidualRationale = Normalize(item.ResidualRationale),
                TreatmentDecision = Allowed(item.TreatmentDecision, BusinessRiskTreatmentDecisions.All, "treatment decision"),
                Owner = Required(item.Owner, "Risk owner", 191),
                DueDate = item.DueDate,
                SortOrder = index + 1
            };
        }).ToList();

        db.ComplianceAuditEvents.Add(CreateAudit(assessment.Id, "Updated", user, reason, oldJson, AuditSummary(assessment)));
        await db.SaveChangesAsync();
    }

    public async Task SubmitAsync(int id, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var assessment = await db.BusinessRiskAssessments.Include(item => item.Items).SingleAsync(item => item.Id == id);
        EnsureStatus(assessment, ComplianceStatuses.Draft);
        ValidateForSubmission(assessment);

        assessment.PortfolioSnapshotJson = await BuildPortfolioSnapshotAsync(assessment.AsAtDate);
        assessment.Status = ComplianceStatuses.Review;
        assessment.SubmittedAtUtc = DateTime.UtcNow;
        assessment.UpdatedAtUtc = DateTime.UtcNow;
        assessment.UpdatedBy = user;
        db.ComplianceAuditEvents.Add(CreateAudit(id, "Submitted", user, reason, null, AuditSummary(assessment)));
        await db.SaveChangesAsync();
    }

    public async Task ApproveAsync(int id, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var assessment = await db.BusinessRiskAssessments
            .Include(item => item.Items)
            .Include(item => item.Approvals)
            .SingleAsync(item => item.Id == id);
        EnsureStatus(assessment, ComplianceStatuses.Review);
        if (assessment.Approvals.Any(item => string.Equals(item.Approver, user, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The same KI cannot approve this BRA twice.");
        }

        assessment.Approvals.Add(new BusinessRiskApproval { Approver = user, Reason = reason.Trim() });
        if (assessment.Approvals.Count >= 2)
        {
            assessment.Status = ComplianceStatuses.Approved;
            assessment.ApprovedAtUtc = DateTime.UtcNow;
            assessment.SnapshotJson = CreateFrozenSnapshot(assessment);
        }

        db.ComplianceAuditEvents.Add(CreateAudit(id, "KIApprovalRecorded", user, reason, null, new
        {
            assessment.Status,
            ApprovalCount = assessment.Approvals.Count
        }));
        await db.SaveChangesAsync();
    }

    public async Task ActivateAsync(int id, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var assessment = await db.BusinessRiskAssessments.SingleAsync(item => item.Id == id);
        EnsureStatus(assessment, ComplianceStatuses.Approved);
        if (string.IsNullOrWhiteSpace(assessment.SnapshotJson))
        {
            throw new InvalidOperationException("The approved BRA has no frozen snapshot.");
        }

        var active = await db.BusinessRiskAssessments
            .Where(item => item.Id != id && item.Status == ComplianceStatuses.Active)
            .ToListAsync();
        foreach (var prior in active)
        {
            prior.Status = ComplianceStatuses.Superseded;
            prior.UpdatedAtUtc = DateTime.UtcNow;
            db.ComplianceAuditEvents.Add(CreateAudit(prior.Id, "Superseded", user, reason, null, new { prior.Status }));
        }

        assessment.Status = ComplianceStatuses.Active;
        assessment.ActivatedAtUtc = DateTime.UtcNow;
        assessment.UpdatedAtUtc = DateTime.UtcNow;
        assessment.UpdatedBy = user;
        db.ComplianceAuditEvents.Add(CreateAudit(id, "Activated", user, reason, null, new { assessment.Status }));
        await db.SaveChangesAsync();
    }

    public async Task<BusinessRiskPrintableModel> LoadPrintableAsync(int id)
    {
        var assessment = await db.BusinessRiskAssessments.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id)
            ?? throw new KeyNotFoundException("Business Risk Assessment not found.");
        if (assessment.Status is not (ComplianceStatuses.Approved or ComplianceStatuses.Active or ComplianceStatuses.Superseded) ||
            string.IsNullOrWhiteSpace(assessment.SnapshotJson))
        {
            throw new InvalidOperationException("Only a frozen approved BRA can be printed.");
        }

        return new BusinessRiskPrintableModel(assessment.Name, assessment.Status, assessment.SnapshotJson);
    }

    public static string RatingFor(int score) => score switch
    {
        <= 2 => BusinessRiskRatings.Low,
        <= 4 => BusinessRiskRatings.Standard,
        <= 9 => BusinessRiskRatings.High,
        _ => throw new ValidationException("The inherent-risk score must be between 1 and 9.")
    };

    private async Task<string> BuildPortfolioSnapshotAsync(DateOnly asAtDate)
    {
        var assessments = await db.ClientRiskAssessments.AsNoTracking()
            .Where(item => item.Status == ClientRiskAssessmentStatuses.Approved &&
                           item.EffectiveDate != null && item.EffectiveDate <= asAtDate)
            .Select(item => new
            {
                item.ClientId,
                item.FinalRating,
                item.Client!.ClientCategory
            })
            .ToListAsync();
        var accounts = await db.ClientInvestmentAccounts.AsNoTracking()
            .Where(item => item.Client.IsActive &&
                           (item.SurrenderDate == null || item.SurrenderDate > asAtDate))
            .Select(item => new { item.Administrator, item.ProductName })
            .ToListAsync();

        var snapshot = new
        {
            AsAtDate = asAtDate,
            ApprovedClientAssessmentCount = assessments.Count,
            ClientsByCategory = assessments.GroupBy(item => item.ClientCategory)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count()),
            ClientsByFinalRating = assessments.GroupBy(item => item.FinalRating ?? "Unrated")
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count()),
            ActiveInvestmentAccountCount = accounts.Count,
            AccountsByAdministrator = accounts.GroupBy(item => NormalizeLabel(item.Administrator))
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count()),
            AccountsByProduct = accounts.GroupBy(item => NormalizeLabel(item.ProductName))
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count())
        };
        return JsonSerializer.Serialize(snapshot, SnapshotOptions);
    }

    private static string CreateFrozenSnapshot(BusinessRiskAssessment assessment)
        => JsonSerializer.Serialize(new
        {
            assessment.Id,
            assessment.Name,
            assessment.AssessmentYear,
            assessment.AsAtDate,
            assessment.Status,
            assessment.Scope,
            assessment.MethodologyNarrative,
            assessment.ManagementJudgement,
            assessment.Limitations,
            assessment.RiskTolerance,
            Portfolio = JsonSerializer.Deserialize<JsonElement>(assessment.PortfolioSnapshotJson ?? "{}"),
            Risks = assessment.Items.OrderBy(item => item.SortOrder).Select(item => new
            {
                item.Category,
                item.RiskStatement,
                item.EvidenceAndRationale,
                item.Likelihood,
                item.Impact,
                item.InherentScore,
                item.InherentRating,
                item.KeyControls,
                item.ControlEffectiveness,
                item.ResidualRating,
                item.ResidualRationale,
                item.TreatmentDecision,
                item.Owner,
                item.DueDate
            }),
            Approvals = assessment.Approvals.OrderBy(item => item.ApprovedAtUtc)
                .Select(item => new { item.Approver, item.Reason, item.ApprovedAtUtc })
        }, SnapshotOptions);

    private static void ValidateForSubmission(BusinessRiskAssessment assessment)
    {
        Required(assessment.Scope, "Scope");
        Required(assessment.MethodologyNarrative, "Methodology");
        Required(assessment.ManagementJudgement, "Management judgement");
        Required(assessment.Limitations, "Limitations");
        Required(assessment.RiskTolerance, "Risk tolerance");
        foreach (var category in BusinessRiskCategories.All)
        {
            if (!assessment.Items.Any(item => item.Category == category))
            {
                throw new ValidationException($"Add a risk for {BusinessRiskCategories.Display(category)}.");
            }
        }

        foreach (var item in assessment.Items)
        {
            Required(item.RiskStatement, $"{BusinessRiskCategories.Display(item.Category)} risk statement");
            Required(item.EvidenceAndRationale, $"{BusinessRiskCategories.Display(item.Category)} evidence and rationale");
            Required(item.KeyControls, $"{BusinessRiskCategories.Display(item.Category)} controls");
            Required(item.ResidualRationale, $"{BusinessRiskCategories.Display(item.Category)} residual rationale");
            Required(item.Owner, $"{BusinessRiskCategories.Display(item.Category)} owner");
            if (item.TreatmentDecision == BusinessRiskTreatmentDecisions.Treat && item.DueDate is null)
            {
                throw new ValidationException($"A treatment due date is required for {BusinessRiskCategories.Display(item.Category)}.");
            }
        }
    }

    private async Task AuditAsync(int id, string action, string user, string reason, object value)
    {
        db.ComplianceAuditEvents.Add(CreateAudit(id, action, user, reason, null, value));
        await db.SaveChangesAsync();
    }

    private static ComplianceAuditEvent CreateAudit(int id, string action, string user, string reason, string? oldJson, object value)
        => new()
        {
            EntityType = nameof(BusinessRiskAssessment),
            EntityId = id,
            Action = action,
            OldValueJson = oldJson,
            NewValueJson = JsonSerializer.Serialize(value, SnapshotOptions),
            UserName = user,
            TimestampUtc = DateTime.UtcNow,
            Reason = reason.Trim()
        };

    private static object AuditSummary(BusinessRiskAssessment assessment) => new
    {
        assessment.Id,
        assessment.Name,
        assessment.AssessmentYear,
        assessment.AsAtDate,
        assessment.Status,
        assessment.UpdatedBy,
        Risks = assessment.Items.Select(item => new
        {
            item.Category,
            item.InherentScore,
            item.InherentRating,
            item.ResidualRating,
            item.TreatmentDecision,
            item.Owner,
            item.DueDate
        })
    };

    private static int ValidateMatrixValue(int value, string field)
        => value is >= 1 and <= 3 ? value : throw new ValidationException($"{field} must be between 1 and 3.");

    private static string Allowed(string? value, IReadOnlyList<string> allowed, string label)
        => allowed.Contains(value ?? "", StringComparer.Ordinal)
            ? value!
            : throw new ValidationException($"Select a valid {label}.");

    private static string Required(string? value, string label, int? maximumLength = null)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ValidationException($"{label} is required.");
        }
        if (maximumLength is not null && normalized.Length > maximumLength)
        {
            throw new ValidationException($"{label} cannot exceed {maximumLength} characters.");
        }
        return normalized;
    }

    private static void EnsureStatus(BusinessRiskAssessment assessment, string status)
    {
        if (assessment.Status != status)
        {
            throw new InvalidOperationException($"This action requires a {status} BRA.");
        }
    }

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("A reason is required.");
        }
    }

    private static string RequireUser(string? userName)
        => string.IsNullOrWhiteSpace(userName)
            ? throw new ValidationException("The current user identity is required.")
            : userName.Trim();

    private static string Normalize(string? value) => value?.Trim() ?? "";
    private static string NormalizeLabel(string? value) => string.IsNullOrWhiteSpace(value) ? "Not recorded" : value.Trim();
}

public sealed class BusinessRiskAssessmentEditModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int AssessmentYear { get; set; }
    public DateOnly AsAtDate { get; set; }
    public string Scope { get; set; } = "";
    public string MethodologyNarrative { get; set; } = "";
    public string ManagementJudgement { get; set; } = "";
    public string Limitations { get; set; } = "";
    public string RiskTolerance { get; set; } = "";
    public List<BusinessRiskItemEditModel> Items { get; set; } = [];

    public static BusinessRiskAssessmentEditModel FromEntity(BusinessRiskAssessment item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        AssessmentYear = item.AssessmentYear,
        AsAtDate = item.AsAtDate,
        Scope = item.Scope,
        MethodologyNarrative = item.MethodologyNarrative,
        ManagementJudgement = item.ManagementJudgement,
        Limitations = item.Limitations,
        RiskTolerance = item.RiskTolerance,
        Items = item.Items.OrderBy(risk => risk.SortOrder).Select(BusinessRiskItemEditModel.FromEntity).ToList()
    };
}

public sealed class BusinessRiskItemEditModel
{
    public string Category { get; set; } = "";
    public string RiskStatement { get; set; } = "";
    public string EvidenceAndRationale { get; set; } = "";
    public int Likelihood { get; set; } = 1;
    public int Impact { get; set; } = 1;
    public string KeyControls { get; set; } = "";
    public string ControlEffectiveness { get; set; } = BusinessRiskControlEffectiveness.PartiallyEffective;
    public string ResidualRating { get; set; } = BusinessRiskRatings.Standard;
    public string ResidualRationale { get; set; } = "";
    public string TreatmentDecision { get; set; } = BusinessRiskTreatmentDecisions.Accept;
    public string Owner { get; set; } = "";
    public DateOnly? DueDate { get; set; }

    public static BusinessRiskItemEditModel FromEntity(BusinessRiskItem item) => new()
    {
        Category = item.Category,
        RiskStatement = item.RiskStatement,
        EvidenceAndRationale = item.EvidenceAndRationale,
        Likelihood = item.Likelihood,
        Impact = item.Impact,
        KeyControls = item.KeyControls,
        ControlEffectiveness = item.ControlEffectiveness,
        ResidualRating = item.ResidualRating,
        ResidualRationale = item.ResidualRationale,
        TreatmentDecision = item.TreatmentDecision,
        Owner = item.Owner,
        DueDate = item.DueDate
    };
}

public sealed record BusinessRiskPrintableModel(string Name, string Status, string SnapshotJson);
