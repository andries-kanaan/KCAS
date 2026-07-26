using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientEntityOwnershipService(ApplicationDbContext db)
{
    public async Task<ClientEntityOwnershipModel> LoadAsync(int clientId)
    {
        var client = await db.Clients
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.EntityProfile)
            .Include(item => item.RelatedParties).ThenInclude(party => party.Roles)
            .Include(item => item.RelatedParties).ThenInclude(party => party.EvidenceLinks).ThenInclude(link => link.EvidenceItem)
            .SingleOrDefaultAsync(item => item.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");

        EnsureSupportedCategory(client.ClientCategory);

        var screening = await db.ClientEvidenceItems
            .AsNoTracking()
            .Where(item => item.ClientId == clientId &&
                item.ClientRelatedPartyId != null &&
                (item.OwnershipStatus == ClientEvidenceOwnershipStatuses.Confirmed ||
                 item.OwnershipStatus == ClientEvidenceOwnershipStatuses.AutoAssigned))
            .ToListAsync();
        var evidence = await db.ClientEvidenceItems
            .AsNoTracking()
            .Where(item => item.ClientId == clientId &&
                (item.OwnershipStatus == ClientEvidenceOwnershipStatuses.Confirmed ||
                 item.OwnershipStatus == ClientEvidenceOwnershipStatuses.AutoAssigned))
            .OrderByDescending(item => item.SelectionStatus == ClientEvidenceSelectionStatuses.Current)
            .ThenBy(item => item.EvidenceType)
            .ThenBy(item => item.Title)
            .ToListAsync();

        var blockers = EntityOwnershipRules.CalculateBlockers(
            client.ClientCategory,
            client.EntityProfile,
            client.RelatedParties,
            screening,
            DateOnly.FromDateTime(DateTime.Today));

        return new ClientEntityOwnershipModel
        {
            ClientId = client.Id,
            DisplayName = client.DisplayName,
            ClientCategory = client.ClientCategory,
            Profile = ClientEntityProfileEditModel.FromEntity(client.EntityProfile),
            Parties = client.RelatedParties
                .OrderByDescending(party => party.IsActive)
                .ThenBy(party => party.DisplayName)
                .Select(ClientRelatedPartyModel.FromEntity)
                .ToList(),
            EvidenceChoices = evidence.Select(item => new ClientRelatedPartyEvidenceChoice
            {
                Id = item.Id,
                Title = item.Title,
                EvidenceType = item.EvidenceType,
                VerifiedDate = item.VerifiedDate,
                SelectionStatus = item.SelectionStatus
            }).ToList(),
            Blockers = blockers
        };
    }

    public async Task SaveProfileAsync(int clientId, ClientEntityProfileEditModel request, string? userName, string reason)
    {
        RequireReason(reason);
        var client = await db.Clients.Include(item => item.EntityProfile).SingleOrDefaultAsync(item => item.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");
        EnsureSupportedCategory(client.ClientCategory);

        var oldJson = Snapshot(client.EntityProfile);
        var profile = client.EntityProfile ?? new ClientEntityProfile { ClientId = clientId };
        profile.LegalForm = Required(request.LegalForm, "Legal form");
        profile.RegistrationNumber = Required(request.RegistrationNumber, "Registration or trust number");
        profile.RegistrationCountry = Required(request.RegistrationCountry, "Registration country");
        profile.EstablishmentDate = request.EstablishmentDate;
        profile.NatureOfBusinessOrPurpose = Required(request.NatureOfBusinessOrPurpose, "Nature of business or trust purpose");
        profile.UpdatedAtUtc = DateTime.UtcNow;
        profile.UpdatedBy = userName;

        if (client.EntityProfile is null)
        {
            db.ClientEntityProfiles.Add(profile);
        }
        else if (profile.OwnershipReviewStatus == ClientOwnershipReviewStatuses.Complete)
        {
            profile.OwnershipReviewStatus = ClientOwnershipReviewStatuses.Draft;
            profile.ControlConclusion = null;
            profile.ControlConclusionReason = null;
            profile.OwnershipReviewedAtUtc = null;
            profile.OwnershipReviewedBy = null;
            profile.NextOwnershipReviewDate = null;
        }

        await db.SaveChangesAsync();
        await AddAuditAsync(nameof(ClientEntityProfile), profile.Id, "SaveProfile", oldJson, Snapshot(profile), userName, reason);
    }

    public async Task<int> SavePartyAsync(int clientId, ClientRelatedPartyEditModel request, string? userName, string reason)
    {
        RequireReason(reason);
        var client = await db.Clients.Include(item => item.EntityProfile).SingleOrDefaultAsync(item => item.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");
        EnsureSupportedCategory(client.ClientCategory);
        ValidateParty(request);

        ClientRelatedParty party;
        string? oldJson = null;
        if (request.Id is null)
        {
            party = new ClientRelatedParty { ClientId = clientId };
            db.ClientRelatedParties.Add(party);
        }
        else
        {
            party = await db.ClientRelatedParties
                .Include(item => item.Roles)
                .SingleOrDefaultAsync(item => item.Id == request.Id && item.ClientId == clientId)
                ?? throw new InvalidOperationException("Related party not found.");
            oldJson = Snapshot(party);
        }

        party.PartyType = request.PartyType;
        party.DisplayName = request.DisplayName.Trim();
        party.SouthAfricanIdNumber = Normalize(request.SouthAfricanIdNumber);
        party.PassportNumber = Normalize(request.PassportNumber);
        party.PassportCountry = Normalize(request.PassportCountry);
        party.RegistrationNumber = Normalize(request.RegistrationNumber);
        party.BirthDate = request.BirthDate;
        party.Nationality = Normalize(request.Nationality);
        party.CountryOfResidence = Normalize(request.CountryOfResidence);
        party.OwnershipPercent = request.OwnershipPercent;
        party.ControlBasis = Normalize(request.ControlBasis);
        party.AuthorityBasis = Normalize(request.AuthorityBasis);
        party.EffectiveFrom = request.EffectiveFrom;
        party.EffectiveTo = request.EffectiveTo;
        party.Notes = Normalize(request.Notes);
        party.UpdatedAtUtc = DateTime.UtcNow;
        party.UpdatedBy = userName;

        if (request.Id is not null)
        {
            db.ClientRelatedPartyRoles.RemoveRange(party.Roles);
            party.Roles.Clear();
        }
        foreach (var role in request.Roles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            party.Roles.Add(new ClientRelatedPartyRole { RoleCode = role });
        }

        ResetOwnershipReview(client.EntityProfile, userName);
        await db.SaveChangesAsync();
        await AddAuditAsync(nameof(ClientRelatedParty), party.Id, request.Id is null ? "Add" : "Update", oldJson, Snapshot(party), userName, reason);
        return party.Id;
    }

    public async Task DeactivatePartyAsync(int clientId, int partyId, string? userName, string reason)
    {
        RequireReason(reason);
        var party = await db.ClientRelatedParties
            .Include(item => item.Client).ThenInclude(client => client.EntityProfile)
            .Include(item => item.Roles)
            .SingleOrDefaultAsync(item => item.Id == partyId && item.ClientId == clientId)
            ?? throw new InvalidOperationException("Related party not found.");
        if (!party.IsActive)
        {
            return;
        }

        var oldJson = Snapshot(party);
        party.IsActive = false;
        party.EffectiveTo ??= DateOnly.FromDateTime(DateTime.Today);
        party.UpdatedAtUtc = DateTime.UtcNow;
        party.UpdatedBy = userName;
        ResetOwnershipReview(party.Client.EntityProfile, userName);
        await db.SaveChangesAsync();
        await AddAuditAsync(nameof(ClientRelatedParty), party.Id, "Deactivate", oldJson, Snapshot(party), userName, reason);
    }

    public async Task LinkEvidenceAsync(int clientId, int partyId, int evidenceItemId, string purpose, string? userName, string reason)
    {
        RequireReason(reason);
        if (!ClientRelatedPartyEvidencePurposes.All.Contains(purpose))
        {
            throw new ValidationException("Evidence purpose is invalid.");
        }

        var party = await db.ClientRelatedParties.SingleOrDefaultAsync(item => item.Id == partyId && item.ClientId == clientId)
            ?? throw new InvalidOperationException("Related party not found.");
        var evidence = await db.ClientEvidenceItems.SingleOrDefaultAsync(item => item.Id == evidenceItemId && item.ClientId == clientId)
            ?? throw new ValidationException("Evidence must belong to the same client.");
        if (!ClientEvidenceOwnershipStatuses.IsActive(evidence.OwnershipStatus))
        {
            throw new ValidationException("Unresolved or excluded evidence cannot be linked.");
        }
        if (await db.ClientRelatedPartyEvidenceLinks.AnyAsync(link =>
            link.ClientRelatedPartyId == partyId && link.ClientEvidenceItemId == evidenceItemId && link.Purpose == purpose))
        {
            return;
        }

        var link = new ClientRelatedPartyEvidenceLink
        {
            ClientRelatedPartyId = partyId,
            ClientEvidenceItemId = evidenceItemId,
            Purpose = purpose,
            LinkedBy = userName
        };
        db.ClientRelatedPartyEvidenceLinks.Add(link);
        await db.SaveChangesAsync();
        await AddAuditAsync(nameof(ClientRelatedPartyEvidenceLink), link.Id, "Link", null, Snapshot(link), userName, reason);
    }

    public async Task UnlinkEvidenceAsync(int clientId, int linkId, string? userName, string reason)
    {
        RequireReason(reason);
        var link = await db.ClientRelatedPartyEvidenceLinks
            .Include(item => item.RelatedParty).ThenInclude(party => party.Client).ThenInclude(client => client.EntityProfile)
            .SingleOrDefaultAsync(item => item.Id == linkId && item.RelatedParty.ClientId == clientId)
            ?? throw new InvalidOperationException("Evidence link not found.");
        var oldJson = Snapshot(link);
        ResetOwnershipReview(link.RelatedParty.Client.EntityProfile, userName);
        db.ClientRelatedPartyEvidenceLinks.Remove(link);
        await db.SaveChangesAsync();
        await AddAuditAsync(nameof(ClientRelatedPartyEvidenceLink), linkId, "Unlink", oldJson, null, userName, reason);
    }

    public async Task CompleteOwnershipReviewAsync(
        int clientId,
        string conclusion,
        string conclusionReason,
        DateOnly nextReviewDate,
        string? userName,
        string reason)
    {
        RequireReason(reason);
        if (!ClientControlConclusions.All.Contains(conclusion))
        {
            throw new ValidationException("Control conclusion is invalid.");
        }
        if (string.IsNullOrWhiteSpace(conclusionReason))
        {
            throw new ValidationException("The ownership/control conclusion reason is required.");
        }
        if (nextReviewDate <= DateOnly.FromDateTime(DateTime.Today))
        {
            throw new ValidationException("The next ownership review date must be in the future.");
        }

        var client = await db.Clients
            .AsSplitQuery()
            .Include(item => item.EntityProfile)
            .Include(item => item.RelatedParties).ThenInclude(party => party.Roles)
            .Include(item => item.RelatedParties).ThenInclude(party => party.EvidenceLinks).ThenInclude(link => link.EvidenceItem)
            .SingleOrDefaultAsync(item => item.Id == clientId)
            ?? throw new InvalidOperationException("Client not found.");
        EnsureSupportedCategory(client.ClientCategory);
        var profile = client.EntityProfile ?? throw new ValidationException("Complete the entity profile first.");

        if (conclusion == ClientControlConclusions.SeniorManagingOfficialFallback &&
            !client.RelatedParties.Any(party => party.IsActive &&
                party.Roles.Any(role => role.RoleCode == ClientRelatedPartyRoles.SeniorManagingOfficial)))
        {
            throw new ValidationException("Record the senior managing official used as the fallback.");
        }

        var screening = await db.ClientEvidenceItems
            .Where(item => item.ClientId == clientId && item.ClientRelatedPartyId != null)
            .ToListAsync();
        var blockers = EntityOwnershipRules.CalculateBlockers(
            client.ClientCategory, profile, client.RelatedParties, screening,
            DateOnly.FromDateTime(DateTime.Today), requireCompletedReview: false);
        if (blockers.Count > 0)
        {
            throw new ValidationException($"Ownership review cannot be completed: {blockers[0]}");
        }

        var oldJson = Snapshot(profile);
        profile.OwnershipReviewStatus = ClientOwnershipReviewStatuses.Complete;
        profile.ControlConclusion = conclusion;
        profile.ControlConclusionReason = conclusionReason.Trim();
        profile.OwnershipReviewedAtUtc = DateTime.UtcNow;
        profile.OwnershipReviewedBy = userName;
        profile.NextOwnershipReviewDate = nextReviewDate;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        profile.UpdatedBy = userName;
        await db.SaveChangesAsync();
        await AddAuditAsync(nameof(ClientEntityProfile), profile.Id, "CompleteOwnershipReview", oldJson, Snapshot(profile), userName, reason);
    }

    private static void ValidateParty(ClientRelatedPartyEditModel request)
    {
        if (!ClientRelatedPartyTypes.All.Contains(request.PartyType))
        {
            throw new ValidationException("Party type is invalid.");
        }
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new ValidationException("Related-party name is required.");
        }
        if (request.Roles.Count == 0 || request.Roles.Any(role => !ClientRelatedPartyRoles.All.Contains(role)))
        {
            throw new ValidationException("Select at least one valid role.");
        }
        if (request.OwnershipPercent is < 0 or > 100)
        {
            throw new ValidationException("Ownership percentage must be between 0 and 100.");
        }
        if ((request.OwnershipPercent is > 0 ||
             request.Roles.Any(role => role is ClientRelatedPartyRoles.BeneficialOwner or ClientRelatedPartyRoles.Controller)) &&
            string.IsNullOrWhiteSpace(request.ControlBasis))
        {
            throw new ValidationException("Control basis is required for an owner or controller.");
        }
        if (request.Roles.Any(role => role is ClientRelatedPartyRoles.Trustee or ClientRelatedPartyRoles.Director or
                ClientRelatedPartyRoles.MemberShareholder or ClientRelatedPartyRoles.AuthorisedPerson or
                ClientRelatedPartyRoles.Founder or ClientRelatedPartyRoles.Protector or ClientRelatedPartyRoles.Partner) &&
            string.IsNullOrWhiteSpace(request.AuthorityBasis))
        {
            throw new ValidationException("Authority basis is required for the selected role.");
        }
    }

    private static void EnsureSupportedCategory(string category)
    {
        if (category is not (ClientCategories.Trust or ClientCategories.LegalPerson))
        {
            throw new ValidationException("The ownership register is available only for trust and legal-person clients.");
        }
    }

    private static void ResetOwnershipReview(ClientEntityProfile? profile, string? userName)
    {
        if (profile is null || profile.OwnershipReviewStatus != ClientOwnershipReviewStatuses.Complete)
        {
            return;
        }
        profile.OwnershipReviewStatus = ClientOwnershipReviewStatuses.Draft;
        profile.ControlConclusion = null;
        profile.ControlConclusionReason = null;
        profile.OwnershipReviewedAtUtc = null;
        profile.OwnershipReviewedBy = null;
        profile.NextOwnershipReviewDate = null;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        profile.UpdatedBy = userName;
    }

    private async Task AddAuditAsync(string entityType, int entityId, string action, string? oldJson, string? newJson, string? userName, string reason)
    {
        db.ComplianceAuditEvents.Add(new ComplianceAuditEvent
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            OldValueJson = oldJson,
            NewValueJson = newJson,
            UserName = userName,
            TimestampUtc = DateTime.UtcNow,
            Reason = reason.Trim()
        });
        await db.SaveChangesAsync();
    }

    private static string? Snapshot(object? value) => value is null ? null : JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
    });
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Required(string? value, string label) => Normalize(value) ?? throw new ValidationException($"{label} is required.");
    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("A reason is required.");
        }
    }
}

