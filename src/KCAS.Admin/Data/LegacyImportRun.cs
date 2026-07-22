using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class LegacyImportRun
{
    public long Id { get; set; }

    [MaxLength(32)]
    public string Mode { get; set; } = LegacyImportModes.Scan;

    [MaxLength(32)]
    public string Status { get; set; } = LegacyImportRunStatuses.Scanning;

    [MaxLength(256)]
    public string SourceLabel { get; set; } = "Legacy KCAS database";

    [MaxLength(64)]
    public string SourceSnapshotSha256 { get; set; } = string.Empty;

    [MaxLength(260)]
    public string? SourceSnapshotFileName { get; set; }

    public long? ApprovedScanRunId { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public int NewCount { get; set; }

    public int UnchangedCount { get; set; }

    public int ChangedCount { get; set; }

    public int MissingCount { get; set; }

    public int InvalidCount { get; set; }

    public int OrphanedCount { get; set; }

    public int AppliedCount { get; set; }

    public int FailedCount { get; set; }

    public string? ErrorSummary { get; set; }

    public ICollection<LegacyImportRowState> Rows { get; } = [];
}

public static class LegacyImportModes
{
    public const string Scan = "Scan";
    public const string ApplyNew = "ApplyNew";
}

public static class LegacyImportRunStatuses
{
    public const string Scanning = "Scanning";
    public const string AwaitingReview = "AwaitingReview";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}
