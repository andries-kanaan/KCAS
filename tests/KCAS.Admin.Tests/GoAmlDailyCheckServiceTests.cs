using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class GoAmlDailyCheckServiceTests(KcasWebApplicationFactory factory)
{
    private static readonly byte[] JpegEvidence = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////2wBDAf//////////////////////////////////////////////////////////////////////////////////////wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAf/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIQAxAAAAF//8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABBQJ//8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAwEBPwF//8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAgEBPwF//8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQAGPwJ//8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPyF//9oADAMBAAIAAwAAABAf/8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAwEBPxB//8QAFBEBAAAAAAAAAAAAAAAAAAAAAP/aAAgBAgEBPxB//8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxB//9k=");

    [Fact]
    public async Task Completed_check_saves_configured_compressed_evidence_and_audit()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GoAmlDailyCheckService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var date = new DateOnly(2040, 1, 15);
        var root = Path.Combine(Path.GetTempPath(), $"kcas-goaml-{Guid.NewGuid():N}");
        try
        {
            await service.SaveSettingsAsync(new GoAmlSettingsModel
            {
                EvidenceRootPath = root,
                PortalUrl = GoAmlDefaults.PortalUrl,
                TrackingStartDate = date,
                DueHourLocal = 10,
                BackupChecker = "backup@example.test"
            }, "checker@example.test", "Configure isolated test evidence.");

            var check = await service.StartTodayAsync("checker@example.test", date.ToDateTime(new TimeOnly(9, 0)));
            await using var image = new MemoryStream(JpegEvidence);
            await service.CompleteTodayAsync(new GoAmlCompletionModel
            {
                Status = GoAmlCheckStatuses.NoNewMessages,
                Notes = "Inbox reviewed; no new or actionable messages."
            }, image, "image/jpeg", "checker@example.test", "Complete daily review.",
                date.ToDateTime(new TimeOnly(9, 5)));

            var saved = await db.GoAmlDailyChecks.AsNoTracking().SingleAsync(item => item.Id == check.Id);
            Assert.Equal(GoAmlCheckStatuses.NoNewMessages, saved.Status);
            Assert.True(File.Exists(saved.EvidencePath));
            Assert.Contains(Path.Combine("2040", "01"), saved.EvidencePath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(JpegEvidence)).ToLowerInvariant(), saved.EvidenceSha256);
            Assert.True(await db.ComplianceAuditEvents.AnyAsync(item =>
                item.EntityType == nameof(GoAmlDailyCheck) && item.EntityId == check.Id && item.Action == "Completed"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Action_required_creates_high_priority_work_item_and_completed_check_is_immutable()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GoAmlDailyCheckService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var date = new DateOnly(2041, 2, 16);
        var root = Path.Combine(Path.GetTempPath(), $"kcas-goaml-{Guid.NewGuid():N}");
        try
        {
            await service.SaveSettingsAsync(new GoAmlSettingsModel
            {
                EvidenceRootPath = root,
                PortalUrl = GoAmlDefaults.PortalUrl,
                TrackingStartDate = date,
                DueHourLocal = 10
            }, "checker@example.test", "Configure isolated test evidence.");
            var check = await service.StartTodayAsync("checker@example.test", date.ToDateTime(new TimeOnly(8, 0)));
            await using var image = new MemoryStream(JpegEvidence);
            var completion = new GoAmlCompletionModel
            {
                Status = GoAmlCheckStatuses.ActionRequired,
                MessageReference = "FIC request 123",
                ActionOwner = "mlco@example.test",
                ActionDueDate = date.AddDays(1),
                Notes = "Review and respond."
            };
            await service.CompleteTodayAsync(completion, image, "image/jpeg", "checker@example.test",
                "Escalate new message.", date.ToDateTime(new TimeOnly(8, 5)));

            var saved = await db.GoAmlDailyChecks.AsNoTracking().SingleAsync(item => item.Id == check.Id);
            var task = await db.ComplianceTasks.AsNoTracking().SingleAsync(item => item.Id == saved.ComplianceTaskId);
            Assert.Equal("High", task.Priority);
            Assert.Equal(ComplianceWorkStatuses.Open, task.Status);
            Assert.Equal(nameof(GoAmlDailyCheck), task.LinkedEntityType);
            await using var duplicateImage = new MemoryStream(JpegEvidence);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteTodayAsync(
                completion, duplicateImage, "image/jpeg", "checker@example.test", "Try duplicate completion.",
                date.ToDateTime(new TimeOnly(8, 10))));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Dashboard_identifies_each_missing_required_day_after_due_hour()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GoAmlDailyCheckService>();
        var date = new DateOnly(2042, 3, 20);
        var root = Path.Combine(Path.GetTempPath(), $"kcas-goaml-{Guid.NewGuid():N}");
        try
        {
            await service.SaveSettingsAsync(new GoAmlSettingsModel
            {
                EvidenceRootPath = root,
                PortalUrl = GoAmlDefaults.PortalUrl,
                TrackingStartDate = date.AddDays(-2),
                DueHourLocal = 10
            }, "checker@example.test", "Configure overdue test.");

            var dashboard = await service.LoadDashboardAsync(date.ToDateTime(new TimeOnly(11, 0)));

            Assert.Equal([date.AddDays(-2), date.AddDays(-1), date], dashboard.MissingDates);
            Assert.True(dashboard.IsOverdue);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Successful_check_requires_a_JPEG_screenshot()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GoAmlDailyCheckService>();
        var date = new DateOnly(2043, 4, 21);
        await service.StartTodayAsync("checker@example.test", date.ToDateTime(new TimeOnly(9, 0)));

        await Assert.ThrowsAsync<ValidationException>(() => service.CompleteTodayAsync(
            new GoAmlCompletionModel { Status = GoAmlCheckStatuses.NoNewMessages },
            null, null, "checker@example.test", "Attempt without evidence.",
            date.ToDateTime(new TimeOnly(9, 5))));
    }

    [Fact]
    public async Task Unavailable_check_accepts_a_screenshot_without_notes()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GoAmlDailyCheckService>();
        var date = new DateOnly(2044, 5, 22);
        var root = Path.Combine(Path.GetTempPath(), $"kcas-goaml-{Guid.NewGuid():N}");
        try
        {
            await service.SaveSettingsAsync(new GoAmlSettingsModel
            {
                EvidenceRootPath = root,
                PortalUrl = GoAmlDefaults.PortalUrl,
                TrackingStartDate = date,
                DueHourLocal = 10
            }, "checker@example.test", "Configure unavailable-check test evidence.");
            var check = await service.StartTodayAsync("checker@example.test", date.ToDateTime(new TimeOnly(9, 0)));
            await using var image = new MemoryStream(JpegEvidence);

            await service.CompleteTodayAsync(new GoAmlCompletionModel
            {
                Status = GoAmlCheckStatuses.Unavailable,
                Notes = null
            }, image, "image/jpeg", "checker@example.test", "Record unavailable goAML evidence.",
                date.ToDateTime(new TimeOnly(9, 5)));

            using var verificationScope = factory.Services.CreateScope();
            var verificationDb = verificationScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await verificationDb.GoAmlDailyChecks.AsNoTracking().SingleAsync(item => item.Id == check.Id);
            Assert.Equal(GoAmlCheckStatuses.Unavailable, saved.Status);
            Assert.Null(saved.Notes);
            Assert.NotNull(saved.EvidencePath);
            var dashboard = await service.LoadDashboardAsync(date.ToDateTime(new TimeOnly(11, 0)));
            Assert.True(dashboard.IsTodayComplete);
            Assert.True(dashboard.IsTodayUnavailable);
            Assert.False(dashboard.IsOverdue);
            Assert.DoesNotContain(date, dashboard.MissingDates);
            var repeatedStart = await service.StartTodayAsync("checker@example.test", date.ToDateTime(new TimeOnly(15, 0)));
            Assert.Equal(check.Id, repeatedStart.Id);
            Assert.Equal(GoAmlCheckStatuses.Unavailable, repeatedStart.Status);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
