using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientEvidenceScanFile
{
    public int Id { get; set; }

    public int ClientEvidenceScanRunId { get; set; }

    public ClientEvidenceScanRun ScanRun { get; set; } = null!;

    public int? ClientId { get; set; }

    public Client? Client { get; set; }

    [MaxLength(512)]
    public string FullPath { get; set; } = "";

    [MaxLength(512)]
    public string RelativePath { get; set; } = "";

    [MaxLength(260)]
    public string FileName { get; set; } = "";

    [MaxLength(64)]
    public string FileSha256 { get; set; } = "";

    public long FileSizeBytes { get; set; }

    public DateTime FileLastWriteTimeUtc { get; set; }

    [MaxLength(32)]
    public string MatchStatus { get; set; } = ClientEvidenceScanFileStatuses.Unmatched;

    [MaxLength(96)]
    public string? SuggestedEvidenceType { get; set; }

    [MaxLength(512)]
    public string? MatchReason { get; set; }

    public int CandidateCount { get; set; }

    public ClientEvidenceItem? EvidenceItem { get; set; }
}