public static class EntityOwnershipRules
{
    private static readonly string[] AuthorityRoles =
    [
        ClientRelatedPartyRoles.Founder, ClientRelatedPartyRoles.Trustee, ClientRelatedPartyRoles.Director,
        ClientRelatedPartyRoles.MemberShareholder, ClientRelatedPartyRoles.AuthorisedPerson,
        ClientRelatedPartyRoles.Protector, ClientRelatedPartyRoles.Partner
    ];

    public static List<string> CalculateBlockers(
        string clientCategory,
        ClientEntityProfile? profile,
        IEnumerable<ClientRelatedParty> allParties,
        IEnumerable<ClientEvidenceItem> screeningItems,
        DateOnly today,
        bool requireCompletedReview = true)
    {
        if (clientCategory is not (ClientCategories.Trust or ClientCategories.LegalPerson))
        {
            return [];
        }

        var blockers = new List<string>();
        var parties = allParties.Where(party => party.IsActive &&
            (party.EffectiveTo is null || party.EffectiveTo >= today)).ToList();
        if (profile is null)
        {
            return ["Entity profile is missing."];
        }
        if (string.IsNullOrWhiteSpace(profile.LegalForm)) blockers.Add("Legal form is missing.");
        if (string.IsNullOrWhiteSpace(profile.RegistrationNumber)) blockers.Add("Registration or trust number is missing.");
        if (string.IsNullOrWhiteSpace(profile.RegistrationCountry)) blockers.Add("Registration country is missing.");
        if (string.IsNullOrWhiteSpace(profile.NatureOfBusinessOrPurpose)) blockers.Add("Nature of business or trust purpose is missing.");

        if (clientCategory == ClientCategories.Trust)
        {
            RequireRole(parties, ClientRelatedPartyRoles.Founder, "A founder must be recorded.", blockers);
            RequireRole(parties, ClientRelatedPartyRoles.Trustee, "At least one trustee must be recorded.", blockers);
            RequireRole(parties, ClientRelatedPartyRoles.Beneficiary, "A beneficiary or beneficiary class must be recorded.", blockers);
        }
        else
        {
            if (!HasAnyRole(parties, ClientRelatedPartyRoles.Director, ClientRelatedPartyRoles.MemberShareholder, ClientRelatedPartyRoles.Partner))
            {
                blockers.Add("At least one director, member/shareholder or partner must be recorded.");
            }
        }
        if (!HasAnyRole(parties, ClientRelatedPartyRoles.BeneficialOwner, ClientRelatedPartyRoles.Controller, ClientRelatedPartyRoles.SeniorManagingOfficial))
        {
            blockers.Add("A beneficial owner, controller or documented senior-managing-official fallback must be recorded.");
        }

        foreach (var party in parties)
        {
            var roles = party.Roles.Select(role => role.RoleCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (party.PartyType == ClientRelatedPartyTypes.NaturalPerson &&
                !HasVerifiedLink(party, ClientRelatedPartyEvidencePurposes.Identity, today))
            {
                blockers.Add($"{party.DisplayName}: verified identity evidence is missing.");
            }
            if (roles.Any(role => AuthorityRoles.Contains(role)) &&
                !HasVerifiedLink(party, ClientRelatedPartyEvidencePurposes.RoleAuthority, today))
            {
                blockers.Add($"{party.DisplayName}: verified role or authority evidence is missing.");
            }
            if (roles.Any(role => role is ClientRelatedPartyRoles.BeneficialOwner or ClientRelatedPartyRoles.Controller or ClientRelatedPartyRoles.MemberShareholder) &&
                !HasVerifiedLink(party, ClientRelatedPartyEvidencePurposes.OwnershipControl, today))
            {
                blockers.Add($"{party.DisplayName}: verified ownership or control evidence is missing.");
            }
            foreach (var evidenceType in new[] { "PepPip", "SanctionsTfs" })
            {
                var screened = screeningItems.Any(item => item.ClientRelatedPartyId == party.Id &&
                    item.EvidenceType == evidenceType &&
                    ClientEvidenceOwnershipStatuses.IsActive(item.OwnershipStatus) &&
                    item.VerifiedDate is not null &&
                    (item.ExpiryDate is null || item.ExpiryDate >= today));
                if (!screened)
                {
                    blockers.Add($"{party.DisplayName}: current {evidenceType} screening is missing.");
                }
            }
        }

        if (requireCompletedReview)
        {
            if (profile.OwnershipReviewStatus != ClientOwnershipReviewStatuses.Complete)
            {
                blockers.Add("Ownership/control review is not complete.");
            }
            else if (profile.NextOwnershipReviewDate is null || profile.NextOwnershipReviewDate < today)
            {
                blockers.Add("Ownership/control review is overdue.");
            }
        }
        return blockers.Distinct().ToList();
    }

    private static bool HasVerifiedLink(ClientRelatedParty party, string purpose, DateOnly today) =>
        party.EvidenceLinks.Any(link => link.Purpose == purpose &&
            ClientEvidenceOwnershipStatuses.IsActive(link.EvidenceItem.OwnershipStatus) &&
            link.EvidenceItem.VerifiedDate is not null &&
            (link.EvidenceItem.ExpiryDate is null || link.EvidenceItem.ExpiryDate >= today));
    private static bool HasAnyRole(IEnumerable<ClientRelatedParty> parties, params string[] roles) =>
        parties.Any(party => party.Roles.Any(role => roles.Contains(role.RoleCode)));
    private static void RequireRole(IEnumerable<ClientRelatedParty> parties, string role, string message, ICollection<string> blockers)
    {
        if (!HasAnyRole(parties, role)) blockers.Add(message);
    }
}

public sealed class ClientEntityOwnershipModel
{
    public int ClientId { get; set; }
    public string DisplayName { get; set; } = "";
    public string ClientCategory { get; set; } = "";
    public ClientEntityProfileEditModel Profile { get; set; } = new();
    public List<ClientRelatedPartyModel> Parties { get; set; } = [];
    public List<ClientRelatedPartyEvidenceChoice> EvidenceChoices { get; set; } = [];
    public List<string> Blockers { get; set; } = [];
}

public sealed class ClientEntityProfileEditModel
{
    public string? LegalForm { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? RegistrationCountry { get; set; }
    public DateOnly? EstablishmentDate { get; set; }
    public string? NatureOfBusinessOrPurpose { get; set; }
    public string OwnershipReviewStatus { get; set; } = ClientOwnershipReviewStatuses.Draft;
    public string? ControlConclusion { get; set; }
    public string? ControlConclusionReason { get; set; }
    public DateTime? OwnershipReviewedAtUtc { get; set; }
    public string? OwnershipReviewedBy { get; set; }
    public DateOnly? NextOwnershipReviewDate { get; set; }

    public static ClientEntityProfileEditModel FromEntity(ClientEntityProfile? profile) => profile is null ? new() : new()
    {
        LegalForm = profile.LegalForm,
        RegistrationNumber = profile.RegistrationNumber,
        RegistrationCountry = profile.RegistrationCountry,
        EstablishmentDate = profile.EstablishmentDate,
        NatureOfBusinessOrPurpose = profile.NatureOfBusinessOrPurpose,
        OwnershipReviewStatus = profile.OwnershipReviewStatus,
        ControlConclusion = profile.ControlConclusion,
        ControlConclusionReason = profile.ControlConclusionReason,
        OwnershipReviewedAtUtc = profile.OwnershipReviewedAtUtc,
        OwnershipReviewedBy = profile.OwnershipReviewedBy,
        NextOwnershipReviewDate = profile.NextOwnershipReviewDate
    };
}

public class ClientRelatedPartyEditModel
{
    public int? Id { get; set; }
    public string PartyType { get; set; } = ClientRelatedPartyTypes.NaturalPerson;
    public string DisplayName { get; set; } = "";
    public string? SouthAfricanIdNumber { get; set; }
    public string? PassportNumber { get; set; }
    public string? PassportCountry { get; set; }
    public string? RegistrationNumber { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? Nationality { get; set; }
    public string? CountryOfResidence { get; set; }
    public decimal? OwnershipPercent { get; set; }
    public string? ControlBasis { get; set; }
    public string? AuthorityBasis { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Notes { get; set; }
    public List<string> Roles { get; set; } = [];
}

public sealed class ClientRelatedPartyModel : ClientRelatedPartyEditModel
{
    public bool IsActive { get; set; }
    public List<ClientRelatedPartyEvidenceLinkModel> EvidenceLinks { get; set; } = [];

    public static ClientRelatedPartyModel FromEntity(ClientRelatedParty party) => new()
    {
        Id = party.Id,
        PartyType = party.PartyType,
        DisplayName = party.DisplayName,
        SouthAfricanIdNumber = party.SouthAfricanIdNumber,
        PassportNumber = party.PassportNumber,
        PassportCountry = party.PassportCountry,
        RegistrationNumber = party.RegistrationNumber,
        BirthDate = party.BirthDate,
        Nationality = party.Nationality,
        CountryOfResidence = party.CountryOfResidence,
        OwnershipPercent = party.OwnershipPercent,
        ControlBasis = party.ControlBasis,
        AuthorityBasis = party.AuthorityBasis,
        EffectiveFrom = party.EffectiveFrom,
        EffectiveTo = party.EffectiveTo,
        Notes = party.Notes,
        Roles = party.Roles.Select(role => role.RoleCode).OrderBy(role => role).ToList(),
        IsActive = party.IsActive,
        EvidenceLinks = party.EvidenceLinks.Select(link => new ClientRelatedPartyEvidenceLinkModel
        {
            Id = link.Id,
            Purpose = link.Purpose,
            EvidenceItemId = link.ClientEvidenceItemId,
            EvidenceTitle = link.EvidenceItem.Title,
            EvidenceType = link.EvidenceItem.EvidenceType,
            VerifiedDate = link.EvidenceItem.VerifiedDate
        }).ToList()
    };
}

public sealed class ClientRelatedPartyEvidenceLinkModel
{
    public int Id { get; set; }
    public int EvidenceItemId { get; set; }
    public string Purpose { get; set; } = "";
    public string EvidenceTitle { get; set; } = "";
    public string EvidenceType { get; set; } = "";
    public DateOnly? VerifiedDate { get; set; }
}

public sealed class ClientRelatedPartyEvidenceChoice
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string EvidenceType { get; set; } = "";
    public DateOnly? VerifiedDate { get; set; }
    public string SelectionStatus { get; set; } = "";
}
