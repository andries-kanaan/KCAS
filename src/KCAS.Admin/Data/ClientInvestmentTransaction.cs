using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class ClientInvestmentTransaction
{
    public int Id { get; set; }

    public int ClientInvestmentAccountId { get; set; }

    public ClientInvestmentAccount InvestmentAccount { get; set; } = null!;

    public int LegacyInvestmentHistoryId { get; set; }

    public int? LegacyInvestmentAccountId { get; set; }

    public DateOnly? TransactionDate { get; set; }

    public string? Description { get; set; }

    public decimal? ExchangeRate { get; set; }

    public decimal? InvestmentAmountForeign { get; set; }

    public decimal? InvestmentAmountZar { get; set; }

    public decimal? WithdrawalAmountForeign { get; set; }

    public decimal? WithdrawalAmountZar { get; set; }

    [MaxLength(100)]
    public string? InvestmentFrequency { get; set; }

    public decimal? AnnualIncreasePercent { get; set; }

    public decimal? BalanceForeign { get; set; }

    public decimal? BalanceZar { get; set; }

    public bool IsDeleted { get; set; }

    public bool IsFinal { get; set; }

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
