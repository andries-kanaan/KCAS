using System.ComponentModel.DataAnnotations;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientEntityOwnershipServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Owner_or_controller_requires_control_basis()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEntityOwnershipService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Control Basis Trust", ClientCategories.Trust);

        await Assert.ThrowsAsync<ValidationException>(() => service.SavePartyAsync(clientId, new ClientRelatedPartyEditModel
        {
            DisplayName = "Trustee Controller",
            Roles = [ClientRelatedPartyRoles.Trustee, ClientRelatedPartyRoles.Controller],
            AuthorityBasis = "Trust deed."
        }, "reviewer@example.test", "Record trust controller."));
    }

    [Fact]
    public async Task Evidence_link_must_belong_to_the_same_client()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientEntityOwnershipService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Evidence Link Trust", ClientCategories.Trust);
        var otherClientId = await CreateClientAsync(db, "Other Evidence Client", ClientCategories.NaturalPerson);
        var partyId = await service.SavePartyAsync(clientId, ValidTrustParty(), "reviewer@example.test", "Record trust party.");
        var evidence = new ClientEvidenceItem
        {
            ClientId = otherClientId,
            EvidenceType = "Identity",
            Title = "Other client's identity",
            Status = ClientEvidenceStatuses.Verified,
            VerifiedDate = DateOnly.FromDateTime(DateTime.Today)
        };
        db.ClientEvidenceItems.Add(evidence);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<ValidationException>(() => service.LinkEvidenceAsync(
            clientId, partyId, evidence.Id, ClientRelatedPartyEvidencePurposes.Identity,
            "reviewer@example.test", "Link identity evidence."));
    }

    [Fact]
    public async Task Related_parties_feed_screening_subjects_and_readiness_blockers()
    {
        using var scope = factory.Services.CreateScope();
        var ownershipService = scope.ServiceProvider.GetRequiredService<ClientEntityOwnershipService>();
        var evidenceService = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Screened Trust", ClientCategories.Trust);
        await ownershipService.SaveProfileAsync(clientId, new ClientEntityProfileEditModel
        {
            LegalForm = "Trust",
            RegistrationNumber = "IT100/2026",
            RegistrationCountry = "South Africa",
            NatureOfBusinessOrPurpose = "Test trust."
        }, "reviewer@example.test", "Record trust profile.");
        var partyId = await ownershipService.SavePartyAsync(clientId, ValidTrustParty(), "reviewer@example.test", "Record trust party.");

        var readiness = await evidenceService.LoadClientReadinessAsync(clientId);

        Assert.Contains(readiness.ScreeningSubjects, subject =>
            subject.ClientRelatedPartyId == partyId &&
            subject.SubjectName == "Primary Trustee" &&
            subject.SubjectType == ClientEvidenceScreeningSubjectTypes.Trustee);
        Assert.Contains(readiness.OwnershipBlockers, blocker => blocker.Contains("verified identity evidence"));
        Assert.False(readiness.IsReadyForRiskAssessment);
    }

    [Fact]
    public async Task Complete_review_requires_verified_party_evidence_and_screening_and_is_audited()
    {
        using var scope = factory.Services.CreateScope();
        var ownershipService = scope.ServiceProvider.GetRequiredService<ClientEntityOwnershipService>();
        var evidenceService = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clientId = await CreateClientAsync(db, "Complete Ownership Trust", ClientCategories.Trust);

        await ownershipService.SaveProfileAsync(clientId, new ClientEntityProfileEditModel
        {
            LegalForm = "Trust",
            RegistrationNumber = "IT123/2026",
            RegistrationCountry = "South Africa",
            NatureOfBusinessOrPurpose = "Family investment trust."
        }, "reviewer@example.test", "Record trust profile.");
        var partyId = await ownershipService.SavePartyAsync(clientId, ValidTrustParty(), "reviewer@example.test", "Record trust ownership roles.");

        var evidence = new ClientEvidenceItem
        {
            ClientId = clientId,
            EvidenceType = "BeneficialOwnership",
            Title = "Verified trust deed and identity pack",
            Status = ClientEvidenceStatuses.Verified,
            VerifiedDate = DateOnly.FromDateTime(DateTime.Today),
            ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1))
        };
        db.ClientEvidenceItems.Add(evidence);
        await db.SaveChangesAsync();
        foreach (var purpose in new[]
        {
            ClientRelatedPartyEvidencePurposes.Identity,
            ClientRelatedPartyEvidencePurposes.RoleAuthority,
            ClientRelatedPartyEvidencePurposes.OwnershipControl
        })
        {
            await ownershipService.LinkEvidenceAsync(clientId, partyId, evidence.Id, purpose, "reviewer@example.test", $"Link {purpose} evidence.");
        }

        var readiness = await evidenceService.LoadClientReadinessAsync(clientId);
        foreach (var evidenceType in new[] { "PepPip", "SanctionsTfs" })
        {
            var requirement = readiness.Requirements.Single(item => item.EvidenceType == evidenceType);
            await evidenceService.RecordRequirementReviewAsync(clientId, requirement.RequirementId, new ClientEvidenceScreeningReviewRequest
            {
                ClientRelatedPartyId = partyId,
                Outcome = ClientEvidenceScreeningOutcomes.ForEvidenceType(evidenceType)[0],
                RiskSignal = ClientEvidenceRiskSignals.Low,
                ReviewDate = DateOnly.FromDateTime(DateTime.Today),
                Notes = "Reviewed the related party against the applicable screening source."
            }, "reviewer@example.test", $"Complete {evidenceType} screening.");
        }

        await ownershipService.CompleteOwnershipReviewAsync(
            clientId,
            ClientControlConclusions.NaturalPersonsIdentified,
            "The natural person exercising effective control was identified and verified.",
            DateOnly.FromDateTime(DateTime.Today.AddYears(1)),
            "approver@example.test",
            "Approve the ownership and control conclusion.");

        var model = await ownershipService.LoadAsync(clientId);
        Assert.Equal(ClientOwnershipReviewStatuses.Complete, model.Profile.OwnershipReviewStatus);
        Assert.DoesNotContain(model.Blockers, blocker => blocker.Contains("Ownership/control review"));
        Assert.True(await db.ComplianceAuditEvents.AnyAsync(audit =>
            audit.EntityType == nameof(ClientEntityProfile) &&
            audit.Action == "CompleteOwnershipReview"));
    }

    private static ClientRelatedPartyEditModel ValidTrustParty() => new()
    {
        PartyType = ClientRelatedPartyTypes.NaturalPerson,
        DisplayName = "Primary Trustee",
        SouthAfricanIdNumber = "8001015009087",
        ControlBasis = "Exercises effective control under the trust deed.",
        AuthorityBasis = "Appointed in the trust deed and letters of authority.",
        Roles =
        [
            ClientRelatedPartyRoles.Founder,
            ClientRelatedPartyRoles.Trustee,
            ClientRelatedPartyRoles.Beneficiary,
            ClientRelatedPartyRoles.Controller
        ]
    };

    private static async Task<int> CreateClientAsync(ApplicationDbContext db, string displayName, string category)
    {
        var client = new Client
        {
            DisplayName = displayName,
            FullName = displayName,
            SurnameOrEntityName = displayName,
            ClientCategory = category,
            ClientCategorySource = ClientCategorySources.Manual,
            ClientCategoryReason = "Test category."
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client.Id;
    }
}
