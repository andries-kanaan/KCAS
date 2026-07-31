using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class GoAmlDailyCheckService(ApplicationDbContext db)
{
    private const long MaxEvidenceBytes = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions AuditOptions = new(JsonSerializerDefaults.Web);

    public async Task<GoAmlDashboardModel> LoadDashboardAsync(DateTime? localNow = null)
    {
        var now = localNow ?? DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var settings = await LoadSettingsAsync(today);
        var recent = await db.GoAmlDailyChecks.AsNoTracking()
            .OrderByDescending(item => item.CheckDate)
            .ThenByDescending(item => item.StartedAtUtc)
            .Take(31)
            .ToListAsync();
        var todayCheck = recent.FirstOrDefault(item => item.CheckDate == today);
        var requiredThrough = now.Hour >= settings.DueHourLocal ? today : today.AddDays(-1);
        var recordedDates = recent
            .Where(item => item.Status != GoAmlCheckStatuses.Started)
            .Select(item => item.CheckDate)
            .ToHashSet();
        var windowStart = settings.TrackingStartDate > today.AddDays(-30)
            ? settings.TrackingStartDate
            : today.AddDays(-30);
        var missingDates = new List<DateOnly>();
        for (var date = windowStart; date <= requiredThrough; date = date.AddDays(1))
        {
            if (!recordedDates.Contains(date)) missingDates.Add(date);
        }

        return new GoAmlDashboardModel(settings, todayCheck, recent, missingDates);
    }

    public async Task<GoAmlSettingsModel> SaveSettingsAsync(GoAmlSettingsModel model, string? userName, string reason)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        var root = ValidateEvidenceRoot(model.EvidenceRootPath);
        if (!Uri.TryCreate(model.PortalUrl?.Trim(), UriKind.Absolute, out var portal) || portal.Scheme != Uri.UriSchemeHttps)
        {
            throw new ValidationException("The goAML portal must be a valid HTTPS URL.");
        }
        if (model.DueHourLocal is < 0 or > 23)
        {
            throw new ValidationException("The daily due hour must be between 0 and 23.");
        }

        Directory.CreateDirectory(root);
        var settings = await db.GoAmlSettings.OrderBy(item => item.Id).FirstOrDefaultAsync();
        var oldJson = settings is null ? null : JsonSerializer.Serialize(SettingsSummary(settings), AuditOptions);
        if (settings is null)
        {
            settings = new GoAmlSettings();
            db.GoAmlSettings.Add(settings);
        }
        settings.EvidenceRootPath = root;
        settings.PortalUrl = portal.ToString();
        settings.TrackingStartDate = model.TrackingStartDate;
        settings.DueHourLocal = model.DueHourLocal;
        settings.BackupChecker = NormalizeOrNull(model.BackupChecker);
        settings.UpdatedAtUtc = DateTime.UtcNow;
        settings.UpdatedBy = user;
        await db.SaveChangesAsync();
        db.ComplianceAuditEvents.Add(CreateAudit(nameof(GoAmlSettings), settings.Id,
            oldJson is null ? "Created" : "Updated", user, reason, oldJson, SettingsSummary(settings)));
        await db.SaveChangesAsync();
        return GoAmlSettingsModel.FromEntity(settings);
    }

    public async Task<GoAmlDailyCheck> StartTodayAsync(string? userName, DateTime? localNow = null)
    {
        var user = RequireUser(userName);
        var today = DateOnly.FromDateTime(localNow ?? DateTime.Now);
        var existing = await db.GoAmlDailyChecks.SingleOrDefaultAsync(item => item.CheckDate == today);
        if (existing is not null) return existing;

        var check = new GoAmlDailyCheck
        {
            CheckDate = today,
            Status = GoAmlCheckStatuses.Started,
            StartedAtUtc = DateTime.UtcNow,
            StartedBy = user
        };
        db.GoAmlDailyChecks.Add(check);
        await db.SaveChangesAsync();
        db.ComplianceAuditEvents.Add(CreateAudit(nameof(GoAmlDailyCheck), check.Id, "Started", user,
            "Opened the goAML daily message-board check.", null, CheckSummary(check)));
        await db.SaveChangesAsync();
        return check;
    }

    public async Task CompleteTodayAsync(
        GoAmlCompletionModel model,
        Stream? evidence,
        string? evidenceContentType,
        string? userName,
        string reason,
        DateTime? localNow = null,
        CancellationToken cancellationToken = default)
    {
        RequireReason(reason);
        var user = RequireUser(userName);
        if (!GoAmlCheckStatuses.Completed.Contains(model.Status, StringComparer.Ordinal))
        {
            throw new ValidationException("Select a valid goAML check outcome.");
        }
        var today = DateOnly.FromDateTime(localNow ?? DateTime.Now);
        var check = await db.GoAmlDailyChecks.SingleOrDefaultAsync(item => item.CheckDate == today)
            ?? throw new InvalidOperationException("Start today's goAML check before recording the result.");
        if (check.Status != GoAmlCheckStatuses.Started)
        {
            throw new InvalidOperationException("Today's goAML check has already been completed.");
        }
        if (model.Status != GoAmlCheckStatuses.Unavailable && evidence is null)
        {
            throw new ValidationException("A screenshot is required for a completed goAML message-board check.");
        }
        if (model.Status == GoAmlCheckStatuses.Unavailable && evidence is null && string.IsNullOrWhiteSpace(model.Notes))
        {
            throw new ValidationException("Attach a screenshot or describe the access problem when goAML is unavailable.");
        }
        if (model.Status == GoAmlCheckStatuses.ActionRequired)
        {
            if (string.IsNullOrWhiteSpace(model.MessageReference)) throw new ValidationException("Enter the message subject or reference.");
            if (string.IsNullOrWhiteSpace(model.ActionOwner)) throw new ValidationException("Enter an action owner.");
            if (model.ActionDueDate is null) throw new ValidationException("Enter the action due date.");
        }

        string? createdEvidencePath = null;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (evidence is not null)
            {
                if (!string.Equals(evidenceContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ValidationException("The screenshot must be converted to JPEG before it is saved.");
                }
                var settings = await LoadSettingsAsync(today);
                var folder = Path.Combine(settings.EvidenceRootPath, today.Year.ToString("0000"), today.Month.ToString("00"));
                Directory.CreateDirectory(folder);
                var fileName = $"goAML-{today:yyyy-MM-dd}-{DateTime.Now:HHmmss}-{check.Id}.jpg";
                var path = Path.Combine(folder, fileName);
                createdEvidencePath = path;
                await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await evidence.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaxEvidenceBytes)
                    {
                        output.Close();
                        File.Delete(path);
                        throw new ValidationException("The compressed screenshot cannot exceed 5 MB.");
                    }
                    hasher.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                check.EvidenceFileName = fileName;
                check.EvidencePath = path;
                check.EvidenceContentType = "image/jpeg";
                check.EvidenceSizeBytes = total;
                check.EvidenceSha256 = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            }

            check.Status = model.Status;
            check.CompletedAtUtc = DateTime.UtcNow;
            check.CompletedBy = user;
            check.Notes = NormalizeOrNull(model.Notes);
            check.MessageReference = NormalizeOrNull(model.MessageReference);
            check.ActionOwner = NormalizeOrNull(model.ActionOwner);
            check.ActionDueDate = model.ActionDueDate;

            if (model.Status == GoAmlCheckStatuses.ActionRequired)
            {
                var task = new ComplianceTask
                {
                    TaskType = ComplianceTaskTypes.Remediation,
                    Title = $"goAML message: {check.MessageReference}",
                    Description = check.Notes,
                    Owner = check.ActionOwner,
                    DueDate = check.ActionDueDate,
                    Priority = "High",
                    Status = ComplianceWorkStatuses.Open,
                    LinkedEntityType = nameof(GoAmlDailyCheck),
                    LinkedEntityId = check.Id,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedBy = user
                };
                db.ComplianceTasks.Add(task);
                await db.SaveChangesAsync(cancellationToken);
                check.ComplianceTaskId = task.Id;
                db.ComplianceAuditEvents.Add(CreateAudit(nameof(ComplianceTask), task.Id, "CreatedFromGoAmlMessage", user,
                    reason, null, new { task.Title, task.Owner, task.DueDate, task.Priority, task.Status, GoAmlDailyCheckId = check.Id }));
            }

            db.ComplianceAuditEvents.Add(CreateAudit(nameof(GoAmlDailyCheck), check.Id, "Completed", user,
                reason, null, CheckSummary(check)));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(createdEvidencePath) && File.Exists(createdEvidencePath))
            {
                File.Delete(createdEvidencePath);
            }
            throw;
        }
    }

    public async Task<GoAmlEvidenceFile?> OpenEvidenceAsync(int id, CancellationToken cancellationToken = default)
    {
        var evidence = await db.GoAmlDailyChecks.AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new { item.EvidencePath, item.EvidenceFileName, item.EvidenceContentType })
            .SingleOrDefaultAsync(cancellationToken);
        if (evidence is null || string.IsNullOrWhiteSpace(evidence.EvidencePath) || !File.Exists(evidence.EvidencePath)) return null;
        return new GoAmlEvidenceFile(
            new FileStream(evidence.EvidencePath, FileMode.Open, FileAccess.Read, FileShare.Read),
            evidence.EvidenceContentType ?? "image/jpeg",
            evidence.EvidenceFileName ?? Path.GetFileName(evidence.EvidencePath));
    }

    private async Task<GoAmlSettingsModel> LoadSettingsAsync(DateOnly today)
    {
        var settings = await db.GoAmlSettings.AsNoTracking().OrderBy(item => item.Id).FirstOrDefaultAsync();
        return settings is null
            ? new GoAmlSettingsModel { TrackingStartDate = today }
            : GoAmlSettingsModel.FromEntity(settings);
    }

    private static string ValidateEvidenceRoot(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ValidationException("Evidence folder is required.");
        if (!Path.IsPathFullyQualified(value)) throw new ValidationException("Evidence folder must be an absolute path.");
        var full = Path.GetFullPath(value.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(full, Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException("Select a dedicated evidence folder, not a drive root.");
        }
        return full;
    }

    private static object SettingsSummary(GoAmlSettings settings) => new
    {
        settings.EvidenceRootPath, settings.PortalUrl, settings.TrackingStartDate,
        settings.DueHourLocal, settings.BackupChecker, settings.UpdatedAtUtc, settings.UpdatedBy
    };

    private static object CheckSummary(GoAmlDailyCheck check) => new
    {
        check.CheckDate, check.Status, check.StartedAtUtc, check.StartedBy, check.CompletedAtUtc,
        check.CompletedBy, check.MessageReference, check.ActionOwner, check.ActionDueDate,
        check.ComplianceTaskId, check.EvidenceFileName, check.EvidencePath,
        check.EvidenceSizeBytes, check.EvidenceSha256, check.Notes
    };

    private static ComplianceAuditEvent CreateAudit(string entityType, int entityId, string action, string user,
        string reason, string? oldJson, object newValue) => new()
    {
        EntityType = entityType,
        EntityId = entityId,
        Action = action,
        UserName = user,
        Reason = reason.Trim(),
        OldValueJson = oldJson,
        NewValueJson = JsonSerializer.Serialize(newValue, AuditOptions),
        TimestampUtc = DateTime.UtcNow
    };

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ValidationException("A reason is required.");
    }

    private static string RequireUser(string? userName)
        => string.IsNullOrWhiteSpace(userName)
            ? throw new ValidationException("The current user identity is required.")
            : userName.Trim();

    private static string? NormalizeOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class GoAmlSettingsModel
{
    public string EvidenceRootPath { get; set; } = GoAmlDefaults.EvidenceRootPath;
    public string PortalUrl { get; set; } = GoAmlDefaults.PortalUrl;
    public DateOnly TrackingStartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int DueHourLocal { get; set; } = 10;
    public string? BackupChecker { get; set; }

    public static GoAmlSettingsModel FromEntity(GoAmlSettings settings) => new()
    {
        EvidenceRootPath = settings.EvidenceRootPath,
        PortalUrl = settings.PortalUrl,
        TrackingStartDate = settings.TrackingStartDate,
        DueHourLocal = settings.DueHourLocal,
        BackupChecker = settings.BackupChecker
    };
}

public sealed class GoAmlCompletionModel
{
    public string Status { get; set; } = GoAmlCheckStatuses.NoNewMessages;
    public string? Notes { get; set; }
    public string? MessageReference { get; set; }
    public string? ActionOwner { get; set; }
    public DateOnly? ActionDueDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
}

public sealed record GoAmlDashboardModel(
    GoAmlSettingsModel Settings,
    GoAmlDailyCheck? TodayCheck,
    IReadOnlyList<GoAmlDailyCheck> RecentChecks,
    IReadOnlyList<DateOnly> MissingDates)
{
    public bool IsTodayComplete => TodayCheck is not null && TodayCheck.Status != GoAmlCheckStatuses.Started;
    public bool IsTodayUnavailable => TodayCheck?.Status == GoAmlCheckStatuses.Unavailable;
    public bool IsOverdue => MissingDates.Count > 0;
}

public sealed record GoAmlEvidenceFile(Stream Stream, string ContentType, string FileName);
