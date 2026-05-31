using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class ClientContactPoint
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    [MaxLength(30)]
    public string ContactType { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? Label { get; set; }

    [MaxLength(254)]
    public string Value { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }

    public int SortOrder { get; set; }

    [MaxLength(80)]
    public string? LegacySourceField { get; set; }
}
