using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class EmployeeProfile
{
    public int Id { get; set; }
    [MaxLength(64)] public string TransferKey { get; set; } = Guid.NewGuid().ToString("N");
    [MaxLength(191)] public string DisplayName { get; set; } = "";
    [MaxLength(191)] public string LegalName { get; set; } = "";
    public string Aliases { get; set; } = "";
    [MaxLength(191)] public string? Email { get; set; }
    [MaxLength(32)] public string EmploymentStatus { get; set; } = "Current";
    public string IdentityReference { get; set; } = "";
    public string Responsibilities { get; set; } = "";
    public string AuthorityLimits { get; set; } = "";
    public string SourceReference { get; set; } = "";
    [MaxLength(32)] public string RoleExposure { get; set; } = "Standard";
    public string RiskRationale { get; set; } = "";
    public string SelectedChecks { get; set; } = "";
    public bool RequireTraining { get; set; } = true;
    public bool RequireRegulatedCompetence { get; set; }
    public bool RequireAdditionalCheck { get; set; }
    public int ProposedReviewMonths { get; set; } = 12;
    public DateOnly? EmploymentStart { get; set; }
    public DateOnly? EmploymentEnd { get; set; }
    public string ExternalAccessScope { get; set; } = "";
    [MaxLength(64)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
    [MaxLength(64)] public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class EmployeeTransferRecord
{
    public long Id { get; set; }
    [MaxLength(64)] public string PackageId { get; set; } = "";
    [MaxLength(16)] public string Direction { get; set; } = "Incoming";
    [MaxLength(64)] public string EmployeeKey { get; set; } = "";
    [MaxLength(64)] public string SourceSystemKey { get; set; } = "";
    public int EmployeeProfileId { get; set; }
    [MaxLength(64)] public string SourceDigest { get; set; } = "";
    [MaxLength(64)] public string LocalDigest { get; set; } = "";
    public string MappingJson { get; set; } = "{}";
    public string ActorNamesJson { get; set; } = "{}";
    [MaxLength(1024)] public string StoragePath { get; set; } = "";
    [MaxLength(191)] public string FileName { get; set; } = "";
    [MaxLength(64)] public string UserId { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime PackageCreatedAtUtc { get; set; }
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class EmployeeAccountLink
{
    public int Id { get; set; }
    public int EmployeeProfileId { get; set; }
    [MaxLength(64)] public string UserId { get; set; } = "";
    public string VerificationReference { get; set; } = "";
    [MaxLength(64)] public string LinkedByUserId { get; set; } = "";
    public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class EmployeeComplianceReview
{
    public int Id { get; set; }
    public int EmployeeProfileId { get; set; }
    [MaxLength(32)] public string Status { get; set; } = "Draft";
    [MaxLength(64)] public string ProfileVersion { get; set; } = "";
    [MaxLength(64)] public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public string ProfileSnapshotJson { get; set; } = "";
    [MaxLength(64)] public string PreparedByUserId { get; set; } = "";
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public DateOnly? NextReviewDate { get; set; }
}

public sealed class EmployeeComplianceCheck
{
    public int Id { get; set; }
    public int EmployeeComplianceReviewId { get; set; }
    [MaxLength(48)] public string Kind { get; set; } = "Competence";
    [MaxLength(32)] public string Outcome { get; set; } = "Unresolved";
    [MaxLength(32)] public string PerformerType { get; set; } = "Manual";
    [MaxLength(191)] public string Performer { get; set; } = "";
    public DateTime PerformedAtUtc { get; set; }
    public string SourceReference { get; set; } = "";
    [MaxLength(1024)] public string? SourceUrl { get; set; }
    [MaxLength(191)] public string? ListVersion { get; set; }
    public string IdentifierScope { get; set; } = "";
    public string Finding { get; set; } = "";
    public string Limitations { get; set; } = "";
    [MaxLength(1024)] public string? EvidencePath { get; set; }
    [MaxLength(64)] public string? EvidenceSha256 { get; set; }
    public int? SupersedesCheckId { get; set; }
    public int? EmployeeTfsBatchId { get; set; }
    [MaxLength(64)] public string RecordedByUserId { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class EmployeeEvidenceDocument
{
    public int Id { get; set; }
    public int EmployeeProfileId { get; set; }
    [MaxLength(191)] public string Title { get; set; } = "";
    [MaxLength(48)] public string Category { get; set; } = "Competence";
    [MaxLength(1024)] public string EvidencePath { get; set; } = "";
    [MaxLength(64)] public string EvidenceSha256 { get; set; } = "";
    public string SourceNote { get; set; } = "";
    [MaxLength(64)] public string LinkedByUserId { get; set; } = "";
    public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class EmployeeAccessConfirmation
{
    public int Id { get; set; }
    public int EmployeeComplianceReviewId { get; set; }
    [MaxLength(32)] public string Kind { get; set; } = "KCAS";
    public string SystemAndScope { get; set; } = "";
    public string ApprovedScope { get; set; } = "";
    public string ActualScope { get; set; } = "";
    public string ActionConfirmation { get; set; } = "";
    public bool IsAligned { get; set; }
    public DateTime VerifiedAtUtc { get; set; }
    [MaxLength(191)] public string VerifiedBy { get; set; } = "";
    public string EvidenceReference { get; set; } = "";
    public string AccountScopeJson { get; set; } = "";
    [MaxLength(64)] public string RecordedByUserId { get; set; } = "";
}

public sealed class EmployeeReviewDecision
{
    public int Id { get; set; }
    public int EmployeeComplianceReviewId { get; set; }
    [MaxLength(32)] public string Decision { get; set; } = "FollowUp";
    public string Reason { get; set; } = "";
    public string RestrictionsAndFollowUp { get; set; } = "";
    public int? ApprovedReviewMonths { get; set; }
    [MaxLength(64)] public string ReviewerUserId { get; set; } = "";
    [MaxLength(191)] public string ReviewerName { get; set; } = "";
    public int ReviewerEmployeeProfileId { get; set; }
    public DateTime DecidedAtUtc { get; set; } = DateTime.UtcNow;
    public string EvidenceSnapshotJson { get; set; } = "";
}

public sealed class EmployeeComplianceTask
{
    public int Id { get; set; }
    public int EmployeeProfileId { get; set; }
    [MaxLength(191)] public string TriggerKey { get; set; } = "";
    [MaxLength(48)] public string Kind { get; set; } = "InitialReview";
    [MaxLength(32)] public string Status { get; set; } = "Open";
    public string Reason { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string RecipientUserIdsJson { get; set; } = "[]";
    [MaxLength(64)] public string? AcknowledgedByUserId { get; set; }
    public DateTime? AcknowledgedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public int? EmployeeTfsBatchId { get; set; }
}

public sealed class EmployeeTfsBatch
{
    public int Id { get; set; }
    [MaxLength(191)] public string SourceVersion { get; set; } = "";
    [MaxLength(1024)] public string SourceUrl { get; set; } = "";
    public string Reason { get; set; } = "";
    [MaxLength(64)] public string CreatedByUserId { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class EmployeeComplianceAuditEvent
{
    public int Id { get; set; }
    public int EmployeeProfileId { get; set; }
    [MaxLength(48)] public string Action { get; set; } = "";
    [MaxLength(64)] public string UserId { get; set; } = "";
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = "";
    public string SnapshotJson { get; set; } = "";
}

public static class EmployeeComplianceValues
{
    public static readonly string[] EmploymentStatuses = ["Prospective", "Current", "Inactive"];
    public static readonly string[] RoleExposures = ["Standard", "Heightened"];
    public static readonly string[] CheckKinds = ["Identity", "Competence", "Integrity", "TFS", "Training", "RegulatedCompetence", "Additional"];
    public static readonly string[] CheckOutcomes = ["Satisfied", "NoMatch", "FalsePositive", "Unresolved", "SourceFailed", "ConfirmedDesignation", "Concern"];
    public static readonly string[] DecisionTypes = ["Approved", "Restricted", "FollowUp"];
    public static string CheckLabel(string value) => value switch
    {
        "TFS" => "Targeted financial sanctions",
        "RegulatedCompetence" => "FAIS competence / current-cycle evidence",
        _ => value
    };
}
