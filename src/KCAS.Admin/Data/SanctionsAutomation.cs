using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class SanctionsSourceSnapshot
{
    public int Id { get; set; }
    [MaxLength(1024)] public string SourceUrl { get; set; } = "";
    [MaxLength(64)] public string ContentSha256 { get; set; } = "";
    public byte[] Payload { get; set; } = [];
    public DateTime RetrievedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public int Individuals { get; set; }
    public int Entities { get; set; }
    public int ClientSanctionsBatchId { get; set; }
    public int EmployeeTfsBatchId { get; set; }
}

public sealed class SanctionsSourceCheck
{
    public int Id { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime CompletedAtUtc { get; set; }
    [MaxLength(1024)] public string SourceUrl { get; set; } = "";
    [MaxLength(32)] public string Outcome { get; set; } = "Failed";
    public int? SanctionsSourceSnapshotId { get; set; }
    public SanctionsSourceSnapshot? Snapshot { get; set; }
    public string Detail { get; set; } = "";
    public int SubjectsChecked { get; set; }
    public int NoCandidates { get; set; }
    public int NeedsReview { get; set; }
    public int MaximumSourceAgeHours { get; set; } = 26;
}

public sealed class SanctionsAutomatedResult
{
    public int Id { get; set; }
    public int SanctionsSourceSnapshotId { get; set; }
    public SanctionsSourceSnapshot Snapshot { get; set; } = null!;
    public int? ClientSanctionsSubjectId { get; set; }
    public int? EmployeeProfileId { get; set; }
    [MaxLength(64)] public string ScopeHash { get; set; } = "";
    public string ScopeJson { get; set; } = "";
    [MaxLength(32)] public string Outcome { get; set; } = "Unresolved";
    public string CandidatesJson { get; set; } = "[]";
    public string Finding { get; set; } = "";
    public DateTime PerformedAtUtc { get; set; }
    public int? ClientEvidenceItemId { get; set; }
}

internal static class SanctionsAutomationModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<SanctionsSourceSnapshot>(e =>
        {
            e.Property(x => x.Payload).HasColumnType("longblob");
            e.HasIndex(x => x.ContentSha256);
            e.HasOne<ClientSanctionsBatch>().WithMany().HasForeignKey(x => x.ClientSanctionsBatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EmployeeTfsBatch>().WithMany().HasForeignKey(x => x.EmployeeTfsBatchId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SanctionsSourceCheck>(e =>
        {
            e.HasOne(x => x.Snapshot).WithMany().HasForeignKey(x => x.SanctionsSourceSnapshotId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CompletedAtUtc);
        });
        builder.Entity<SanctionsAutomatedResult>(e =>
        {
            e.HasOne(x => x.Snapshot).WithMany().HasForeignKey(x => x.SanctionsSourceSnapshotId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ClientSanctionsSubject>().WithMany().HasForeignKey(x => x.ClientSanctionsSubjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<EmployeeProfile>().WithMany().HasForeignKey(x => x.EmployeeProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ClientEvidenceItem>().WithMany().HasForeignKey(x => x.ClientEvidenceItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SanctionsSourceSnapshotId, x.EmployeeProfileId, x.PerformedAtUtc });
            e.HasIndex(x => new { x.ClientSanctionsSubjectId, x.PerformedAtUtc });
        });
    }
}

public sealed class SanctionsAutomationOptions
{
    public const string Section = "SanctionsAutomation";
    public const string DefaultSource = "https://transfer.fic.gov.za/public/folder/SE-MPQpJlUKmxgfA11NwZQ/Downloads/Consolidated%20United%20Nations%20Security%20Council%20Sanctions%20List.xml";
    public bool Enabled { get; set; } = true;
    public string SourceUrl { get; set; } = DefaultSource;
    public int PollIntervalMinutes { get; set; } = 60;
    public int MaximumSourceAgeHours { get; set; } = 26;
    public int MinimumIndividuals { get; set; } = 100;
    public int MinimumEntities { get; set; } = 25;
}
