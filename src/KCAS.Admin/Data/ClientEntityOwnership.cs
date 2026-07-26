using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientEntityProfile
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    [MaxLength(64)]
    public string? LegalForm { get; set; }

    [MaxLength(128)]
    public string? RegistrationNumber { get; set; }

    [MaxLength(96)]
    public string? RegistrationCountry { get; set; }

    public DateOnly? EstablishmentDate { get; set; }

    [MaxLength(500)]
    public string? NatureOfBusinessOrPurpose { get; set; }

    [MaxLength(32)]
    public string OwnershipReviewStatus { get; set; } = ClientOwnershipReviewStatuses.Draft;

    [MaxLength(32)]
    public string? ControlConclusion { get; set; }

    [MaxLength(1000)]
    public string? ControlConclusionReason { get; set; }

    public DateTime? OwnershipReviewedAtUtc { get; set; }

    [MaxLength(191)]
    public string? OwnershipReviewedBy { get; set; }

    public DateOnly? NextOwnershipReviewDate { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    [MaxLength(191)]
    public string? UpdatedBy { get; set; }
}

public sealed class ClientRelatedParty
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    [MaxLength(32)]
    public string PartyType { get; set; } = ClientRelatedPartyTypes.NaturalPerson;

    [MaxLength(240)]
    public string DisplayName { get; set; } = "";

    [MaxLength(13)]
    public string? SouthAfricanIdNumber { get; set; }

    [MaxLength(64)]
    public string? PassportNumber { get; set; }

    [MaxLength(96)]
    public string? PassportCountry { get; set; }

    [MaxLength(128)]
    public string? RegistrationNumber { get; set; }

    public DateOnly? BirthDate { get; set; }

    [MaxLength(96)]
    public string? Nationality { get; set; }

    [MaxLength(96)]
    public string? CountryOfResidence { get; set; }

    public decimal? OwnershipPercent { get; set; }

    [MaxLength(1000)]
    public string? ControlBasis { get; set; }

    [MaxLength(1000)]
    public string? AuthorityBasis { get; set; }

    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    [MaxLength(191)]
    public string? UpdatedBy { get; set; }

    public ICollection<ClientRelatedPartyRole> Roles { get; } = [];
    public ICollection<ClientRelatedPartyEvidenceLink> EvidenceLinks { get; } = [];
    public ICollection<ClientEvidenceItem> ScreeningEvidenceItems { get; } = [];
}

public sealed class ClientRelatedPartyRole
{
    public int Id { get; set; }
    public int ClientRelatedPartyId { get; set; }
    public ClientRelatedParty RelatedParty { get; set; } = null!;

    [MaxLength(64)]
    public string RoleCode { get; set; } = "";
}

public sealed class ClientRelatedPartyEvidenceLink
{
    public int Id { get; set; }
    public int ClientRelatedPartyId { get; set; }
    public ClientRelatedParty RelatedParty { get; set; } = null!;
    public int ClientEvidenceItemId { get; set; }
    public ClientEvidenceItem EvidenceItem { get; set; } = null!;

    [MaxLength(32)]
    public string Purpose { get; set; } = ClientRelatedPartyEvidencePurposes.Other;

    public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(191)]
    public string? LinkedBy { get; set; }
}

public static class ClientRelatedPartyTypes
{
    public const string NaturalPerson = "NaturalPerson";
    public const string LegalEntity = "LegalEntity";
    public const string BeneficiaryClass = "BeneficiaryClass";
    public const string Other = "Other";
    public static readonly string[] All = [NaturalPerson, LegalEntity, BeneficiaryClass, Other];
}

public static class ClientRelatedPartyRoles
{
    public const string Founder = "Founder";
    public const string Trustee = "Trustee";
    public const string Beneficiary = "Beneficiary";
    public const string Director = "Director";
    public const string MemberShareholder = "MemberShareholder";
    public const string BeneficialOwner = "BeneficialOwner";
    public const string Controller = "Controller";
    public const string AuthorisedPerson = "AuthorisedPerson";
    public const string Protector = "Protector";
    public const string Partner = "Partner";
    public const string SeniorManagingOfficial = "SeniorManagingOfficial";

    public static readonly string[] All =
    [
        Founder, Trustee, Beneficiary, Director, MemberShareholder, BeneficialOwner,
        Controller, AuthorisedPerson, Protector, Partner, SeniorManagingOfficial
    ];
}

public static class ClientRelatedPartyEvidencePurposes
{
    public const string Identity = "Identity";
    public const string RoleAuthority = "RoleAuthority";
    public const string OwnershipControl = "OwnershipControl";
    public const string Other = "Other";
    public static readonly string[] All = [Identity, RoleAuthority, OwnershipControl, Other];
}

public static class ClientOwnershipReviewStatuses
{
    public const string Draft = "Draft";
    public const string Complete = "Complete";
}

public static class ClientControlConclusions
{
    public const string NaturalPersonsIdentified = "NaturalPersonsIdentified";
    public const string SeniorManagingOfficialFallback = "SeniorManagingOfficialFallback";
    public static readonly string[] All = [NaturalPersonsIdentified, SeniorManagingOfficialFallback];
}
