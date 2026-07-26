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

    [MaxLength(96)]
    public string ClientCategory { get; set; } = ClientCategories.NaturalPerson;

    [MaxLength(32)]
    public string ClientCategorySource { get; set; } = ClientCategorySources.Unknown;

    [MaxLength(512)]
    public string? ClientCategoryReason { get; set; }

    public DateTime? ClientCategoryUpdatedAtUtc { get; set; }

    [MaxLength(191)]
    public string? ClientCategoryUpdatedBy { get; set; }

    public bool IsActive { get; set; } = true;

    [MaxLength(32)]
    public string LifecycleStatus { get; set; } = ClientLifecycleStatuses.Unreviewed;

    [MaxLength(1000)]
    public string? LifecycleReason { get; set; }

    public DateTime? LifecycleReviewedAtUtc { get; set; }

    [MaxLength(191)]
    public string? LifecycleReviewedBy { get; set; }

    public int? DuplicateOfClientId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    [MaxLength(32)]
    public string LegacyReconciliationStatus { get; set; } = LegacyReconciliationStatuses.Unscanned;

    public ClientPersonalProfile? PersonalProfile { get; set; }

    public ClientFinancialProfile? FinancialProfile { get; set; }

    public ClientEntityProfile? EntityProfile { get; set; }

    public ICollection<ClientContactPoint> ContactPoints { get; } = [];

    public ICollection<ClientAddress> Addresses { get; } = [];

    public ICollection<ClientRelationship> Relationships { get; } = [];

    public ICollection<ClientLegacySnapshot> LegacySnapshots { get; } = [];

    public ICollection<ClientNote> Notes { get; } = [];

    public ICollection<ClientKycPolicy> KycPolicies { get; } = [];

    public ICollection<ClientKycRecommendation> KycRecommendations { get; } = [];

    public ICollection<ClientInvestmentAccount> InvestmentAccounts { get; } = [];

    public ICollection<ClientFundValuation> FundValuations { get; } = [];

    public ICollection<ClientEvidenceItem> EvidenceItems { get; } = [];

    public ICollection<ClientEvidenceException> EvidenceExceptions { get; } = [];

    public ICollection<ClientEvidenceOwnershipAlias> EvidenceOwnershipAliases { get; } = [];

    public ICollection<ClientRelatedParty> RelatedParties { get; } = [];

    public ICollection<ClientRiskAssessment> RiskAssessments { get; } = [];

    public ICollection<ClientVerificationItem> VerificationItems { get; } = [];
}

public static class ClientLifecycleStatuses
{
    public const string Unreviewed = "Unreviewed";
    public const string Current = "Current";
    public const string Closed = "Closed";
    public const string Deceased = "Deceased";
    public const string Duplicate = "Duplicate";
    public const string Historical = "Historical";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Unreviewed,
        Current,
        Closed,
        Deceased,
        Duplicate,
        Historical
    };
}

public static class LegacyReconciliationStatuses
{
    public const string Unscanned = "Unscanned";
    public const string UnchangedReconciled = "UnchangedReconciled";
    public const string NewPendingReview = "NewPendingReview";
    public const string ChangedPendingReview = "ChangedPendingReview";
    public const string Conflict = "Conflict";
    public const string Reconciled = "Reconciled";
}

public static class ClientCategories
{
    public const string NaturalPerson = "NaturalPerson";
    public const string LegalPerson = "LegalPerson";
    public const string Trust = "Trust";
    public const string Other = "Other";
}

public static class ClientCategorySources
{
    public const string Unknown = "Unknown";
    public const string LegacyImportInferred = "LegacyImportInferred";
    public const string Manual = "Manual";
    public const string EvidenceScanInferred = "EvidenceScanInferred";
}
