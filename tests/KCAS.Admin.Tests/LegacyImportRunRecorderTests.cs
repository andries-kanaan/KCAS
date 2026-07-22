using KCAS.Admin.Data;
using KCAS.Admin.LegacyImport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class LegacyImportRunRecorderTests(KcasWebApplicationFactory factory)
{
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
}
