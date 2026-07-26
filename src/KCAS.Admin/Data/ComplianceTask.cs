namespace KCAS.Admin.Data;

public sealed class ComplianceTask
{
    public int Id { get; set; }
    public string TaskType { get; set; } = ComplianceTaskTypes.Remediation;
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Owner { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Priority { get; set; } = "Normal";
    public string Status { get; set; } = ComplianceStatuses.Draft;
    public string? LinkedEntityType { get; set; }
    public int? LinkedEntityId { get; set; }
    public int? ClientId { get; set; }
    public Client? Client { get; set; }
    public int? ClientRiskAssessmentId { get; set; }
    public ClientRiskAssessment? ClientRiskAssessment { get; set; }
    public int? BusinessRiskAssessmentId { get; set; }
    public BusinessRiskAssessment? BusinessRiskAssessment { get; set; }
    public int? RmcpVersionId { get; set; }
    public RmcpVersion? RmcpVersion { get; set; }
    public int? RmcpControlId { get; set; }
    public RmcpControl? RmcpControl { get; set; }
    public string? EvidenceSummary { get; set; }
    public string? Outcome { get; set; }
    public string? ClosureReason { get; set; }
    public string? ClosureNotes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EscalatedAtUtc { get; set; }
    public string? EscalatedBy { get; set; }
    public DateTime? ClosureRequestedAtUtc { get; set; }
    public string? ClosureRequestedBy { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string? ClosedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}

public static class ComplianceTaskTypes
{
    public const string PeriodicReview = "PeriodicReview";
    public const string TriggerReview = "TriggerReview";
    public const string Edd = "EDD";
    public const string ScreeningEscalation = "ScreeningEscalation";
    public const string UnusualActivityReview = "UnusualActivityReview";
    public const string ControlTest = "ControlTest";
    public const string TreatmentAction = "TreatmentAction";
    public const string Finding = "Finding";
    public const string Training = "Training";
    public const string Exception = "Exception";
    public const string Remediation = "Remediation";

    public static readonly IReadOnlyList<string> All =
    [
        PeriodicReview, TriggerReview, Edd, ScreeningEscalation, UnusualActivityReview,
        ControlTest, TreatmentAction, Finding, Training, Exception, Remediation
    ];

    public static readonly IReadOnlyList<string> Material = [Edd, ScreeningEscalation, UnusualActivityReview];

    public static string Display(string value) => value switch
    {
        PeriodicReview => "Periodic client review",
        TriggerReview => "Trigger-event client review",
        Edd => "Enhanced due diligence",
        ScreeningEscalation => "Screening escalation",
        UnusualActivityReview => "Unusual activity review",
        ControlTest => "Control test",
        TreatmentAction => "Treatment action",
        _ => value
    };
}

public static class ComplianceWorkStatuses
{
    public const string Open = "Open";
    public const string InProgress = "InProgress";
    public const string PendingClosure = "PendingClosure";
}
