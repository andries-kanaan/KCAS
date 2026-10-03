using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace KCAS.Admin.Data;

public sealed class ClientSanctionsBatch
{
    public int Id { get; set; }
    [MaxLength(191)] public string SourceVersion { get; set; } = "";
    [MaxLength(1024)] public string SourceUrl { get; set; } = "";
    public DateTime SourcePublishedAtUtc { get; set; }
    public string Reason { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(191)] public string CreatedBy { get; set; } = "";
    [MaxLength(32)] public string Version { get; set; } = ComplianceWorkflowAccess.NewVersion();
    public string RecipientUserIdsJson { get; set; } = "[]";
}

public sealed class ClientSanctionsSubject
{
    public int Id { get; set; }
    public int ClientSanctionsBatchId { get; set; }
    public ClientSanctionsBatch Batch { get; set; } = null!;
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;
    [MaxLength(96)] public string SubjectKey { get; set; } = "";
    [MaxLength(64)] public string ScopeHash { get; set; } = "";
    [MaxLength(96)] public string SubjectType { get; set; } = "";
    [MaxLength(240)] public string SubjectName { get; set; } = "";
    public int? ClientRelatedPartyId { get; set; }
    public string IdentitySummary { get; set; } = "";
    public bool IsCurrent { get; set; } = true;
    public int? ComplianceTaskId { get; set; }
    public ComplianceTask? Task { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ClientSanctionsCoverageRecord
{
    public int Id { get; set; }
    public int ClientSanctionsSubjectId { get; set; }
    public ClientSanctionsSubject Subject { get; set; } = null!;
    public int? ClientEvidenceItemId { get; set; }
    public ClientEvidenceItem? Evidence { get; set; }
    [MaxLength(64)] public string? EvidenceFingerprint { get; set; }
    [MaxLength(64)] public string Outcome { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? ExclusionReference { get; set; }
    [MaxLength(191)] public string RecordedBy { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ComplaintCase
{
    public int Id { get; set; }
    public int? ClientId { get; set; }
    public Client? Client { get; set; }
    [MaxLength(240)] public string ComplainantName { get; set; } = "";
    public string ContactDetails { get; set; } = "";
    public DateTime? ReceivedAtUtc { get; set; }
    [MaxLength(48)] public string Channel { get; set; } = "Email";
    public string Allegation { get; set; } = "";
    public string RequestedOutcome { get; set; } = "";
    [MaxLength(96)] public string Category { get; set; } = "Other";
    public string SecondaryThemes { get; set; } = "";
    [MaxLength(191)] public string HandlerUserId { get; set; } = "";
    [MaxLength(191)] public string? ImplicatedUserId { get; set; }
    public bool IsReportable { get; set; } = true;
    [MaxLength(32)] public string Status { get; set; } = "Open";
    [MaxLength(48)] public string? Decision { get; set; }
    public string? DecisionReasons { get; set; }
    public string? Remedy { get; set; }
    public decimal CompensationAwarded { get; set; }
    public decimal GoodwillAwarded { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    [MaxLength(191)] public string? DecidedBy { get; set; }
    public DateOnly? NextUpdateDate { get; set; }
    public int? ComplianceTaskId { get; set; }
    public ComplianceTask? Task { get; set; }
    [MaxLength(64)] public string? LegacyKey { get; set; }
    [MaxLength(64)] public string? LegacyRowHash { get; set; }
    public string? LegacySourceJson { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(191)] public string UpdatedBy { get; set; } = "";
    [MaxLength(32)] public string Version { get; set; } = ComplianceWorkflowAccess.NewVersion();
}

public sealed class ComplaintEvent
{
    public int Id { get; set; }
    public int ComplaintCaseId { get; set; }
    public ComplaintCase Case { get; set; } = null!;
    [MaxLength(48)] public string Kind { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public string Details { get; set; } = "";
    public string EvidenceReference { get; set; } = "";
    [MaxLength(191)] public string PerformedBy { get; set; } = "";
    public string? RecourseDetails { get; set; }
    public decimal? Amount { get; set; }
    [MaxLength(191)] public string RecordedBy { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}

internal static class ComplianceCoverageAndComplaintsModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<ClientSanctionsBatch>(e => { e.HasIndex(x => x.SourceVersion).IsUnique(); e.Property(x => x.Version).IsConcurrencyToken(); });
        builder.Entity<ClientSanctionsSubject>(e =>
        {
            e.HasIndex(x => new { x.ClientSanctionsBatchId, x.SubjectKey, x.ScopeHash }).IsUnique();
            e.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.ClientSanctionsBatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.ComplianceTaskId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ClientSanctionsCoverageRecord>(e =>
        {
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.ClientSanctionsSubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Evidence).WithMany().HasForeignKey(x => x.ClientEvidenceItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ClientSanctionsSubjectId, x.RecordedAtUtc });
        });
        builder.Entity<ComplaintCase>(e =>
        {
            e.Property(x => x.NextUpdateDate).HasConversion(new ValueConverter<DateOnly?, DateTime?>(
                value => value.HasValue ? value.Value.ToDateTime(TimeOnly.MinValue) : null,
                value => value.HasValue ? DateOnly.FromDateTime(value.Value) : null)).HasColumnType("date");
            e.Property(x => x.Version).IsConcurrencyToken(); e.HasIndex(x => x.LegacyKey).IsUnique();
            e.HasIndex(x => new { x.Status, x.ReceivedAtUtc });
            e.Property(x => x.CompensationAwarded).HasPrecision(18, 2); e.Property(x => x.GoodwillAwarded).HasPrecision(18, 2);
            e.HasOne(x => x.Client).WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.ComplianceTaskId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ComplaintEvent>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne(x => x.Case).WithMany().HasForeignKey(x => x.ComplaintCaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ComplaintCaseId, x.OccurredAtUtc });
        });
    }
}
