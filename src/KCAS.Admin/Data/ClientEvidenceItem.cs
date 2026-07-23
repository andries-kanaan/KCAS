using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientEvidenceItem
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    public int? ClientEvidenceRequirementId { get; set; }

    public ClientEvidenceRequirement? Requirement { get; set; }

    [MaxLength(96)]
    public string EvidenceType { get; set; } = "";

    [MaxLength(240)]
    public string Title { get; set; } = "";

    [MaxLength(512)]
    public string? SourcePath { get; set; }

    [MaxLength(512)]
    public string? RelativePath { get; set; }

    [MaxLength(260)]
    public string? FileName { get; set; }

    [MaxLength(64)]
    public string? FileSha256 { get; set; }

    public long? FileSizeBytes { get; set; }

    public DateTime? FileLastWriteTimeUtc { get; set; }

    public DateOnly? ReceivedDate { get; set; }

    public DateOnly? VerifiedDate { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    [MaxLength(191)]
    public string? Reviewer { get; set; }

    public DateOnly? ScreeningReviewDate { get; set; }

    [MaxLength(96)]
    public string? ScreeningSubjectType { get; set; }

    [MaxLength(240)]
    public string? ScreeningSubjectName { get; set; }

    [MaxLength(96)]
    public string? ScreeningOutcome { get; set; }

    [MaxLength(32)]
    public string? ScreeningRiskSignal { get; set; }

    public bool EscalationRequired { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = ClientEvidenceStatuses.Linked;

    public string? Notes { get; set; }

    public int? ClientEvidenceScanFileId { get; set; }

    public ClientEvidenceScanFile? ScanFile { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    [MaxLength(191)]
    public string? UpdatedBy { get; set; }
}
