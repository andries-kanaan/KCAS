using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class LegacySourceSnapshot
{
    public long Id { get; set; }

    [MaxLength(64)]
    public string SourceTable { get; set; } = string.Empty;

    public long SourceId { get; set; }

    [MaxLength(64)]
    public string Fingerprint { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public DateTime AcceptedAtUtc { get; set; } = DateTime.UtcNow;

    public long? AcceptedFromRunId { get; set; }

    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;
}
