using KCAS.Admin.Data;
using KCAS.Admin.LegacyImport;

namespace KCAS.Admin.Tests;

public sealed class LegacyImportApprovalValidatorTests
{
    [Fact]
    public void Returns_only_new_rows_from_matching_completed_scan()
    {
        var run = Scan("stage", new string('a', 64));
        run.Rows.Add(Row("tbl_client", 1, LegacyImportClassifications.New, "one"));
        run.Rows.Add(Row("tbl_client", 2, LegacyImportClassifications.Changed, "two"));
        run.Rows.Add(Row("tbl_fund", 3, LegacyImportClassifications.New, "unstable-id"));
        run.Rows.Add(Row("tbl_kyc", 4, LegacyImportClassifications.New, "replacement-policy"));

        var approved = LegacyImportApprovalValidator.GetApprovedNewRows(run, "stage", new string('A', 64));

        var item = Assert.Single(approved);
        Assert.Equal(("tbl_client", 1L), item.Key);
        Assert.Equal("one", item.Value);
    }

    [Fact]
    public void Rejects_snapshot_or_database_mismatch()
    {
        var run = Scan("stage", new string('a', 64));

        Assert.Throws<InvalidOperationException>(() => LegacyImportApprovalValidator.GetApprovedNewRows(run, "other", new string('a', 64)));
        Assert.Throws<InvalidOperationException>(() => LegacyImportApprovalValidator.GetApprovedNewRows(run, "stage", new string('b', 64)));
    }

    [Fact]
    public void Rejects_incomplete_or_non_scan_run()
    {
        var run = Scan("stage", new string('a', 64));
        run.Mode = LegacyImportModes.ApplyNew;

        Assert.Throws<InvalidOperationException>(() => LegacyImportApprovalValidator.GetApprovedNewRows(run, "stage", new string('a', 64)));
    }

    private static LegacyImportRun Scan(string source, string hash) => new()
    {
        Id = 7,
        Mode = LegacyImportModes.Scan,
        Status = LegacyImportRunStatuses.AwaitingReview,
        CompletedAtUtc = DateTime.UtcNow,
        SourceLabel = source,
        SourceSnapshotSha256 = hash
    };

    private static LegacyImportRowState Row(string table, long id, string classification, string fingerprint) => new()
    {
        SourceTable = table,
        SourceId = id,
        Classification = classification,
        IncomingFingerprint = fingerprint
    };
}
