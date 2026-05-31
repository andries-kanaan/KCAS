using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class ClientKycPolicy
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    public int LegacyKycId { get; set; }

    public int? LegacyClientId { get; set; }

    [MaxLength(256)]
    public string? KanaanId { get; set; }

    public int? LegacyMainClassId { get; set; }

    [MaxLength(256)]
    public string? MainClassName { get; set; }

    public int? LegacySubClassId { get; set; }

    [MaxLength(256)]
    public string? SubClassName { get; set; }

    [MaxLength(256)]
    public string? SubClassExtra { get; set; }

    [MaxLength(256)]
    public string? Administrator { get; set; }

    [MaxLength(256)]
    public string? Product { get; set; }

    [MaxLength(256)]
    public string? PolicyNumber { get; set; }

    public string? Description { get; set; }

    [MaxLength(256)]
    public string? Fund { get; set; }

    public decimal? Value { get; set; }

    public decimal? LifeCover { get; set; }

    public decimal? DisabilityCover { get; set; }

    public decimal? DreadDiseaseCover { get; set; }

    public decimal? CompulsoryContributionValue { get; set; }

    public decimal? VoluntaryContributionValue { get; set; }

    public decimal? Debt { get; set; }

    public decimal? MonthlyPremium { get; set; }

    public decimal? OnceOffPremium { get; set; }

    public decimal? MonthlyIncome { get; set; }

    public decimal? CapitalAdequacyRatioPercent { get; set; }

    public decimal? TaxPercent { get; set; }

    public bool IncludeInCalculations { get; set; }

    public bool SurrenderOrLiquidate { get; set; }

    public bool IsRetirementAnnuity { get; set; }

    public bool IsPreservationFund { get; set; }

    public bool IsRetrenchmentPackage { get; set; }

    public bool IsQuote { get; set; }

    public DateTime? ValuationDate { get; set; }

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
