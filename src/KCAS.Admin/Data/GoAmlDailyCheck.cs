namespace KCAS.Admin.Data;

public sealed class GoAmlSettings
{
    public int Id { get; set; }
    public string EvidenceRootPath { get; set; } = GoAmlDefaults.EvidenceRootPath;
    public string PortalUrl { get; set; } = GoAmlDefaults.PortalUrl;
    public DateOnly TrackingStartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public int DueHourLocal { get; set; } = 10;
    public string? BackupChecker { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}

public sealed class GoAmlDailyCheck
{
    public int Id { get; set; }
    public DateOnly CheckDate { get; set; }
    public string Status { get; set; } = GoAmlCheckStatuses.Started;
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public string StartedBy { get; set; } = "";
    public DateTime? CompletedAtUtc { get; set; }
    public string? CompletedBy { get; set; }
    public string? Notes { get; set; }
    public string? MessageReference { get; set; }
    public string? ActionOwner { get; set; }
    public DateOnly? ActionDueDate { get; set; }
    public int? ComplianceTaskId { get; set; }
    public ComplianceTask? ComplianceTask { get; set; }
    public string? EvidenceFileName { get; set; }
    public string? EvidencePath { get; set; }
    public string? EvidenceContentType { get; set; }
    public long? EvidenceSizeBytes { get; set; }
    public string? EvidenceSha256 { get; set; }
}

public static class GoAmlDefaults
{
    public const string EvidenceRootPath = @"C:\Download\_kanaan\Compliance\dailygoAML";
    public const string PortalUrl = "https://goweb.fic.gov.za/goAMLWeb_PRD/Account/LogOn";
}

public static class GoAmlCheckStatuses
{
    public const string Started = "Started";
    public const string NoNewMessages = "NoNewMessages";
    public const string ActionRequired = "ActionRequired";
    public const string Unavailable = "Unavailable";

    public static readonly IReadOnlyList<string> Completed = [NoNewMessages, ActionRequired, Unavailable];

    public static string Display(string status) => status switch
    {
        Started => "Check started",
        NoNewMessages => "No new or actionable messages",
        ActionRequired => "New message - action required",
        Unavailable => "goAML unavailable",
        _ => status
    };
}
