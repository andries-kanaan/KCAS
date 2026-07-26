using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class InspectionCase
{
    public int Id { get; set; }

    [MaxLength(96)]
    public string Reference { get; set; } = "";

    [MaxLength(240)]
    public string Title { get; set; } = "";

    [MaxLength(191)]
    public string RequestingAuthority { get; set; } = "";

    public DateOnly AsAtDate { get; set; }
    public DateOnly RequestDate { get; set; }
    public DateOnly DueDate { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = InspectionStatuses.Draft;

    public string Scope { get; set; } = "";

    [MaxLength(191)]
    public string Coordinator { get; set; } = "";

    public string? Notes { get; set; }
    public string? SnapshotJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? FrozenAtUtc { get; set; }

    [MaxLength(191)]
    public string? CreatedBy { get; set; }

    [MaxLength(191)]
    public string? UpdatedBy { get; set; }

    public List<InspectionRequestItem> Items { get; set; } = [];
    public List<InspectionReadinessCheck> ReadinessChecks { get; set; } = [];
}

public sealed class InspectionRequestItem
{
    public int Id { get; set; }
    public int InspectionCaseId { get; set; }
    public InspectionCase? InspectionCase { get; set; }

    [MaxLength(64)]
    public string Category { get; set; } = InspectionEvidenceCategories.Other;

    [MaxLength(240)]
    public string Title { get; set; } = "";

    public string? Description { get; set; }

    [MaxLength(191)]
    public string Owner { get; set; } = "";

    public DateOnly DueDate { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = InspectionItemStatuses.Open;

    public string? EvidenceTitle { get; set; }
    public string? EvidenceLocation { get; set; }

    [MaxLength(128)]
    public string? LinkedEntityType { get; set; }

    public int? LinkedEntityId { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    [MaxLength(191)]
    public string? CompletedBy { get; set; }

    public int SortOrder { get; set; }
}

public sealed class InspectionReadinessCheck
{
    public int Id { get; set; }
    public int InspectionCaseId { get; set; }
    public InspectionCase? InspectionCase { get; set; }

    [MaxLength(64)]
    public string CheckType { get; set; } = "";

    [MaxLength(32)]
    public string Status { get; set; } = InspectionCheckStatuses.Pending;

    public string? EvidenceLocation { get; set; }
    public string? Notes { get; set; }
    public DateTime? TestedAtUtc { get; set; }

    [MaxLength(191)]
    public string? TestedBy { get; set; }
}

public static class InspectionStatuses
{
    public const string Draft = "Draft";
    public const string Open = "Open";
    public const string Frozen = "Frozen";
    public const string Closed = "Closed";
}

public static class InspectionItemStatuses
{
    public const string Open = "Open";
    public const string InProgress = "InProgress";
    public const string Ready = "Ready";
    public const string NotApplicable = "NotApplicable";
}

public static class InspectionCheckStatuses
{
    public const string Pending = "Pending";
    public const string Passed = "Passed";
    public const string Failed = "Failed";
    public static readonly IReadOnlyList<string> All = [Pending, Passed, Failed];
}

public static class InspectionReadinessCheckTypes
{
    public const string AccessPermissions = "AccessPermissions";
    public const string AuditLog = "AuditLog";
    public const string SensitiveData = "SensitiveData";
    public const string Backup = "Backup";
    public const string Restore = "Restore";
    public const string Rollback = "Rollback";
    public const string Performance = "Performance";
    public const string TrainingSupport = "TrainingSupport";

    public static readonly IReadOnlyList<string> All =
    [
        AccessPermissions, AuditLog, SensitiveData, Backup, Restore, Rollback, Performance, TrainingSupport
    ];

    public static string Display(string value) => value switch
    {
        AccessPermissions => "Access and permissions",
        AuditLog => "Audit-log review",
        SensitiveData => "Sensitive-data handling",
        TrainingSupport => "User acceptance, training and support",
        _ => value
    };
}

public static class InspectionEvidenceCategories
{
    public const string Clients = "Clients";
    public const string ClientAssessments = "ClientAssessments";
    public const string Bra = "BRA";
    public const string Rmcp = "RMCP";
    public const string Approvals = "Approvals";
    public const string Training = "Training";
    public const string Monitoring = "Monitoring";
    public const string Remediation = "Remediation";
    public const string Governance = "Governance";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All =
    [
        Clients, ClientAssessments, Bra, Rmcp, Approvals, Training, Monitoring, Remediation, Governance, Other
    ];
}
