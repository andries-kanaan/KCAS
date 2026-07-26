using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class RmcpVersion
{
    public int Id { get; set; }
    public int BusinessRiskAssessmentId { get; set; }
    public BusinessRiskAssessment? BusinessRiskAssessment { get; set; }

    [MaxLength(191)]
    public string Title { get; set; } = "";

    [MaxLength(64)]
    public string VersionReference { get; set; } = "";

    [MaxLength(32)]
    public string Status { get; set; } = ComplianceStatuses.Draft;

    public string Scope { get; set; } = "";

    [MaxLength(191)]
    public string Owner { get; set; } = "";

    public int ReviewMonths { get; set; } = 12;
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? NextReviewDate { get; set; }

    [MaxLength(1024)]
    public string SignedDocumentLocation { get; set; } = "";

    [MaxLength(1024)]
    public string ApprovalResolutionLocation { get; set; } = "";

    public string ChangeSummary { get; set; } = "";
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

    public List<RmcpControl> Controls { get; set; } = [];
}

public sealed class RmcpControl
{
    public int Id { get; set; }
    public int RmcpVersionId { get; set; }
    public RmcpVersion? RmcpVersion { get; set; }
    public int? BusinessRiskItemId { get; set; }
    public BusinessRiskItem? BusinessRiskItem { get; set; }

    [MaxLength(48)]
    public string Domain { get; set; } = "";

    [MaxLength(64)]
    public string Code { get; set; } = "";

    [MaxLength(191)]
    public string Title { get; set; } = "";

    public string ProcedureSummary { get; set; } = "";

    [MaxLength(191)]
    public string Owner { get; set; } = "";

    [MaxLength(64)]
    public string Frequency { get; set; } = "";

    public string EvidenceExpectation { get; set; } = "";
    public string MonitoringMethod { get; set; } = "";
    public string EscalationProcedure { get; set; } = "";
    public bool HasGap { get; set; }
    public string? GapDescription { get; set; }

    [MaxLength(191)]
    public string? TreatmentOwner { get; set; }

    public DateOnly? TreatmentDueDate { get; set; }
    public int? ComplianceTaskId { get; set; }
    public int SortOrder { get; set; }
}

public static class RmcpControlDomains
{
    public const string ClientRisk = "ClientRisk";
    public const string Cdd = "CDD";
    public const string Edd = "EDD";
    public const string Screening = "Screening";
    public const string Records = "Records";
    public const string Reporting = "Reporting";
    public const string Training = "Training";
    public const string Governance = "Governance";
    public const string Review = "Review";

    public static readonly IReadOnlyList<string> All =
    [
        ClientRisk, Cdd, Edd, Screening, Records, Reporting, Training, Governance, Review
    ];

    public static string Display(string value) => value switch
    {
        ClientRisk => "Client risk",
        Cdd => "Customer due diligence",
        Edd => "Enhanced due diligence",
        _ => value
    };
}
