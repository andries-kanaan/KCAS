using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientEvidenceScanRun
{
    public int Id { get; set; }

    [MaxLength(512)]
    public string RootPath { get; set; } = "";

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAtUtc { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = ClientEvidenceScanStatuses.Running;

    public int TotalFiles { get; set; }

    public int LinkedFiles { get; set; }

    public int UnmatchedFiles { get; set; }

    public int AmbiguousFiles { get; set; }

    public int SkippedFiles { get; set; }

    public string? ErrorMessage { get; set; }

    [MaxLength(191)]
    public string? StartedBy { get; set; }

    public ICollection<ClientEvidenceScanFile> Files { get; } = [];
}
