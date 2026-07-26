using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientRiskAssessmentService(
    ApplicationDbContext db,
    ClientEvidenceReadinessService evidenceReadinessService)
{
    private static readonly JsonSerializerOptions SnapshotOptions = new(JsonSerializerDefaults.Web);

    public async Task<ClientRiskAssessmentPageModel> LoadAsync(int clientId)
    {
        var client = await db.Clients.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == clientId)
            ?? throw new KeyNotFoundException("Client not found.");

        var assessment = await db.ClientRiskAssessments.AsNoTracking()
            .Include(item => item.MethodologyVersion).ThenInclude(methodology => methodology!.Factors).ThenInclude(factor => factor.Options)
            .Include(item => item.MethodologyVersion).ThenInclude(methodology => methodology!.Bands)
            .Include(item => item.Responses).ThenInclude(response => response.SelectedOption)
            .Include(item => item.Responses).ThenInclude(response => response.EvidenceItem)
            .Include(item => item.Approvals)
            .Where(item => item.ClientId == clientId && item.Status != ClientRiskAssessmentStatuses.Superseded)
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync();

        var activeMethodology = assessment?.MethodologyVersion ?? await db.RiskMethodologyVersions.AsNoTracking()
            .Include(methodology => methodology.Factors).ThenInclude(factor => factor.Options)
            .Include(methodology => methodology.Bands)
            .Where(methodology => methodology.Status == ComplianceStatuses.Active)
            .OrderByDescending(methodology => methodology.ActivatedAtUtc)
            .FirstOrDefaultAsync();

        var evidence = await db.ClientEvidenceItems.AsNoTracking()
            .Where(item => item.ClientId == clientId &&
                           item.VerifiedDate != null &&
                           item.OwnershipStatus == ClientEvidenceOwnershipStatuses.Confirmed &&
                           item.SelectionStatus == ClientEvidenceSelectionStatuses.Current)
            .OrderBy(item => item.EvidenceType)
            .ThenBy(item => item.Title)
            .Select(item => new ClientRiskEvidenceOption(item.Id, item.EvidenceType, item.Title))
            .ToListAsync();

        var readiness = await evidenceReadinessService.LoadClientReadinessAsync(clientId);
        var history = await db.ClientRiskAssessments.AsNoTracking()
            .Where(item => item.ClientId == clientId && item.Status != ClientRiskAssessmentStatuses.Draft)
            .OrderByDescending(item => item.Id)
            .Select(item => new ClientRiskAssessmentHistoryItem(
                item.Id,
                item.Status,
                item.FinalRating ?? item.CalculatedRating,
                item.CalculatedScore,
                item.EffectiveDate,
                item.NextReviewDate,
                item.FinalisedBy))
            .ToListAsync();

        return new ClientRiskAssessmentPageModel
        {
            ClientId = client.Id,
            DisplayName = client.DisplayName,
            KanaanId = client.KanaanId,
            ClientCategory = client.ClientCategory,
            IsReadyForRiskAssessment = readiness.IsReadyForRiskAssessment,
            BlockingEvidenceCount = readiness.BlockedCount,
            HasActiveMethodology = activeMethodology is not null,
            ActiveMethodologyName = activeMethodology is null ? null : $"{activeMethodology.Name} {activeMethodology.VersionLabel}".Trim(),
            Assessment = assessment is null ? null : MapAssessment(assessment),
            Factors = activeMethodology?.Factors
                .OrderBy(factor => factor.SortOrder)
                .Select(factor =>
                {
                    var response = assessment?.Responses.SingleOrDefault(item => item.RiskFactorDefinitionId == factor.Id);
                    return new ClientRiskFactorEditModel
                    {
                        FactorId = factor.Id,
                        Code = factor.Code,
                        Name = factor.Name,
                        Description = factor.Description,
                        Weight = factor.Weight,
                        SelectedOptionId = response?.RiskFactorOptionId,
                        EvidenceItemId = response?.ClientEvidenceItemId,
                        Explanation = response?.Explanation,
                        Options = factor.Options.OrderBy(option => option.SortOrder)
                            .Select(option => new ClientRiskFactorOptionModel(option.Id, option.Label, option.Score, option.TriggersHighRisk))
                            .ToList()
                    };
                }).ToList() ?? [],
            EvidenceOptions = evidence,
            History = history
        };
    }

    public async Task<int> CreateDraftAsync(int clientId, string? userName, string reason)
    {
        RequireReason(reason);
        var preparedBy = RequireUser(userName);
        if (!await db.Clients.AnyAsync(client => client.Id == clientId))
        {
            throw new KeyNotFoundException("Client not found.");
        }

        var existingDraft = await db.ClientRiskAssessments.AnyAsync(item =>
            item.ClientId == clientId &&
            (item.Status == ClientRiskAssessmentStatuses.Draft ||
             item.Status == ClientRiskAssessmentStatuses.PendingKiApproval));
        if (existingDraft)
        {
            throw new InvalidOperationException("This client already has an assessment in progress.");
        }

        var methodology = await LoadActiveMethodologyAsync();
        ValidateMethodology(methodology);
        var assessment = new ClientRiskAssessment
        {
            ClientId = clientId,
            RiskMethodologyVersionId = methodology.Id,
            PreparedBy = preparedBy,
            Status = ClientRiskAssessmentStatuses.Draft,
            Responses = methodology.Factors.Select(factor => new ClientRiskAssessmentResponse
            {
                RiskFactorDefinitionId = factor.Id
            }).ToList()
        };
        db.ClientRiskAssessments.Add(assessment);
        await db.SaveChangesAsync();
        db.ComplianceAuditEvents.Add(CreateAudit(assessment.Id, "DraftCreated", preparedBy, reason, AuditSummary(assessment)));
        await db.SaveChangesAsync();
        return assessment.Id;
    }

    public async Task SaveDraftAsync(int assessmentId, ClientRiskAssessmentEditModel model, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var assessment = await LoadAssessmentForMutationAsync(assessmentId);
        EnsureDraft(assessment);

        var supplied = model.Factors.ToDictionary(item => item.FactorId);
        foreach (var response in assessment.Responses)
        {
            if (!supplied.TryGetValue(response.RiskFactorDefinitionId, out var input))
            {
                continue;
            }

            var selectedOption = input.SelectedOptionId.HasValue
                ? response.FactorDefinition!.Options.SingleOrDefault(option => option.Id == input.SelectedOptionId.Value)
                : null;
            if (input.SelectedOptionId.HasValue && selectedOption is null)
            {
                throw new ValidationException($"The selected option does not belong to {response.FactorDefinition!.Name}.");
            }

            if (input.EvidenceItemId.HasValue)
            {
                var validEvidence = await db.ClientEvidenceItems.AnyAsync(item =>
                    item.Id == input.EvidenceItemId.Value &&
                    item.ClientId == assessment.ClientId &&
                    item.VerifiedDate != null &&
                    item.OwnershipStatus == ClientEvidenceOwnershipStatuses.Confirmed &&
                    item.SelectionStatus == ClientEvidenceSelectionStatuses.Current);
                if (!validEvidence)
                {
                    throw new ValidationException("Only current, confirmed and verified evidence for this client may be linked.");
                }
            }

            response.RiskFactorOptionId = input.SelectedOptionId;
            response.SelectedOption = selectedOption;
            response.ClientEvidenceItemId = input.EvidenceItemId;
            response.Explanation = Normalize(input.Explanation);
        }

        assessment.HasPepExposure = model.HasPepExposure;
        assessment.HasSanctionsConcern = model.HasSanctionsConcern;
        assessment.HasAdverseInformation = model.HasAdverseInformation;
        assessment.StandardControlsApplied = model.StandardControlsApplied;
        assessment.Narrative = Normalize(model.Narrative);
        assessment.IsOverride = model.IsOverride;
        assessment.FinalRating = model.IsOverride ? Normalize(model.OverrideRating) : null;
        assessment.OverrideReason = model.IsOverride ? Normalize(model.OverrideReason) : null;
        assessment.UpdatedAtUtc = DateTime.UtcNow;
        Calculate(assessment);
        db.ComplianceAuditEvents.Add(CreateAudit(assessment.Id, "DraftUpdated", user, reason, AuditSummary(assessment)));
        await db.SaveChangesAsync();
    }

    public async Task FinaliseAsync(int assessmentId, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var assessment = await LoadAssessmentForMutationAsync(assessmentId);
        EnsureDraft(assessment);

        var readiness = await evidenceReadinessService.LoadClientReadinessAsync(assessment.ClientId);
        if (!readiness.IsReadyForRiskAssessment)
        {
            throw new InvalidOperationException($"The assessment cannot be finalised while {readiness.BlockedCount} blocking evidence item(s) remain.");
        }
        if (assessment.Responses.Any(response => response.RiskFactorOptionId is null))
        {
            throw new ValidationException("Select an answer for every risk factor.");
        }
        if (assessment.Responses.Any(response => string.IsNullOrWhiteSpace(response.Explanation)))
        {
            throw new ValidationException("Explain every selected risk-factor answer.");
        }
        if (!assessment.StandardControlsApplied)
        {
            throw new ValidationException("Confirm that Kanaan's standard methodology controls have been applied.");
        }
        if (assessment.HasSanctionsConcern)
        {
            throw new InvalidOperationException("A sanctions/TFS concern must be escalated and resolved before the assessment can be finalised.");
        }

        Calculate(assessment);
        if (assessment.IsOverride)
        {
            if (string.IsNullOrWhiteSpace(assessment.FinalRating) || string.IsNullOrWhiteSpace(assessment.OverrideReason))
            {
                throw new ValidationException("An override requires a final rating and a reason.");
            }
            if (HasMandatoryHighTrigger(assessment) && !IsHigh(assessment.FinalRating))
            {
                throw new InvalidOperationException("A mandatory High-risk trigger cannot be reduced by an override.");
            }
        }
        else
        {
            assessment.FinalRating = assessment.CalculatedRating;
        }

        assessment.RequiresEdd = IsHigh(assessment.FinalRating) ||
                                 assessment.HasPepExposure ||
                                 assessment.HasAdverseInformation;
        assessment.Status = assessment.RequiresEdd || assessment.IsOverride
            ? ClientRiskAssessmentStatuses.PendingKiApproval
            : ClientRiskAssessmentStatuses.Finalised;
        assessment.EffectiveDate = DateOnly.FromDateTime(DateTime.Today);
        assessment.NextReviewDate = CalculateNextReviewDate(assessment);
        assessment.FinalisedAtUtc = DateTime.UtcNow;
        assessment.FinalisedBy = user;
        assessment.UpdatedAtUtc = DateTime.UtcNow;
        assessment.SnapshotJson = CreateSnapshot(assessment);

        if (assessment.Status == ClientRiskAssessmentStatuses.Finalised)
        {
            await SupersedePriorAssessmentsAsync(assessment);
        }
        db.ComplianceAuditEvents.Add(CreateAudit(assessment.Id, "Finalised", user, reason, AuditSummary(assessment)));
        await db.SaveChangesAsync();
    }

    public async Task ApproveAsync(int assessmentId, string? userName, string reason)
    {
        RequireReason(reason);
        var approver = RequireUser(userName);
        var assessment = await LoadAssessmentForMutationAsync(assessmentId);
        if (assessment.Status != ClientRiskAssessmentStatuses.PendingKiApproval)
        {
            throw new InvalidOperationException("Only an assessment pending KI approval can be approved.");
        }
        if (assessment.Approvals.Any(item => string.Equals(item.Approver, approver, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("This KI has already approved the assessment.");
        }

        assessment.Approvals.Add(new ClientRiskAssessmentApproval
        {
            Approver = approver,
            Decision = ComplianceStatuses.Approved,
            Reason = reason.Trim()
        });
        if (assessment.Approvals.Count(item => item.Decision == ComplianceStatuses.Approved) >= 2)
        {
            assessment.Status = ClientRiskAssessmentStatuses.Approved;
            assessment.ApprovedAtUtc = DateTime.UtcNow;
            await SupersedePriorAssessmentsAsync(assessment);
        }
        assessment.UpdatedAtUtc = DateTime.UtcNow;
        db.ComplianceAuditEvents.Add(CreateAudit(
            assessment.Id,
            assessment.Status == ClientRiskAssessmentStatuses.Approved ? "ApprovedByBothKIs" : "KiApprovalRecorded",
            approver,
            reason,
            new { assessment.Status, ApprovalCount = assessment.Approvals.Count }));
        await db.SaveChangesAsync();
    }

    public async Task ReturnToDraftAsync(int assessmentId, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var assessment = await LoadAssessmentForMutationAsync(assessmentId);
        if (assessment.Status != ClientRiskAssessmentStatuses.PendingKiApproval)
        {
            throw new InvalidOperationException("Only an assessment pending KI approval can be returned.");
        }

        db.ClientRiskAssessmentApprovals.RemoveRange(assessment.Approvals);
        assessment.Approvals.Clear();
        assessment.Status = ClientRiskAssessmentStatuses.Draft;
        assessment.FinalisedAtUtc = null;
        assessment.FinalisedBy = null;
        assessment.EffectiveDate = null;
        assessment.NextReviewDate = null;
        assessment.SnapshotJson = null;
        assessment.UpdatedAtUtc = DateTime.UtcNow;
        db.ComplianceAuditEvents.Add(CreateAudit(assessment.Id, "ReturnedToDraft", user, reason, AuditSummary(assessment)));
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ClientRiskPortfolioItem>> LoadPortfolioAsync(string? search)
    {
        var query = db.ClientRiskAssessments.AsNoTracking()
            .Where(item => item.Status != ClientRiskAssessmentStatuses.Draft &&
                           item.Status != ClientRiskAssessmentStatuses.Superseded)
            .Include(item => item.Client)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(item => item.Client!.DisplayName.Contains(term) ||
                                        (item.Client.KanaanId != null && item.Client.KanaanId.Contains(term)) ||
                                        (item.FinalRating != null && item.FinalRating.Contains(term)));
        }

        var rows = await query.OrderByDescending(item => item.Id)
            .Select(item => new
            {
                item.Id,
                item.ClientId,
                item.Client!.DisplayName,
                item.Client.KanaanId,
                Rating = item.FinalRating ?? item.CalculatedRating,
                item.Status,
                item.NextReviewDate
            })
            .ToListAsync();
        return rows.GroupBy(item => item.ClientId)
            .Select(group => group.OrderByDescending(item => item.Id).First())
            .OrderBy(item => item.Rating)
            .ThenBy(item => item.DisplayName)
            .Select(item => new ClientRiskPortfolioItem(
                item.ClientId,
                item.DisplayName,
                item.KanaanId,
                item.Rating,
                item.Status,
                item.NextReviewDate))
            .ToList();
    }

    private async Task<RiskMethodologyVersion> LoadActiveMethodologyAsync()
        => await db.RiskMethodologyVersions
               .Include(methodology => methodology.Factors).ThenInclude(factor => factor.Options)
               .Include(methodology => methodology.Bands)
               .Where(methodology => methodology.Status == ComplianceStatuses.Active)
               .OrderByDescending(methodology => methodology.ActivatedAtUtc)
               .FirstOrDefaultAsync()
           ?? throw new InvalidOperationException("No active client-risk methodology is available. Review and activate one in Compliance Settings.");

    private async Task<ClientRiskAssessment> LoadAssessmentForMutationAsync(int assessmentId)
        => await db.ClientRiskAssessments
               .Include(item => item.Client)
               .Include(item => item.MethodologyVersion).ThenInclude(methodology => methodology!.Bands)
               .Include(item => item.Responses).ThenInclude(response => response.FactorDefinition).ThenInclude(factor => factor!.Options)
               .Include(item => item.Responses).ThenInclude(response => response.SelectedOption)
               .Include(item => item.Responses).ThenInclude(response => response.EvidenceItem)
               .Include(item => item.Approvals)
               .SingleOrDefaultAsync(item => item.Id == assessmentId)
           ?? throw new KeyNotFoundException("Risk assessment not found.");

    private static void ValidateMethodology(RiskMethodologyVersion methodology)
    {
        if (methodology.Factors.Count == 0 || methodology.Factors.Any(factor => factor.Options.Count == 0))
        {
            throw new InvalidOperationException("The active methodology must contain factors and answer options.");
        }
        if (methodology.Bands.Count == 0)
        {
            throw new InvalidOperationException("The active methodology must contain risk bands.");
        }
    }

    private static void EnsureDraft(ClientRiskAssessment assessment)
    {
        if (assessment.Status != ClientRiskAssessmentStatuses.Draft)
        {
            throw new InvalidOperationException("Only a draft assessment can be changed.");
        }
    }

    private static void Calculate(ClientRiskAssessment assessment)
    {
        foreach (var response in assessment.Responses)
        {
            response.Score = response.SelectedOption?.Score ?? 0;
            response.WeightedScore = response.Score * (response.FactorDefinition?.Weight ?? 0);
        }
        assessment.CalculatedScore = assessment.Responses.Sum(response => response.WeightedScore);
        var band = assessment.MethodologyVersion!.Bands
            .OrderBy(item => item.MinimumScore)
            .FirstOrDefault(item => assessment.CalculatedScore >= item.MinimumScore &&
                                    (!item.MaximumScore.HasValue || assessment.CalculatedScore <= item.MaximumScore.Value));
        assessment.CalculatedRating = band?.Name;
        if (HasMandatoryHighTrigger(assessment))
        {
            assessment.CalculatedRating = "High";
        }
    }

    private static bool HasMandatoryHighTrigger(ClientRiskAssessment assessment)
        => assessment.Responses.Any(response => response.SelectedOption?.TriggersHighRisk == true);

    private static DateOnly CalculateNextReviewDate(ClientRiskAssessment assessment)
    {
        var band = assessment.MethodologyVersion!.Bands
            .FirstOrDefault(item => string.Equals(item.Name, assessment.FinalRating, StringComparison.OrdinalIgnoreCase));
        var months = band?.ReviewMonths > 0 ? band.ReviewMonths : IsHigh(assessment.FinalRating) ? 12 : 36;
        return DateOnly.FromDateTime(DateTime.Today.AddMonths(months));
    }

    private async Task SupersedePriorAssessmentsAsync(ClientRiskAssessment current)
    {
        var prior = await db.ClientRiskAssessments
            .Where(item => item.ClientId == current.ClientId &&
                           item.Id != current.Id &&
                           (item.Status == ClientRiskAssessmentStatuses.Finalised ||
                            item.Status == ClientRiskAssessmentStatuses.Approved))
            .ToListAsync();
        foreach (var item in prior)
        {
            item.Status = ClientRiskAssessmentStatuses.Superseded;
            item.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private static string CreateSnapshot(ClientRiskAssessment assessment)
        => JsonSerializer.Serialize(new
        {
            Client = new
            {
                assessment.ClientId,
                assessment.Client!.KanaanId,
                assessment.Client.DisplayName,
                assessment.Client.ClientCategory
            },
            Methodology = new
            {
                assessment.MethodologyVersion!.Id,
                assessment.MethodologyVersion.Name,
                assessment.MethodologyVersion.VersionLabel,
                Factors = assessment.Responses.OrderBy(response => response.FactorDefinition!.SortOrder).Select(response => new
                {
                    response.FactorDefinition!.Code,
                    response.FactorDefinition.Name,
                    response.FactorDefinition.Weight,
                    Option = response.SelectedOption?.Label,
                    response.Score,
                    response.WeightedScore,
                    response.Explanation,
                    Evidence = response.EvidenceItem is null ? null : new { response.EvidenceItem.Id, response.EvidenceItem.Title, response.EvidenceItem.SourcePath }
                }),
                Bands = assessment.MethodologyVersion.Bands.OrderBy(band => band.SortOrder).Select(band => new { band.Name, band.MinimumScore, band.MaximumScore, band.ReviewMonths })
            },
            Result = new
            {
                assessment.CalculatedScore,
                assessment.CalculatedRating,
                assessment.FinalRating,
                assessment.IsOverride,
                assessment.OverrideReason,
                assessment.HasPepExposure,
                assessment.HasAdverseInformation,
                assessment.RequiresEdd,
                assessment.StandardControlsApplied,
                assessment.Narrative,
                assessment.EffectiveDate,
                assessment.NextReviewDate
            }
        }, SnapshotOptions);

    private static ComplianceAuditEvent CreateAudit(int assessmentId, string action, string user, string reason, object value)
        => new()
        {
            EntityType = nameof(ClientRiskAssessment),
            EntityId = assessmentId,
            Action = action,
            NewValueJson = JsonSerializer.Serialize(value, SnapshotOptions),
            UserName = user,
            TimestampUtc = DateTime.UtcNow,
            Reason = reason.Trim()
        };

    private static object AuditSummary(ClientRiskAssessment assessment)
        => new
        {
            assessment.Id,
            assessment.ClientId,
            assessment.RiskMethodologyVersionId,
            assessment.Status,
            assessment.CalculatedScore,
            assessment.CalculatedRating,
            assessment.FinalRating,
            assessment.IsOverride,
            assessment.HasPepExposure,
            assessment.HasSanctionsConcern,
            assessment.HasAdverseInformation,
            assessment.RequiresEdd,
            assessment.EffectiveDate,
            assessment.NextReviewDate,
            Responses = assessment.Responses.Select(response => new
            {
                response.RiskFactorDefinitionId,
                response.RiskFactorOptionId,
                response.ClientEvidenceItemId,
                response.Score,
                response.WeightedScore,
                response.Explanation
            })
        };

    private static ClientRiskAssessmentSummary MapAssessment(ClientRiskAssessment assessment)
        => new(
            assessment.Id,
            assessment.Status,
            assessment.CalculatedScore,
            assessment.CalculatedRating,
            assessment.FinalRating,
            assessment.IsOverride,
            assessment.OverrideReason,
            assessment.HasPepExposure,
            assessment.HasSanctionsConcern,
            assessment.HasAdverseInformation,
            assessment.RequiresEdd,
            assessment.StandardControlsApplied,
            assessment.Narrative,
            assessment.EffectiveDate,
            assessment.NextReviewDate,
            assessment.PreparedBy,
            assessment.FinalisedBy,
            assessment.Approvals.OrderBy(item => item.DecidedAtUtc)
                .Select(item => new ClientRiskApprovalSummary(item.Approver, item.Reason, item.DecidedAtUtc)).ToList());

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("A reason is required.");
        }
    }

    private static string RequireUser(string? userName)
        => Normalize(userName) ?? throw new ValidationException("The current user identity is required.");

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool IsHigh(string? value) => string.Equals(value, "High", StringComparison.OrdinalIgnoreCase);
}

public sealed class ClientRiskAssessmentPageModel
{
    public int ClientId { get; init; }
    public string DisplayName { get; init; } = "";
    public string? KanaanId { get; init; }
    public string ClientCategory { get; init; } = "";
    public bool IsReadyForRiskAssessment { get; init; }
    public int BlockingEvidenceCount { get; init; }
    public bool HasActiveMethodology { get; init; }
    public string? ActiveMethodologyName { get; init; }
    public ClientRiskAssessmentSummary? Assessment { get; init; }
    public IReadOnlyList<ClientRiskFactorEditModel> Factors { get; init; } = [];
    public IReadOnlyList<ClientRiskEvidenceOption> EvidenceOptions { get; init; } = [];
    public IReadOnlyList<ClientRiskAssessmentHistoryItem> History { get; init; } = [];
}

public sealed record ClientRiskAssessmentSummary(
    int Id,
    string Status,
    decimal CalculatedScore,
    string? CalculatedRating,
    string? FinalRating,
    bool IsOverride,
    string? OverrideReason,
    bool HasPepExposure,
    bool HasSanctionsConcern,
    bool HasAdverseInformation,
    bool RequiresEdd,
    bool StandardControlsApplied,
    string? Narrative,
    DateOnly? EffectiveDate,
    DateOnly? NextReviewDate,
    string? PreparedBy,
    string? FinalisedBy,
    IReadOnlyList<ClientRiskApprovalSummary> Approvals);

public sealed class ClientRiskAssessmentEditModel
{
    public List<ClientRiskFactorInput> Factors { get; set; } = [];
    public bool HasPepExposure { get; set; }
    public bool HasSanctionsConcern { get; set; }
    public bool HasAdverseInformation { get; set; }
    public bool StandardControlsApplied { get; set; }
    public string? Narrative { get; set; }
    public bool IsOverride { get; set; }
    public string? OverrideRating { get; set; }
    public string? OverrideReason { get; set; }
}

public sealed class ClientRiskFactorEditModel
{
    public int FactorId { get; init; }
    public string Code { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public decimal Weight { get; init; }
    public int? SelectedOptionId { get; set; }
    public int? EvidenceItemId { get; set; }
    public string? Explanation { get; set; }
    public IReadOnlyList<ClientRiskFactorOptionModel> Options { get; init; } = [];
}

public sealed record ClientRiskFactorInput(int FactorId, int? SelectedOptionId, int? EvidenceItemId, string? Explanation);
public sealed record ClientRiskFactorOptionModel(int Id, string Label, int Score, bool TriggersHighRisk);
public sealed record ClientRiskEvidenceOption(int Id, string EvidenceType, string Title);
public sealed record ClientRiskApprovalSummary(string Approver, string Reason, DateTime DecidedAtUtc);
public sealed record ClientRiskAssessmentHistoryItem(int Id, string Status, string? Rating, decimal Score, DateOnly? EffectiveDate, DateOnly? NextReviewDate, string? FinalisedBy);
public sealed record ClientRiskPortfolioItem(int ClientId, string DisplayName, string? KanaanId, string? Rating, string Status, DateOnly? NextReviewDate);
