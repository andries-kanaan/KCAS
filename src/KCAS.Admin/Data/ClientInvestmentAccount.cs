using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class ClientInvestmentAccount
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    public int? LegacyInvestmentAccountId { get; set; }

    public int? LegacyClientId { get; set; }

    public DateOnly? InvestmentDate { get; set; }

    public DateOnly? SurrenderDate { get; set; }

    [MaxLength(256)]
    public string? Administrator { get; set; }

    public int? LegacyAdministratorId { get; set; }

    [MaxLength(256)]
    public string? AccountNumber { get; set; }

    [MaxLength(256)]
    public string? ProductName { get; set; }

    public int? LegacyProductId { get; set; }

    [MaxLength(256)]
    public string? ProductType { get; set; }

    public int? LegacyProductTypeId { get; set; }

    [MaxLength(256)]
    public string? FundName { get; set; }

    public int? LegacyFundId { get; set; }

    public bool IsLinkedHead { get; set; }

    public int? LegacyLinkedAccountId { get; set; }

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

    public ICollection<ClientInvestmentTransaction> Transactions { get; } = [];
}
