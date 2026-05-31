using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class ClientLegacySnapshot
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    [MaxLength(80)]
    public string SourceTable { get; set; } = string.Empty;

    public int SourceId { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
}
