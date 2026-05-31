using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class ClientFundValuation
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    public int LegacyFundId { get; set; }

    public int? LegacyClientId { get; set; }

    [MaxLength(30)]
    public string? KanaanId { get; set; }

    [MaxLength(256)]
    public string FundName { get; set; } = string.Empty;

    public decimal? AmountForeign { get; set; }

    public decimal? AmountZar { get; set; }

    public string? FundDescription { get; set; }

    [MaxLength(256)]
    public string? CompanyClientNumber { get; set; }

    [MaxLength(256)]
    public string? Administrator { get; set; }

    [MaxLength(256)]
    public string? ProductName { get; set; }

    [MaxLength(256)]
    public string? ProductType { get; set; }

    public string? CompanyDescription { get; set; }

    [MaxLength(256)]
    public string? InvestmentUniqueNumber { get; set; }

    public DateOnly? ValuationDate { get; set; }

    [MaxLength(256)]
    public string? OpenedBy { get; set; }

    [MaxLength(256)]
    public string? UpdatedBy { get; set; }

    public int? LegacyOpenedByUserId { get; set; }

    public int? LegacyUpdatedByUserId { get; set; }

    public DateTime? LegacyOpenedAt { get; set; }

    public DateTime? LegacyUpdatedAt { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
}
