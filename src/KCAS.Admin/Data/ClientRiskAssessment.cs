using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientRiskAssessment
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }
    public int RiskMethodologyVersionId { get; set; }
    public RiskMethodologyVersion? MethodologyVersion { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = ClientRiskAssessmentStatuses.Draft;

    public decimal CalculatedScore { get; set; }

    [MaxLength(96)]
    public string? CalculatedRating { get; set; }

    [MaxLength(96)]
    public string? FinalRating { get; set; }

    public bool IsOverride { get; set; }
    public string? OverrideReason { get; set; }
    public bool HasPepExposure { get; set; }
    public bool HasSanctionsConcern { get; set; }
    public bool HasAdverseInformation { get; set; }
    public bool RequiresEdd { get; set; }
    public bool StandardControlsApplied { get; set; }
    public string? Narrative { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? NextReviewDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FinalisedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    [MaxLength(191)]
    public string? PreparedBy { get; set; }

    [MaxLength(191)]
    public string? FinalisedBy { get; set; }

    public string? SnapshotJson { get; set; }
    public List<ClientRiskAssessmentResponse> Responses { get; set; } = [];
    public List<ClientRiskAssessmentApproval> Approvals { get; set; } = [];
}

public sealed class ClientRiskAssessmentResponse
{
    public int Id { get; set; }
    public int ClientRiskAssessmentId { get; set; }
    public ClientRiskAssessment? Assessment { get; set; }
    public int RiskFactorDefinitionId { get; set; }
    public RiskFactorDefinition? FactorDefinition { get; set; }
    public int? RiskFactorOptionId { get; set; }
    public RiskFactorOption? SelectedOption { get; set; }
    public int? ClientEvidenceItemId { get; set; }
    public ClientEvidenceItem? EvidenceItem { get; set; }
    public int Score { get; set; }
    public decimal WeightedScore { get; set; }
    public string? Explanation { get; set; }
}

public sealed class ClientRiskAssessmentApproval
{
    public int Id { get; set; }
    public int ClientRiskAssessmentId { get; set; }
    public ClientRiskAssessment? Assessment { get; set; }

    [MaxLength(191)]
    public string Approver { get; set; } = "";

    [MaxLength(32)]
    public string Decision { get; set; } = ComplianceStatuses.Approved;

    public string Reason { get; set; } = "";
    public DateTime DecidedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class ClientRiskAssessmentStatuses
{
    public const string Draft = "Draft";
    public const string Finalised = "Finalised";
    public const string PendingKiApproval = "PendingKIApproval";
    public const string Approved = "Approved";
    public const string Superseded = "Superseded";
}
