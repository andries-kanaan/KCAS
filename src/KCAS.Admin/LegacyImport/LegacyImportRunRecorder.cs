using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.LegacyImport;

public sealed class LegacyImportRunRecorder
{
    private readonly ApplicationDbContext db;
    private readonly Dictionary<(string Table, long Id), LegacySourceSnapshot> snapshots;

    private LegacyImportRunRecorder(
        ApplicationDbContext db,
        LegacyImportRun run,
        Dictionary<(string Table, long Id), LegacySourceSnapshot> snapshots)
    {
        this.db = db;
        Run = run;
        this.snapshots = snapshots;
    }

    public LegacyImportRun Run { get; }

    public static async Task<LegacyImportRunRecorder> StartAsync(
        ApplicationDbContext db,
        string mode,
        string sourceLabel,
        string sourceSnapshotSha256,
        string? sourceSnapshotFileName = null,
        long? approvedScanRunId = null,
        CancellationToken cancellationToken = default)
    {
        var run = new LegacyImportRun
        {
            Mode = mode,
            SourceLabel = sourceLabel,
            SourceSnapshotSha256 = sourceSnapshotSha256,
            SourceSnapshotFileName = sourceSnapshotFileName,
            ApprovedScanRunId = approvedScanRunId,
            Status = LegacyImportRunStatuses.Scanning,
            StartedAtUtc = DateTime.UtcNow
        };
        db.LegacyImportRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        var snapshots = await db.LegacySourceSnapshots
            .ToDictionaryAsync(snapshot => (snapshot.SourceTable, snapshot.SourceId), cancellationToken);
        return new LegacyImportRunRecorder(db, run, snapshots);
    }

    public LegacyImportRowState Stage(
        string sourceTable,
        long sourceId,
        string incomingPayloadJson,
        string? existingBaselinePayloadJson,
        string? targetEntityType,
        long? targetEntityId,
        DateTime? sourceUpdatedAt = null,
        bool appliedNew = false)
    {
        snapshots.TryGetValue((sourceTable, sourceId), out var acceptedSnapshot);
        var baseline = acceptedSnapshot?.PayloadJson ?? existingBaselinePayloadJson;
        // A matching KCAS entity without a retained source snapshot is existing
        // operational data, not a new import candidate.  Compare it as an
        // unresolved baseline so it must be reviewed and can never be added again.
        if (baseline is null && targetEntityId.HasValue)
        {
            baseline = "{}";
        }
        var row = LegacyImportReconciler.Compare(
            Run.Id,
            sourceTable,
            sourceId,
            incomingPayloadJson,
            baseline,
            targetEntityType,
            targetEntityId,
            sourceUpdatedAt);

        if (appliedNew && row.Classification == LegacyImportClassifications.New)
        {
            row.ApplyStatus = LegacyImportApplyStatuses.Applied;
            AcceptSnapshot(sourceTable, sourceId, row.IncomingPayloadJson, row.IncomingFingerprint);
        }
        else if (row.Classification == LegacyImportClassifications.Unchanged)
        {
            if (acceptedSnapshot is null)
            {
                AcceptSnapshot(sourceTable, sourceId, row.IncomingPayloadJson, row.IncomingFingerprint);
            }
            else
            {
                acceptedSnapshot.LastSeenAtUtc = DateTime.UtcNow;
            }
        }

        Run.Rows.Add(row);
        return row;
    }

    public LegacyImportRowState StageMissing(
        string sourceTable,
        long sourceId,
        string baselinePayloadJson,
        string? targetEntityType,
        long? targetEntityId)
    {
        var row = LegacyImportReconciler.Missing(
            Run.Id,
            sourceTable,
            sourceId,
            baselinePayloadJson,
            targetEntityType,
            targetEntityId);
        Run.Rows.Add(row);
        return row;
    }

    public LegacyImportRowState StageIssue(
        string sourceTable,
        long sourceId,
        string incomingPayloadJson,
        string classification,
        string error)
    {
        if (classification is not (LegacyImportClassifications.Invalid or LegacyImportClassifications.Orphaned))
        {
            throw new ArgumentOutOfRangeException(nameof(classification));
        }

        var canonical = LegacyImportReconciler.CanonicalizePayload(incomingPayloadJson);
        var row = new LegacyImportRowState
        {
            LegacyImportRunId = Run.Id,
            SourceTable = sourceTable,
            SourceId = sourceId,
            Classification = classification,
            ApplyStatus = LegacyImportApplyStatuses.PendingReview,
            IncomingPayloadJson = canonical,
            IncomingFingerprint = LegacyImportReconciler.Fingerprint(canonical),
            Error = error
        };
        Run.Rows.Add(row);
        return row;
    }

    public async Task CompleteAsync(int failedCount, string? errorSummary = null, CancellationToken cancellationToken = default)
    {
        Run.NewCount = Run.Rows.Count(row => row.Classification == LegacyImportClassifications.New);
        Run.UnchangedCount = Run.Rows.Count(row => row.Classification == LegacyImportClassifications.Unchanged);
        Run.ChangedCount = Run.Rows.Count(row => row.Classification == LegacyImportClassifications.Changed);
        Run.MissingCount = Run.Rows.Count(row => row.Classification == LegacyImportClassifications.MissingFromSource);
        Run.InvalidCount = Run.Rows.Count(row => row.Classification == LegacyImportClassifications.Invalid);
        Run.OrphanedCount = Run.Rows.Count(row => row.Classification == LegacyImportClassifications.Orphaned);
        Run.AppliedCount = Run.Rows.Count(row => row.ApplyStatus == LegacyImportApplyStatuses.Applied);
        Run.FailedCount = failedCount;
        Run.ErrorSummary = errorSummary;
        Run.CompletedAtUtc = DateTime.UtcNow;
        Run.Status = failedCount > 0
            ? LegacyImportRunStatuses.Failed
            : Run.ChangedCount + Run.MissingCount + Run.InvalidCount + Run.OrphanedCount > 0
                ? LegacyImportRunStatuses.AwaitingReview
                : LegacyImportRunStatuses.Completed;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(string errorSummary, CancellationToken cancellationToken = default)
    {
        db.ChangeTracker.Clear();
        var run = await db.LegacyImportRuns.SingleAsync(item => item.Id == Run.Id, cancellationToken);
        run.Status = LegacyImportRunStatuses.Failed;
        run.FailedCount = Math.Max(1, run.FailedCount);
        run.ErrorSummary = errorSummary;
        run.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private void AcceptSnapshot(string sourceTable, long sourceId, string payloadJson, string fingerprint)
    {
        var snapshot = new LegacySourceSnapshot
        {
            SourceTable = sourceTable,
            SourceId = sourceId,
            PayloadJson = payloadJson,
            Fingerprint = fingerprint,
            AcceptedAtUtc = DateTime.UtcNow,
            AcceptedFromRunId = Run.Id,
            LastSeenAtUtc = DateTime.UtcNow
        };
        snapshots[(sourceTable, sourceId)] = snapshot;
        db.LegacySourceSnapshots.Add(snapshot);
    }
}
