using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class GoAmlTransferServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Completed_checks_export_preview_apply_once_with_evidence_and_action_task()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var daily = scope.ServiceProvider.GetRequiredService<GoAmlDailyCheckService>();
        var transfers = scope.ServiceProvider.GetRequiredService<GoAmlTransferService>();
        var root = Path.Combine(Path.GetTempPath(), $"kcas-goaml-transfer-{Guid.NewGuid():N}");
        string? outgoingPath = null;
        string? incomingPath = null;
        try
        {
            await daily.SaveSettingsAsync(new GoAmlSettingsModel
            {
                EvidenceRootPath = root,
                PortalUrl = GoAmlDefaults.PortalUrl,
                TrackingStartDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-90),
                DueHourLocal = 10
            }, "checker@example.test", "Configure goAML transfer test evidence.");

            var firstDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-62);
            var secondDate = firstDate.AddDays(1);
            await RecordCheckAsync(daily, firstDate, GoAmlCheckStatuses.NoNewMessages,
                "No messages.", null, null);
            await RecordCheckAsync(daily, secondDate, GoAmlCheckStatuses.ActionRequired,
                "Investigate the message.", "FIC-TRANSFER-001", secondDate.AddDays(2));

            var detectedRange = await transfers.LoadExportRangeAsync();
            Assert.NotNull(detectedRange);
            Assert.True(detectedRange.FirstCheckDate <= firstDate);
            Assert.True(detectedRange.LastCheckDate >= secondDate);

            const string passphrase = "goaml-transfer-test-passphrase";
            var exported = await transfers.ExportAsync(firstDate, secondDate, passphrase,
                "checker@example.test", "Transfer laptop checks to live.");
            outgoingPath = exported.StoragePath;
            Assert.Matches(
                @"^KCAS-goAML-\d{8}-\d{8}-[a-f0-9]{12}\.kcas-goaml$",
                exported.FileName);
            Assert.Equal(2, exported.CheckCount);
            Assert.Equal(2, exported.EvidenceCount);
            var encrypted = await File.ReadAllBytesAsync(exported.StoragePath);
            await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() =>
                transfers.PreviewAsync(encrypted, "wrong-goaml-passphrase"));

            var sourceChecks = await db.GoAmlDailyChecks
                .Where(item => item.CheckDate == firstDate || item.CheckDate == secondDate)
                .ToListAsync();
            var sourceTaskIds = sourceChecks.Where(item => item.ComplianceTaskId.HasValue)
                .Select(item => item.ComplianceTaskId!.Value).ToList();
            db.GoAmlDailyChecks.RemoveRange(sourceChecks);
            await db.SaveChangesAsync();
            if (sourceTaskIds.Count > 0)
            {
                db.ComplianceTasks.RemoveRange(await db.ComplianceTasks
                    .Where(item => sourceTaskIds.Contains(item.Id)).ToListAsync());
                await db.SaveChangesAsync();
            }
            db.ChangeTracker.Clear();

            var preview = await transfers.PreviewAsync(encrypted, passphrase);
            Assert.True(preview.CanApply);
            Assert.Equal(2, preview.NewCheckCount);
            Assert.Equal(0, preview.ExistingCheckCount);
            Assert.Equal(2, preview.EvidenceCount);

            var imported = await transfers.ApplyAsync(encrypted, passphrase,
                "live-importer@example.test", "Approved laptop goAML transfer.");
            incomingPath = imported.StoragePath;
            Assert.Equal(2, imported.ChecksImported);
            Assert.Equal(2, imported.EvidenceImported);
            Assert.Equal(1, imported.TasksCreated);

            var restored = await db.GoAmlDailyChecks.AsNoTracking()
                .Where(item => item.CheckDate == firstDate || item.CheckDate == secondDate)
                .OrderBy(item => item.CheckDate)
                .ToListAsync();
            Assert.Equal(2, restored.Count);
            Assert.All(restored, item =>
            {
                Assert.NotNull(item.EvidencePath);
                Assert.True(File.Exists(item.EvidencePath));
                Assert.Equal(64, item.EvidenceSha256?.Length);
            });
            Assert.NotNull(restored[1].ComplianceTaskId);
            Assert.Contains(await db.ComplianceAuditEvents.AsNoTracking().ToListAsync(), item =>
                item.Action == "GoAmlPackageApplied" && item.UserName == "live-importer@example.test");

            var duplicate = await transfers.PreviewAsync(encrypted, passphrase);
            Assert.True(duplicate.AlreadyApplied);
            Assert.False(duplicate.CanApply);
            await Assert.ThrowsAsync<InvalidOperationException>(() => transfers.ApplyAsync(
                encrypted, passphrase, "live-importer@example.test", "Attempt duplicate."));
        }
        finally
        {
            DeleteFile(outgoingPath);
            DeleteFile(incomingPath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Preview_blocks_a_different_live_check_for_the_same_date()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var daily = scope.ServiceProvider.GetRequiredService<GoAmlDailyCheckService>();
        var transfers = scope.ServiceProvider.GetRequiredService<GoAmlTransferService>();
        var root = Path.Combine(Path.GetTempPath(), $"kcas-goaml-conflict-{Guid.NewGuid():N}");
        string? outgoingPath = null;
        try
        {
            await daily.SaveSettingsAsync(new GoAmlSettingsModel
            {
                EvidenceRootPath = root,
                PortalUrl = GoAmlDefaults.PortalUrl,
                TrackingStartDate = DateOnly.FromDateTime(DateTime.Today).AddDays(-120),
                DueHourLocal = 10
            }, "checker@example.test", "Configure goAML conflict test evidence.");
            var date = DateOnly.FromDateTime(DateTime.Today).AddDays(-93);
            await RecordCheckAsync(daily, date, GoAmlCheckStatuses.NoNewMessages,
                "Original notes.", null, null);
            const string passphrase = "goaml-conflict-test-passphrase";
            var exported = await transfers.ExportAsync(date, date, passphrase,
                "checker@example.test", "Create conflict test package.");
            outgoingPath = exported.StoragePath;
            var encrypted = await File.ReadAllBytesAsync(exported.StoragePath);

            var identicalPreview = await transfers.PreviewAsync(encrypted, passphrase);
            Assert.True(identicalPreview.CanApply);
            Assert.Equal(1, identicalPreview.ExistingCheckCount);
            Assert.Equal(0, identicalPreview.NewCheckCount);

            var live = await db.GoAmlDailyChecks.SingleAsync(item => item.CheckDate == date);
            live.Notes = "Different live notes.";
            await db.SaveChangesAsync();

            var preview = await transfers.PreviewAsync(encrypted, passphrase);
            Assert.False(preview.CanApply);
            Assert.Contains(preview.Conflicts, item =>
                item.Contains("never overwritten", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteFile(outgoingPath);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static async Task RecordCheckAsync(GoAmlDailyCheckService daily, DateOnly date,
        string status, string notes, string? reference, DateOnly? dueDate)
    {
        var localNow = date.ToDateTime(new TimeOnly(9, 0));
        await daily.StartTodayAsync("checker@example.test", localNow);
        await using var evidence = new MemoryStream([0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, 0x4a, 0x46, 0x49, 0x46, 0xff, 0xd9]);
        await daily.CompleteTodayAsync(new GoAmlCompletionModel
        {
            Status = status,
            Notes = notes,
            MessageReference = reference,
            ActionOwner = reference is null ? null : "owner@example.test",
            ActionDueDate = dueDate
        }, evidence, "image/jpeg", "checker@example.test", "Record transfer test check.", localNow);
    }

    private static void DeleteFile(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
    }
}
