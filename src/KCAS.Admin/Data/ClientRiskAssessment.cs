using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientRiskAssessment
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client? Client { get; set; }
    public int RiskMethodologyVersionId { get; set; }
    public RiskMethodologyVersion? MethodologyVersion { get; set; }
    public int? PreviousAssessmentId { get; set; }
    public ClientRiskAssessment? PreviousAssessment { get; set; }
    public List<ClientRiskAssessment> Reassessments { get; set; } = [];

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
    [MaxLength(48)]
    public string ReviewTriggerType { get; set; } = ClientRiskReviewTriggerTypes.Initial;
    public string? ReviewTriggerReason { get; set; }
    public DateTime? ReviewTriggeredAtUtc { get; set; }

    [MaxLength(191)]
    public string? PreparedBy { get; set; }

    [MaxLength(191)]
    public string? FinalisedBy { get; set; }

    [MaxLength(191)]
    public string? ReviewTriggeredBy { get; set; }

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
    public DateTime? ConfirmedAtUtc { get; set; }
    [MaxLength(191)]
    public string? ConfirmedBy { get; set; }
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

public static class ClientRiskReviewTriggerTypes
{
    public const string Initial = "Initial";
    public const string PeriodicReview = "PeriodicReview";
    public const string ClientInformationChange = "ClientInformationChange";
    public const string OwnershipChange = "OwnershipChange";
    public const string ProductChange = "ProductChange";
    public const string GeographyChange = "GeographyChange";
    public const string ScreeningEvent = "ScreeningEvent";
    public const string UnusualActivity = "UnusualActivity";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> ReassessmentTypes =
    [
        PeriodicReview,
        ClientInformationChange,
        OwnershipChange,
        ProductChange,
        GeographyChange,
        ScreeningEvent,
        UnusualActivity,
        Other
    ];
}
