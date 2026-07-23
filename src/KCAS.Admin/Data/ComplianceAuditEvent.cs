using System.ComponentModel.DataAnnotations.Schema;

namespace KCAS.Admin.Data;

public sealed class ComplianceAuditEvent
{
    public long Id { get; set; }
    public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
    public string Action { get; set; } = "";
    public string? OldValueJson { get; set; }
    public string? NewValueJson { get; set; }
    public string? UserName { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = "";

    [NotMapped]
    public string EntityLabel => $"{EntityType} #{EntityId}";
}
