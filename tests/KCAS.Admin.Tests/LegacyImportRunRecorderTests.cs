using KCAS.Admin.Data;
using KCAS.Admin.LegacyImport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class LegacyImportRunRecorderTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Delete_import_history_clears_runs_rows_differences_and_source_snapshots()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await LegacyImportWebService.DeleteImportHistoryAsync(db, CancellationToken.None);

        var run = new LegacyImportRun
        {
            Mode = LegacyImportModes.Scan,
            Status = LegacyImportRunStatuses.AwaitingReview,
            SourceLabel = "seed",
            SourceSnapshotSha256 = new string('b', 64),
            StartedAtUtc = DateTime.UtcNow
        };
        var row = new LegacyImportRowState
        {
            LegacyImportRun = run,
            SourceTable = "tbl_client",
            SourceId = 123,
            Classification = LegacyImportClassifications.Changed,
            ApplyStatus = LegacyImportApplyStatuses.PendingReview,
            IncomingFingerprint = new string('c', 64),
            IncomingPayloadJson = "{\"id\":\"123\",\"name\":\"Incoming\"}",
            BaselineFingerprint = new string('d', 64),
            BaselinePayloadJson = "{\"id\":\"123\",\"name\":\"Baseline\"}"
        };
        row.Differences.Add(new LegacyImportDifference
        {
            FieldName = "name",
            BaselineValue = "Baseline",
            IncomingValue = "Incoming"
        });
        db.LegacyImportRuns.Add(run);
        db.LegacyImportRowStates.Add(row);
        db.LegacySourceSnapshots.Add(new LegacySourceSnapshot
        {
            SourceTable = "tbl_client",
            SourceId = 123,
            Fingerprint = new string('d', 64),
            PayloadJson = "{\"id\":\"123\",\"name\":\"Baseline\"}",
            AcceptedAtUtc = DateTime.UtcNow,
            LastSeenAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await LegacyImportWebService.DeleteImportHistoryAsync(db, CancellationToken.None);

        Assert.Equal(0, await db.LegacyImportRuns.CountAsync());
        Assert.Equal(0, await db.LegacyImportRowStates.CountAsync());
        Assert.Equal(0, await db.LegacyImportDifferences.CountAsync());
        Assert.Equal(0, await db.LegacySourceSnapshots.CountAsync());
    }

    [Fact]
    public async Task Existing_target_without_a_source_snapshot_is_changed_not_new()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var recorder = await LegacyImportRunRecorder.StartAsync(db, LegacyImportModes.ApplyNew, "test-source", new string('a', 64));

        var row = recorder.Stage(
            "tbl_existing",
            500,
            "{\"id\":\"500\",\"name\":\"Existing\"}",
            existingBaselinePayloadJson: null,
            targetEntityType: "Client",
            targetEntityId: 42,
            appliedNew: true);
        await recorder.CompleteAsync(0);

        Assert.Equal(LegacyImportClassifications.Changed, row.Classification);
        Assert.Equal(LegacyImportApplyStatuses.PendingReview, row.ApplyStatus);
        Assert.Equal(0, recorder.Run.NewCount);
        Assert.Equal(1, recorder.Run.ChangedCount);
        Assert.Equal(0, recorder.Run.AppliedCount);
    }

    [Fact]
    public async Task Review_actions_record_reasoned_decisions()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<LegacyImportWebService>();
        await LegacyImportWebService.DeleteImportHistoryAsync(db, CancellationToken.None);

        var deferRow = await SeedReviewRowAsync(db, 8001, LegacyImportClassifications.Changed, withDifference: true);
        var rejectRow = await SeedReviewRowAsync(db, 8002, LegacyImportClassifications.MissingFromSource, withDifference: false);
        var manualRow = await SeedReviewRowAsync(db, 8003, LegacyImportClassifications.Changed, withDifference: true);

        await service.DeferReviewAsync(deferRow.Id, "reviewer@example.test", "Need adviser confirmation.");
        await service.RejectReviewAsync(rejectRow.Id, "reviewer@example.test", "Source deletion is not accepted.");
        await service.RecordManualResolutionAsync(manualRow.Id, "reviewer@example.test", "Corrected manually in KCAS.", "Resolved from client file.");
        db.ChangeTracker.Clear();

        var deferred = await db.LegacyImportRowStates.Include(row => row.Differences).SingleAsync(row => row.Id == deferRow.Id);
        Assert.Equal(LegacyImportApplyStatuses.PendingReview, deferred.ApplyStatus);
        Assert.All(deferred.Differences, difference =>
        {
            Assert.Equal(LegacyImportDecisionStatuses.Deferred, difference.Decision);
            Assert.Equal("Need adviser confirmation.", difference.ReviewReason);
            Assert.Equal("reviewer@example.test", difference.ReviewedBy);
            Assert.NotNull(difference.ReviewedAtUtc);
        });

        var rejected = await db.LegacyImportRowStates.Include(row => row.Differences).SingleAsync(row => row.Id == rejectRow.Id);
        Assert.Equal(LegacyImportApplyStatuses.NotApplicable, rejected.ApplyStatus);
        var rowDecision = Assert.Single(rejected.Differences);
        Assert.Equal("__row__", rowDecision.FieldName);
        Assert.Equal(LegacyImportDecisionStatuses.Rejected, rowDecision.Decision);
        Assert.Equal("Source deletion is not accepted.", rowDecision.ReviewReason);

        var manual = await db.LegacyImportRowStates.Include(row => row.Differences).SingleAsync(row => row.Id == manualRow.Id);
        Assert.Equal(LegacyImportApplyStatuses.NotApplicable, manual.ApplyStatus);
        Assert.All(manual.Differences, difference =>
        {
            Assert.Equal(LegacyImportDecisionStatuses.Corrected, difference.Decision);
            Assert.Equal("Corrected manually in KCAS.", difference.ResolvedValue);
            Assert.Equal("Resolved from client file.", difference.ReviewReason);
        });
    }

    [Fact]
    public async Task Accepted_new_source_becomes_the_idempotent_baseline_and_changes_remain_pending()
    {
        const string initial = "{\"id\":\"7001\",\"name\":\"Initial\"}";
        const string changed = "{\"id\":\"7001\",\"name\":\"Changed\"}";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var recorder = await LegacyImportRunRecorder.StartAsync(db, LegacyImportModes.ApplyNew, "test-source", new string('a', 64));
            var row = recorder.Stage("tbl_test", 7001, initial, null, "Test", null, appliedNew: true);
            await recorder.CompleteAsync(0);

            Assert.Equal(LegacyImportClassifications.New, row.Classification);
            Assert.Equal(LegacyImportApplyStatuses.Applied, row.ApplyStatus);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var recorder = await LegacyImportRunRecorder.StartAsync(db, LegacyImportModes.Scan, "test-source", new string('a', 64));
            var row = recorder.Stage("tbl_test", 7001, initial, null, "Test", null);
            await recorder.CompleteAsync(0);

            Assert.Equal(LegacyImportClassifications.Unchanged, row.Classification);
            Assert.Equal(LegacyImportRunStatuses.Completed, recorder.Run.Status);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var recorder = await LegacyImportRunRecorder.StartAsync(db, LegacyImportModes.Scan, "test-source", new string('a', 64));
            var row = recorder.Stage("tbl_test", 7001, changed, null, "Test", null);
            await recorder.CompleteAsync(0);

            Assert.Equal(LegacyImportClassifications.Changed, row.Classification);
            Assert.Equal(LegacyImportApplyStatuses.PendingReview, row.ApplyStatus);
            Assert.Equal(LegacyImportRunStatuses.AwaitingReview, recorder.Run.Status);
            Assert.Single(row.Differences);
        }

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var snapshot = await verificationDb.LegacySourceSnapshots.AsNoTracking()
            .SingleAsync(item => item.SourceTable == "tbl_test" && item.SourceId == 7001);
        Assert.Contains("Initial", snapshot.PayloadJson);
        Assert.DoesNotContain("Changed", snapshot.PayloadJson);
    }

    private static async Task<LegacyImportRowState> SeedReviewRowAsync(
        ApplicationDbContext db,
        long sourceId,
        string classification,
        bool withDifference)
    {
        var run = new LegacyImportRun
        {
            Mode = LegacyImportModes.Scan,
            Status = LegacyImportRunStatuses.AwaitingReview,
            SourceLabel = "test-source",
            SourceSnapshotSha256 = new string('e', 64),
            StartedAtUtc = DateTime.UtcNow
        };
        var row = new LegacyImportRowState
        {
            LegacyImportRun = run,
            SourceTable = "tbl_client",
            SourceId = sourceId,
            Classification = classification,
            ApplyStatus = LegacyImportApplyStatuses.PendingReview,
            IncomingFingerprint = new string('f', 64),
            IncomingPayloadJson = $"{{\"id\":\"{sourceId}\",\"name\":\"Incoming\"}}",
            BaselineFingerprint = new string('0', 64),
            BaselinePayloadJson = $"{{\"id\":\"{sourceId}\",\"name\":\"Baseline\"}}"
        };
        if (withDifference)
        {
            row.Differences.Add(new LegacyImportDifference
            {
                FieldName = "name",
                BaselineValue = "Baseline",
                IncomingValue = "Incoming"
            });
        }

        db.LegacyImportRowStates.Add(row);
        await db.SaveChangesAsync();
        return row;
    }
}
