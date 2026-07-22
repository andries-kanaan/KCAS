using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class LegacyImportRowState
{
    public long Id { get; set; }

    public long LegacyImportRunId { get; set; }

    public LegacyImportRun LegacyImportRun { get; set; } = null!;

    [MaxLength(64)]
    public string SourceTable { get; set; } = string.Empty;

    public long SourceId { get; set; }

    [MaxLength(32)]
    public string Classification { get; set; } = LegacyImportClassifications.Unchanged;

    [MaxLength(32)]
    public string ApplyStatus { get; set; } = LegacyImportApplyStatuses.NotApplicable;

    [MaxLength(128)]
    public string? TargetEntityType { get; set; }

    public long? TargetEntityId { get; set; }

    [MaxLength(64)]
    public string IncomingFingerprint { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? BaselineFingerprint { get; set; }

    public string IncomingPayloadJson { get; set; } = "{}";

    public string? BaselinePayloadJson { get; set; }

    public DateTime? SourceUpdatedAt { get; set; }

    public string? Error { get; set; }

    public ICollection<LegacyImportDifference> Differences { get; } = [];
}

public static class LegacyImportClassifications
{
    public const string New = "New";
    public const string Unchanged = "Unchanged";
    public const string Changed = "Changed";
    public const string MissingFromSource = "MissingFromSource";
    public const string Invalid = "Invalid";
    public const string Orphaned = "Orphaned";
}

public static class LegacyImportApplyStatuses
{
    public const string NotApplicable = "NotApplicable";
    public const string PendingReview = "PendingReview";
    public const string Applied = "Applied";
    public const string Failed = "Failed";
}
