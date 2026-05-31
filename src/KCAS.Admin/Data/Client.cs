using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class Client
{
    public int Id { get; set; }

    public int? LegacyClientId { get; set; }

    [MaxLength(30)]
    public string? KanaanId { get; set; }

    [MaxLength(30)]
    public string? Title { get; set; }

    [MaxLength(50)]
    public string? Initials { get; set; }

    [MaxLength(200)]
    public string? FullName { get; set; }

    [MaxLength(200)]
    public string SurnameOrEntityName { get; set; } = string.Empty;

    [MaxLength(220)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Language { get; set; }

    [MaxLength(512)]
    public string? ClientFolder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    public ClientPersonalProfile? PersonalProfile { get; set; }

    public ClientFinancialProfile? FinancialProfile { get; set; }

    public ICollection<ClientContactPoint> ContactPoints { get; } = [];

    public ICollection<ClientAddress> Addresses { get; } = [];

    public ICollection<ClientRelationship> Relationships { get; } = [];

    public ICollection<ClientLegacySnapshot> LegacySnapshots { get; } = [];

    public ICollection<ClientNote> Notes { get; } = [];

    public ICollection<ClientKycPolicy> KycPolicies { get; } = [];

    public ICollection<ClientInvestmentAccount> InvestmentAccounts { get; } = [];
}
