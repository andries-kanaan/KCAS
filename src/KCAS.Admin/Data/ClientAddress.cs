using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class ClientAddress
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    [MaxLength(40)]
    public string AddressType { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string LinesRaw { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    [MaxLength(80)]
    public string? LegacySourceField { get; set; }
}
