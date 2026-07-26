using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class BusinessRiskAssessment
{
    public int Id { get; set; }

    [MaxLength(191)]
    public string Name { get; set; } = "";

    public int AssessmentYear { get; set; }
    public DateOnly AsAtDate { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = ComplianceStatuses.Draft;

    public string Scope { get; set; } = "";
    public string MethodologyNarrative { get; set; } = "";
    public string ManagementJudgement { get; set; } = "";
    public string Limitations { get; set; } = "";
    public string RiskTolerance { get; set; } = "";
    public string? PortfolioSnapshotJson { get; set; }
    public string? SnapshotJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }

    [MaxLength(191)]
    public string? PreparedBy { get; set; }

    [MaxLength(191)]
    public string? UpdatedBy { get; set; }

    public List<BusinessRiskItem> Items { get; set; } = [];
    public List<BusinessRiskApproval> Approvals { get; set; } = [];
}

public sealed class BusinessRiskItem
{
    public int Id { get; set; }
    public int BusinessRiskAssessmentId { get; set; }
    public BusinessRiskAssessment? Assessment { get; set; }

    [MaxLength(48)]
    public string Category { get; set; } = "";

    public string RiskStatement { get; set; } = "";
    public string EvidenceAndRationale { get; set; } = "";
    public int Likelihood { get; set; } = 1;
    public int Impact { get; set; } = 1;
    public int InherentScore { get; set; } = 1;

    [MaxLength(32)]
    public string InherentRating { get; set; } = BusinessRiskRatings.Low;

    public string KeyControls { get; set; } = "";

    [MaxLength(32)]
    public string ControlEffectiveness { get; set; } = BusinessRiskControlEffectiveness.PartiallyEffective;

    [MaxLength(32)]
    public string ResidualRating { get; set; } = BusinessRiskRatings.Standard;

    public string ResidualRationale { get; set; } = "";

    [MaxLength(32)]
    public string TreatmentDecision { get; set; } = BusinessRiskTreatmentDecisions.Accept;

    [MaxLength(191)]
    public string Owner { get; set; } = "";

    public DateOnly? DueDate { get; set; }
    public int SortOrder { get; set; }
}

public sealed class BusinessRiskApproval
{
    public int Id { get; set; }
    public int BusinessRiskAssessmentId { get; set; }
    public BusinessRiskAssessment? Assessment { get; set; }

    [MaxLength(191)]
    public string Approver { get; set; } = "";

    public string Reason { get; set; } = "";
    public DateTime ApprovedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class BusinessRiskCategories
{
    public const string Clients = "Clients";
    public const string ProductsServices = "ProductsServices";
    public const string DeliveryChannels = "DeliveryChannels";
    public const string Geography = "Geography";
    public const string Activity = "Activity";
    public const string ExternalThreats = "ExternalThreats";

    public static readonly IReadOnlyList<string> All =
    [
        Clients,
        ProductsServices,
        DeliveryChannels,
        Geography,
        Activity,
        ExternalThreats
    ];

    public static string Display(string value) => value switch
    {
        ProductsServices => "Products and services",
        DeliveryChannels => "Delivery channels",
        ExternalThreats => "External threats",
        _ => value
    };
}

public static class BusinessRiskRatings
{
    public const string Low = "Low";
    public const string Standard = "Standard";
    public const string High = "High";
    public static readonly IReadOnlyList<string> All = [Low, Standard, High];
}

public static class BusinessRiskControlEffectiveness
{
    public const string Effective = "Effective";
    public const string PartiallyEffective = "PartiallyEffective";
    public const string Ineffective = "Ineffective";
    public static readonly IReadOnlyList<string> All = [Effective, PartiallyEffective, Ineffective];
}

public static class BusinessRiskTreatmentDecisions
{
    public const string Accept = "Accept";
    public const string Treat = "Treat";
    public const string Avoid = "Avoid";
    public const string Escalate = "Escalate";
    public static readonly IReadOnlyList<string> All = [Accept, Treat, Avoid, Escalate];
}
