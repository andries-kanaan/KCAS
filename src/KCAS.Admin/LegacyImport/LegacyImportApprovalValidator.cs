using KCAS.Admin.Data;

namespace KCAS.Admin.LegacyImport;

public static class LegacyImportApprovalValidator
{
    public static readonly IReadOnlySet<string> ReviewOnlySourceTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "tbl_fund",
        "tbl_kyc"
    };

    public static Dictionary<(string Table, long Id), string> GetApprovedNewRows(
        LegacyImportRun approvedScan,
        string sourceLabel,
        string sourceSnapshotSha256)
    {
        if (approvedScan.Mode != LegacyImportModes.Scan || approvedScan.CompletedAtUtc is null ||
            approvedScan.Status is not (LegacyImportRunStatuses.Completed or LegacyImportRunStatuses.AwaitingReview))
        {
            throw new InvalidOperationException($"Approved scan run '{approvedScan.Id}' is not a completed scan.");
        }
        if (!string.Equals(approvedScan.SourceSnapshotSha256, sourceSnapshotSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(approvedScan.SourceLabel, sourceLabel, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Approved scan provenance does not match the staged source database and snapshot SHA-256.");
        }

        return approvedScan.Rows
            .Where(row => row.Classification == LegacyImportClassifications.New && !ReviewOnlySourceTables.Contains(row.SourceTable))
            .ToDictionary(row => (row.SourceTable, row.SourceId), row => row.IncomingFingerprint);
    }
}
