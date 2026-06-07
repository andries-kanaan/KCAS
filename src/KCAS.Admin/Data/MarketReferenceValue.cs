using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class MarketReferenceValue
{
    public int Id { get; set; }

    public int? LegacyMiscInfoId { get; set; }

    public DateOnly? PriceDate { get; set; }

    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    public decimal? Value { get; set; }

    [MaxLength(256)]
    public string? OpenedBy { get; set; }

    [MaxLength(256)]
    public string? UpdatedBy { get; set; }

    public DateTime? LegacyOpenedAt { get; set; }

    public DateTime? LegacyUpdatedAt { get; set; }
}
