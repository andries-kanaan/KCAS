using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientReviewTransferServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Completed_review_exports_previews_applies_once_and_rejects_duplicate_import()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientReviewTransferService>();
        var readinessService = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var compliance = scope.ServiceProvider.GetRequiredService<ComplianceService>();
        await readinessService.LoadDashboardAsync();
        var methodology = await db.RiskMethodologyVersions
            .Include(item => item.Factors).ThenInclude(item => item.Options)
            .Where(item =>
                item.Status == ComplianceStatuses.Review ||
                item.Status == ComplianceStatuses.Approved ||
                item.Status == ComplianceStatuses.Active)
            .OrderByDescending(item => item.Id)
            .FirstOrDefaultAsync();
        if (methodology is null)
        {
            var methodologyId = await compliance.CreateKanaanStarterMethodologyAsync(
                "reviewer@example.test",
                "Create transfer test methodology.");
            await compliance.SubmitMethodologyAsync(
                methodologyId,
                "reviewer@example.test",
                "Make transfer test methodology available.");
            methodology = await db.RiskMethodologyVersions
                .Include(item => item.Factors).ThenInclude(item => item.Options)
                .SingleAsync(item => item.Id == methodologyId);
        }
        var today = DateOnly.FromDateTime(DateTime.Today);
        var applicableRequirements = await db.ClientEvidenceRequirements
            .Where(item => item.Status == ClientEvidenceRequirementStatuses.Active &&
                (item.ClientCategory == "All" || item.ClientCategory == ClientCategories.NaturalPerson))
            .ToListAsync();
        var client = new Client
        {
            LegacyClientId = 99123,
            KanaanId = "TRANSFER-99123",
            DisplayName = "Transfer Pilot",
            SurnameOrEntityName = "Transfer / Pilot: Unsafe?",
            ClientCategory = ClientCategories.NaturalPerson,
            LifecycleStatus = ClientLifecycleStatuses.Current,
            LifecycleReason = "Current relationship confirmed for transfer test.",
            LifecycleReviewedAtUtc = DateTime.UtcNow,
            LifecycleReviewedBy = "reviewer@example.test",
            IsActive = true
        };
        var evidence = new ClientEvidenceItem
        {
            Client = client,
            ClientEvidenceRequirementId = applicableRequirements.Single(item => item.EvidenceType == "Identity").Id,
            EvidenceType = "Identity",
            Title = "Verified identity",
            FileName = "identity.pdf",
            FileSha256 = new string('a', 64),
            VerifiedDate = today,
            Reviewer = "reviewer@example.test",
            Status = ClientEvidenceStatuses.Verified,
            OwnershipStatus = ClientEvidenceOwnershipStatuses.Confirmed,
            SelectionStatus = ClientEvidenceSelectionStatuses.Current
        };
        client.EvidenceItems.Add(evidence);
        foreach (var requirement in applicableRequirements.Where(item => item.EvidenceType != "Identity"))
        {
            client.EvidenceExceptions.Add(new ClientEvidenceException
            {
                Requirement = requirement,
                Reason = $"Transfer test exception for {requirement.EvidenceType}.",
                ApprovedBy = "reviewer@example.test",
                ReviewDate = today.AddYears(3)
            });
        }
        var assessment = new ClientRiskAssessment
        {
            Client = client,
            MethodologyVersion = methodology,
            Status = ClientRiskAssessmentStatuses.Finalised,
            CalculatedScore = methodology.Factors.Sum(factor => factor.Options.First().Score),
            CalculatedRating = "Standard",
            FinalRating = "Standard",
            StandardControlsApplied = true,
            Narrative = "Completed transfer test assessment.",
            EffectiveDate = today,
            NextReviewDate = today.AddYears(3),
            PreparedBy = "reviewer@example.test",
            FinalisedBy = "reviewer@example.test",
            FinalisedAtUtc = DateTime.UtcNow,
            Responses = methodology.Factors.Select(factor =>
            {
                var option = factor.Options.OrderBy(item => item.SortOrder).First();
                return new ClientRiskAssessmentResponse
                {
                    FactorDefinition = factor,
                    SelectedOption = option,
                    EvidenceItem = evidence,
                    Score = option.Score,
                    WeightedScore = option.Score * factor.Weight,
                    Explanation = $"Confirmed {factor.Name}.",
                    ConfirmedAtUtc = DateTime.UtcNow,
                    ConfirmedBy = "reviewer@example.test"
                };
            }).ToList()
        };
        client.RiskAssessments.Add(assessment);
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        const string passphrase = "transfer-test-passphrase";
        var export = await service.ExportAsync(
            client.Id, passphrase, "reviewer@example.test", "Prepare live transfer test.");
        Assert.Matches(
            @"^KCAS-review-C\d+-Transfer-Pilot-Unsafe-\d{8}-[a-f0-9]{12}\.kcas-review$",
            export.FileName);
        var encrypted = await File.ReadAllBytesAsync(export.StoragePath);

        db.ClientRiskAssessments.Remove(assessment);
        db.ClientEvidenceItems.Remove(evidence);
        db.ClientEvidenceExceptions.RemoveRange(client.EvidenceExceptions);
        client.LifecycleStatus = ClientLifecycleStatuses.Unreviewed;
        client.LifecycleReason = null;
        client.LifecycleReviewedAtUtc = null;
        client.LifecycleReviewedBy = null;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var preview = await service.PreviewAsync(encrypted, passphrase);
        Assert.True(preview.CanApply);
        Assert.Equal(client.Id, preview.TargetClientId);
        Assert.Equal(1, preview.NewEvidenceCount);

        var imported = await service.ApplyAsync(
            encrypted,
            passphrase,
            "live-importer@example.test",
            "Approved after matching the client and methodology.");
        Assert.Equal(client.Id, imported.ClientId);

        var restoredClient = await db.Clients.AsNoTracking().SingleAsync(item => item.Id == client.Id);
        Assert.Equal(ClientLifecycleStatuses.Current, restoredClient.LifecycleStatus);
        Assert.Single(await db.ClientEvidenceItems.AsNoTracking()
            .Where(item => item.ClientId == client.Id).ToListAsync());
        Assert.Equal(applicableRequirements.Count - 1, await db.ClientEvidenceExceptions.AsNoTracking()
            .CountAsync(item => item.ClientId == client.Id));
        Assert.Single(await db.ClientRiskAssessments.AsNoTracking()
            .Where(item => item.ClientId == client.Id).ToListAsync());
        Assert.Contains(await db.ComplianceAuditEvents.AsNoTracking().ToListAsync(), item =>
            item.Action == "ClientReviewPackageApplied" &&
            item.UserName == "live-importer@example.test");

        var duplicatePreview = await service.PreviewAsync(encrypted, passphrase);
        Assert.True(duplicatePreview.AlreadyApplied);
        Assert.False(duplicatePreview.CanApply);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApplyAsync(
            encrypted,
            passphrase,
            "live-importer@example.test",
            "Attempt duplicate."));
    }

    [Fact]
    public async Task Shared_kanaan_id_import_restores_trust_ownership_and_rejects_current_value_for_surrendered_account()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientReviewTransferService>();
        var readinessService = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        var investmentService = new InvestmentReconciliationService(db);
        await readinessService.LoadDashboardAsync();
        var methodology = await db.RiskMethodologyVersions
            .Include(item => item.Factors).ThenInclude(item => item.Options)
            .Where(item => item.Status == ComplianceStatuses.Review ||
                           item.Status == ComplianceStatuses.Approved ||
                           item.Status == ComplianceStatuses.Active)
            .OrderByDescending(item => item.Id)
            .FirstAsync();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var nextReview = today.AddYears(3);
        var requirements = await db.ClientEvidenceRequirements
            .Where(item => item.Status == ClientEvidenceRequirementStatuses.Active &&
                (item.ClientCategory == "All" || item.ClientCategory == ClientCategories.Trust))
            .ToListAsync();

        var client = new Client
        {
            LegacyClientId = 99200,
            KanaanId = "SHARED-TRANSFER",
            DisplayName = "Shared Transfer Trust",
            SurnameOrEntityName = "Shared Transfer Trust",
            ClientCategory = ClientCategories.Trust,
            ClientCategorySource = ClientCategorySources.Manual,
            ClientCategoryReason = "Trust deed verified.",
            LifecycleStatus = ClientLifecycleStatuses.Current,
            LifecycleReason = "Ongoing trust relationship.",
            LifecycleReviewedAtUtc = DateTime.UtcNow,
            LifecycleReviewedBy = "reviewer@example.test",
            IsActive = true
        };
        client.EntityProfile = new ClientEntityProfile
        {
            Client = client,
            LegalForm = "Inter vivos family trust",
            RegistrationNumber = "IT-TRANSFER/2026",
            RegistrationCountry = "South Africa",
            NatureOfBusinessOrPurpose = "Hold family assets.",
            OwnershipReviewStatus = ClientOwnershipReviewStatuses.Complete,
            ControlConclusion = ClientControlConclusions.NaturalPersonsIdentified,
            ControlConclusionReason = "Founder, trustee, beneficiary and controller identified.",
            OwnershipReviewedAtUtc = DateTime.UtcNow,
            OwnershipReviewedBy = "reviewer@example.test",
            NextOwnershipReviewDate = nextReview
        };
        var party = new ClientRelatedParty
        {
            Client = client,
            PartyType = ClientRelatedPartyTypes.NaturalPerson,
            DisplayName = "Transfer Trustee",
            SouthAfricanIdNumber = "8001015009087",
            BirthDate = new DateOnly(1980, 1, 1),
            Nationality = "South African",
            CountryOfResidence = "South Africa",
            ControlBasis = "Named founder, beneficial owner and controlling trustee.",
            AuthorityBasis = "Signed trust deed and trustee resolution.",
            EffectiveFrom = today,
            IsActive = true
        };
        foreach (var role in new[]
        {
            ClientRelatedPartyRoles.Founder,
            ClientRelatedPartyRoles.Trustee,
            ClientRelatedPartyRoles.Beneficiary,
            ClientRelatedPartyRoles.BeneficialOwner,
            ClientRelatedPartyRoles.Controller
        })
        {
            party.Roles.Add(new ClientRelatedPartyRole { RoleCode = role });
        }
        client.RelatedParties.Add(party);

        var identity = new ClientEvidenceItem
        {
            Client = client,
            ClientEvidenceRequirementId = requirements.First(item => item.EvidenceType == "Identity").Id,
            EvidenceType = "Identity",
            Title = "Trustee identity and authority",
            FileName = "trustee-identity.pdf",
            FileSha256 = new string('b', 64),
            VerifiedDate = today,
            Reviewer = "reviewer@example.test",
            Status = ClientEvidenceStatuses.Verified,
            OwnershipStatus = ClientEvidenceOwnershipStatuses.Confirmed,
            SelectionStatus = ClientEvidenceSelectionStatuses.Current
        };
        client.EvidenceItems.Add(identity);
        foreach (var purpose in new[]
        {
            ClientRelatedPartyEvidencePurposes.Identity,
            ClientRelatedPartyEvidencePurposes.RoleAuthority,
            ClientRelatedPartyEvidencePurposes.OwnershipControl
        })
        {
            party.EvidenceLinks.Add(new ClientRelatedPartyEvidenceLink
            {
                RelatedParty = party,
                EvidenceItem = identity,
                Purpose = purpose,
                LinkedBy = "reviewer@example.test"
            });
        }
        foreach (var evidenceType in new[] { "PepPip", "SanctionsTfs" })
        {
            client.EvidenceItems.Add(new ClientEvidenceItem
            {
                Client = client,
                RelatedParty = party,
                ClientEvidenceRequirementId = requirements.First(item => item.EvidenceType == evidenceType).Id,
                EvidenceType = evidenceType,
                Title = $"{evidenceType}: Transfer Trustee",
                VerifiedDate = today,
                ScreeningReviewDate = today,
                ScreeningSubjectType = ClientEvidenceScreeningSubjectTypes.Trustee,
                ScreeningSubjectName = party.DisplayName,
                ScreeningOutcome = ClientEvidenceScreeningOutcomes.NoMatch,
                ScreeningRiskSignal = ClientEvidenceRiskSignals.Low,
                Reviewer = "reviewer@example.test",
                Status = ClientEvidenceStatuses.Verified,
                OwnershipStatus = ClientEvidenceOwnershipStatuses.Confirmed,
                SelectionStatus = ClientEvidenceSelectionStatuses.Candidate
            });
        }
        foreach (var requirement in requirements)
        {
            client.EvidenceExceptions.Add(new ClientEvidenceException
            {
                Requirement = requirement,
                Reason = $"Transfer package test evidence rationale for {requirement.EvidenceType}.",
                ApprovedBy = "reviewer@example.test",
                ReviewDate = nextReview
            });
        }

        var account = new ClientInvestmentAccount
        {
            Client = client,
            LegacyInvestmentAccountId = 8801,
            LegacyClientId = client.LegacyClientId,
            AccountNumber = "TRUST-CLOSED-1",
            Administrator = "Test Administrator",
            InvestmentDate = new DateOnly(2018, 1, 1)
        };
        account.Transactions.Add(new ClientInvestmentTransaction
        {
            InvestmentAccount = account,
            LegacyInvestmentHistoryId = 9901,
            LegacyInvestmentAccountId = account.LegacyInvestmentAccountId,
            TransactionDate = new DateOnly(2022, 1, 11),
            Description = "Full surrender",
            WithdrawalAmountZar = 100_000m
        });
        client.InvestmentAccounts.Add(account);
        db.Clients.Add(client);
        db.Clients.Add(new Client
        {
            LegacyClientId = 99201,
            KanaanId = client.KanaanId,
            DisplayName = "Linked household client",
            SurnameOrEntityName = "Linked household client",
            LifecycleStatus = ClientLifecycleStatuses.Unreviewed
        });
        await db.SaveChangesAsync();

        await investmentService.ReviewAccountAsync(client.Id, account.Id, new ClientInvestmentReconciliationReviewRequest
        {
            Outcome = ClientInvestmentReconciliationOutcomes.HistoricalSurrendered,
            SurrenderDate = new DateOnly(2022, 1, 11),
            EvidenceReference = "Signed full-surrender instruction.",
            Reason = "No current value remains and the effective surrender date is verified."
        }, "reviewer@example.test");

        var assessment = new ClientRiskAssessment
        {
            ClientId = client.Id,
            MethodologyVersion = methodology,
            Status = ClientRiskAssessmentStatuses.Finalised,
            CalculatedScore = methodology.Factors.Sum(factor => factor.Options.First().Score),
            CalculatedRating = "Standard",
            FinalRating = "Standard",
            StandardControlsApplied = true,
            Narrative = "Completed trust transfer assessment.",
            EffectiveDate = today,
            NextReviewDate = nextReview,
            PreparedBy = "reviewer@example.test",
            FinalisedBy = "reviewer@example.test",
            FinalisedAtUtc = DateTime.UtcNow,
            Responses = methodology.Factors.Select(factor =>
            {
                var option = factor.Options.OrderBy(item => item.SortOrder).First();
                return new ClientRiskAssessmentResponse
                {
                    FactorDefinition = factor,
                    SelectedOption = option,
                    EvidenceItem = identity,
                    Score = option.Score,
                    WeightedScore = option.Score * factor.Weight,
                    Explanation = $"Confirmed {factor.Name}.",
                    ConfirmedAtUtc = DateTime.UtcNow,
                    ConfirmedBy = "reviewer@example.test"
                };
            }).ToList()
        };
        db.ClientRiskAssessments.Add(assessment);
        await db.SaveChangesAsync();

        const string passphrase = "shared-trust-transfer-passphrase";
        var export = await service.ExportAsync(client.Id, passphrase, "reviewer@example.test", "Export completed trust review.");
        var encrypted = await File.ReadAllBytesAsync(export.StoragePath);

        db.ClientRiskAssessments.Remove(assessment);
        db.ClientRelatedPartyEvidenceLinks.RemoveRange(await db.ClientRelatedPartyEvidenceLinks
            .Where(link => link.RelatedParty.ClientId == client.Id).ToListAsync());
        db.ClientEvidenceItems.RemoveRange(await db.ClientEvidenceItems.Where(item => item.ClientId == client.Id).ToListAsync());
        db.ClientEvidenceExceptions.RemoveRange(await db.ClientEvidenceExceptions.Where(item => item.ClientId == client.Id).ToListAsync());
        db.ClientRelatedPartyRoles.RemoveRange(await db.ClientRelatedPartyRoles.Where(role => role.RelatedParty.ClientId == client.Id).ToListAsync());
        db.ClientRelatedParties.RemoveRange(await db.ClientRelatedParties.Where(item => item.ClientId == client.Id).ToListAsync());
        db.ClientEntityProfiles.RemoveRange(await db.ClientEntityProfiles.Where(item => item.ClientId == client.Id).ToListAsync());
        db.ClientInvestmentReconciliationReviews.RemoveRange(await db.ClientInvestmentReconciliationReviews.Where(item => item.ClientId == client.Id).ToListAsync());
        account.SurrenderDate = null;
        client.ClientCategory = ClientCategories.LegalPerson;
        client.ClientCategorySource = ClientCategorySources.EvidenceScanInferred;
        client.LifecycleStatus = ClientLifecycleStatuses.Unreviewed;
        client.LifecycleReason = null;
        client.LifecycleReviewedAtUtc = null;
        client.LifecycleReviewedBy = null;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var conflictingValuation = new ClientFundValuation
        {
            ClientId = client.Id,
            LegacyFundId = 77101,
            LegacyClientId = client.LegacyClientId,
            KanaanId = client.KanaanId,
            FundName = "Still current fund",
            Administrator = account.Administrator,
            InvestmentUniqueNumber = account.AccountNumber,
            AmountZar = 25_000m,
            ValuationDate = today
        };
        db.ClientFundValuations.Add(conflictingValuation);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var rejectedPreview = await service.PreviewAsync(encrypted, passphrase);
        Assert.Equal(client.Id, rejectedPreview.TargetClientId);
        Assert.False(rejectedPreview.CanApply);
        Assert.Contains(rejectedPreview.Conflicts, message =>
            message.Contains("current valuation", StringComparison.OrdinalIgnoreCase));

        db.ClientFundValuations.Remove(await db.ClientFundValuations.SingleAsync(item => item.Id == conflictingValuation.Id));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var preview = await service.PreviewAsync(encrypted, passphrase);
        Assert.True(preview.CanApply, string.Join(Environment.NewLine, preview.Conflicts));
        Assert.Equal(client.Id, preview.TargetClientId);
        Assert.Single(preview.Package.InvestmentReconciliations);
        Assert.Single(preview.Package.RelatedParties);

        await service.ApplyAsync(encrypted, passphrase, "live-importer@example.test", "Approved trust transfer.");

        var restored = await db.Clients.AsNoTracking()
            .Include(item => item.EntityProfile)
            .Include(item => item.RelatedParties).ThenInclude(item => item.Roles)
            .Include(item => item.RelatedParties).ThenInclude(item => item.EvidenceLinks)
            .SingleAsync(item => item.Id == client.Id);
        Assert.Equal(ClientCategories.Trust, restored.ClientCategory);
        Assert.Equal(ClientOwnershipReviewStatuses.Complete, restored.EntityProfile!.OwnershipReviewStatus);
        var restoredParty = Assert.Single(restored.RelatedParties);
        Assert.Contains(restoredParty.Roles, role => role.RoleCode == ClientRelatedPartyRoles.Controller);
        Assert.Equal(3, restoredParty.EvidenceLinks.Count);
        Assert.Equal(2, await db.ClientEvidenceItems.AsNoTracking().CountAsync(item =>
            item.ClientId == client.Id && item.ClientRelatedPartyId == restoredParty.Id &&
            (item.EvidenceType == "PepPip" || item.EvidenceType == "SanctionsTfs")));
        Assert.Equal(new DateOnly(2022, 1, 11), await db.ClientInvestmentAccounts.AsNoTracking()
            .Where(item => item.Id == account.Id).Select(item => item.SurrenderDate).SingleAsync());
        Assert.True((await investmentService.LoadClientReviewAsync(client.Id)).IsComplete);
        Assert.True((await new ClientEvidenceReadinessService(db).LoadClientReadinessAsync(client.Id)).IsReadyForRiskAssessment);
    }

    [Fact]
    public async Task Preview_rejects_wrong_passphrase()
    {
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ClientReviewTransferService>();

        var exception = await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(
            () => service.PreviewAsync([1, 2, 3, 4], "wrong-passphrase-long"));

        Assert.Contains("package", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
