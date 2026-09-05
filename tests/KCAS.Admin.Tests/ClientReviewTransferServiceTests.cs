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
            ClientFolder = @"C:\Download\_kanaan\ClientsKanaan\TRANSFER PILOT",
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
        var sharedDocumentRequirement = applicableRequirements.First(item => item.EvidenceType != "Identity");
        var sharedDocumentEvidence = new ClientEvidenceItem
        {
            Client = client,
            ClientEvidenceRequirementId = sharedDocumentRequirement.Id,
            EvidenceType = sharedDocumentRequirement.EvidenceType,
            Title = $"Verified {sharedDocumentRequirement.Title}",
            FileName = "identity.pdf",
            FileSha256 = evidence.FileSha256,
            VerifiedDate = today,
            Reviewer = "reviewer@example.test",
            Status = ClientEvidenceStatuses.Verified,
            OwnershipStatus = ClientEvidenceOwnershipStatuses.Confirmed,
            SelectionStatus = ClientEvidenceSelectionStatuses.Current
        };
        client.EvidenceItems.Add(sharedDocumentEvidence);
        foreach (var requirement in applicableRequirements.Where(item =>
                     item.EvidenceType != "Identity" && item.Id != sharedDocumentRequirement.Id))
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
        db.ClientEvidenceScanRoots.Add(new ClientEvidenceScanRoot
        {
            RootPath = @"E:\Userdata\Kanaan Trust\Clients",
            IsActive = true,
            UpdatedBy = "reviewer@example.test"
        });
        await db.SaveChangesAsync();

        const string passphrase = "transfer-test-passphrase";
        var export = await service.ExportAsync(
            client.Id, passphrase, "reviewer@example.test", "Prepare live transfer test.");
        Assert.Matches(
            @"^KCAS-review-C\d+-Transfer-Pilot-Unsafe-\d{8}-[a-f0-9]{12}\.kcas-review$",
            export.FileName);
        var encrypted = await File.ReadAllBytesAsync(export.StoragePath);

        db.ClientRiskAssessments.Remove(assessment);
        db.ClientEvidenceItems.RemoveRange(evidence, sharedDocumentEvidence);
        db.ClientEvidenceExceptions.RemoveRange(client.EvidenceExceptions);
        client.LifecycleStatus = ClientLifecycleStatuses.Unreviewed;
        client.LifecycleReason = null;
        client.LifecycleReviewedAtUtc = null;
        client.LifecycleReviewedBy = null;
        client.ClientFolder = @"Z:\Userdata\Kanaan Trust\Clients\OLD TRANSFER PILOT";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var preview = await service.PreviewAsync(encrypted, passphrase);
        Assert.True(preview.CanApply);
        Assert.Equal(client.Id, preview.TargetClientId);
        Assert.Equal(2, preview.NewEvidenceCount);
        Assert.Equal(@"E:\Userdata\Kanaan Trust\Clients\TRANSFER PILOT", preview.TargetClientFolder);
        Assert.Contains(preview.Warnings, warning =>
            warning.Contains("Client folder will map", StringComparison.OrdinalIgnoreCase));

        var imported = await service.ApplyAsync(
            encrypted,
            passphrase,
            "live-importer@example.test",
            "Approved after matching the client and methodology.");
        Assert.Equal(client.Id, imported.ClientId);

        var restoredClient = await db.Clients.AsNoTracking().SingleAsync(item => item.Id == client.Id);
        Assert.Equal(ClientLifecycleStatuses.Current, restoredClient.LifecycleStatus);
        Assert.Equal(@"E:\Userdata\Kanaan Trust\Clients\TRANSFER PILOT", restoredClient.ClientFolder);
        Assert.Equal(2, await db.ClientEvidenceItems.AsNoTracking()
            .CountAsync(item => item.ClientId == client.Id));
        Assert.Equal(applicableRequirements.Count - 2, await db.ClientEvidenceExceptions.AsNoTracking()
            .CountAsync(item => item.ClientId == client.Id));
        Assert.Single(await db.ClientRiskAssessments.AsNoTracking()
            .Where(item => item.ClientId == client.Id).ToListAsync());
        Assert.Contains(await db.ComplianceAuditEvents.AsNoTracking().ToListAsync(), item =>
            item.Action == "ClientReviewPackageApplied" &&
            item.UserName == "live-importer@example.test");

        var priorIncomingRecord = await db.ClientReviewTransferRecords.SingleAsync(item =>
            item.Direction == ClientReviewTransferDirections.Incoming &&
            item.PackageId == imported.PackageId);
        var legacySummary = System.Text.Json.JsonSerializer.Deserialize<ClientReviewTransferPackageSummary>(
            priorIncomingRecord.SummaryJson,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        legacySummary.ImportedAssessmentId = null;
        legacySummary.SupersededAssessmentId = null;
        legacySummary.AssessmentFingerprint = null;
        priorIncomingRecord.SummaryJson = System.Text.Json.JsonSerializer.Serialize(
            legacySummary,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        await db.SaveChangesAsync();

        var newerExport = await service.ExportAsync(
            client.Id, passphrase, "reviewer@example.test", "Export a more complete reviewed package.");
        var newerEncrypted = await File.ReadAllBytesAsync(newerExport.StoragePath);
        var reconciliationPreview = await service.PreviewAsync(newerEncrypted, passphrase);
        Assert.True(reconciliationPreview.CanApply);
        Assert.Equal(imported.AssessmentId, reconciliationPreview.SupersededAssessmentId);
        Assert.Contains(reconciliationPreview.Warnings, warning =>
            warning.Contains("superseded", StringComparison.OrdinalIgnoreCase));

        var reconciled = await service.ApplyAsync(
            newerEncrypted,
            passphrase,
            "live-importer@example.test",
            "Reconcile a newer completed review package.");
        var assessments = await db.ClientRiskAssessments.AsNoTracking()
            .Where(item => item.ClientId == client.Id)
            .OrderBy(item => item.Id)
            .ToListAsync();
        Assert.Equal(2, assessments.Count);
        Assert.Equal(ClientRiskAssessmentStatuses.Superseded, assessments[0].Status);
        Assert.Equal(assessments[0].Id, assessments[1].PreviousAssessmentId);
        Assert.Equal(reconciled.AssessmentId, assessments[1].Id);

        var liveEditedAssessment = await db.ClientRiskAssessments.SingleAsync(item =>
            item.Id == reconciled.AssessmentId);
        liveEditedAssessment.Narrative = "Edited independently on live after the import.";
        liveEditedAssessment.UpdatedAtUtc = DateTime.UtcNow.AddMinutes(1);
        await db.SaveChangesAsync();
        var postEditExport = await service.ExportAsync(
            client.Id, passphrase, "reviewer@example.test", "Attempt transfer after a live edit.");
        var postEditPreview = await service.PreviewAsync(
            await File.ReadAllBytesAsync(postEditExport.StoragePath), passphrase);
        Assert.False(postEditPreview.CanApply);
        Assert.Contains(postEditPreview.Conflicts, conflict =>
            conflict.Contains("changed on live", StringComparison.OrdinalIgnoreCase));

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
    public async Task Family_bundle_exports_linked_clients_and_imports_each_member_independently()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var familyTransfers = scope.ServiceProvider.GetRequiredService<ClientReviewFamilyTransferService>();
        var readiness = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        await readiness.LoadDashboardAsync();
        var methodology = await db.RiskMethodologyVersions
            .Include(item => item.Factors).ThenInclude(item => item.Options)
            .Where(item => item.Status == ComplianceStatuses.Review ||
                           item.Status == ComplianceStatuses.Approved ||
                           item.Status == ComplianceStatuses.Active)
            .OrderByDescending(item => item.Id)
            .FirstAsync();
        var requirements = await db.ClientEvidenceRequirements
            .Where(item => item.Status == ClientEvidenceRequirementStatuses.Active &&
                (item.ClientCategory == "All" || item.ClientCategory == ClientCategories.NaturalPerson))
            .ToListAsync();
        var familyId = $"FAMILY-{Guid.NewGuid():N}"[..30];
        var legacySeed = Random.Shared.Next(2_000_000, 2_100_000);
        var first = ReviewedNaturalPerson(
            legacySeed, familyId, "Family Transfer One", 'c', methodology, requirements);
        var second = ReviewedNaturalPerson(
            legacySeed + 1, familyId, "Family Transfer Two", 'd', methodology, requirements);
        var excluded = new Client
        {
            LegacyClientId = legacySeed + 2,
            KanaanId = familyId,
            DisplayName = "Family Transfer Pending",
            SurnameOrEntityName = "Family Transfer Pending",
            ClientCategory = ClientCategories.NaturalPerson,
            LifecycleStatus = ClientLifecycleStatuses.Unreviewed
        };
        db.Clients.AddRange(first, second, excluded);
        await db.SaveChangesAsync();

        var family = await familyTransfers.LoadFamilyAsync(first.Id);
        Assert.NotNull(family);
        Assert.Equal(3, family.Members.Count);
        Assert.Equal(2, family.Members.Count(member => member.HasCompletedAssessment));

        const string passphrase = "family-transfer-passphrase";
        var exported = await familyTransfers.ExportAsync(
            first.Id, passphrase, "reviewer@example.test", "Transfer reviewed household together.");
        Assert.Equal(2, exported.MemberCount);
        Assert.Equal(1, exported.ExcludedMemberCount);
        Assert.EndsWith(".kcas-family-review", exported.FileName);
        var encrypted = await File.ReadAllBytesAsync(exported.StoragePath);
        Assert.True(ClientReviewFamilyTransferService.IsFamilyBundle(encrypted));

        var clientIds = new[] { first.Id, second.Id };
        db.ClientRiskAssessments.RemoveRange(await db.ClientRiskAssessments
            .Where(item => clientIds.Contains(item.ClientId)).ToListAsync());
        db.ClientEvidenceItems.RemoveRange(await db.ClientEvidenceItems
            .Where(item => clientIds.Contains(item.ClientId)).ToListAsync());
        db.ClientEvidenceExceptions.RemoveRange(await db.ClientEvidenceExceptions
            .Where(item => clientIds.Contains(item.ClientId)).ToListAsync());
        first.LifecycleStatus = ClientLifecycleStatuses.Unreviewed;
        second.LifecycleStatus = ClientLifecycleStatuses.Unreviewed;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var preview = await familyTransfers.PreviewAsync(encrypted, passphrase);
        Assert.True(preview.CanApply);
        Assert.Equal(2, preview.Members.Count);
        Assert.All(preview.Members, member => Assert.True(member.CanApply));
        Assert.Equal(clientIds.Order(), preview.Members
            .Select(member => member.ClientPreview!.TargetClientId!.Value).Order());
        var firstPackageId = preview.Members.Single(member =>
            member.ClientPreview!.TargetClientId == first.Id).Manifest.PackageId;
        var secondPackageId = preview.Members.Single(member =>
            member.ClientPreview!.TargetClientId == second.Id).Manifest.PackageId;

        var firstImport = await familyTransfers.ApplyAsync(
            encrypted, passphrase, "live-importer@example.test", "Approve first household member.",
            [firstPackageId]);
        Assert.Single(firstImport.Members);
        Assert.Equal("Applied", firstImport.Members[0].Status);
        Assert.True(await db.ClientRiskAssessments.AnyAsync(item => item.ClientId == first.Id));
        Assert.False(await db.ClientRiskAssessments.AnyAsync(item => item.ClientId == second.Id));

        var resumedPreview = await familyTransfers.PreviewAsync(encrypted, passphrase);
        Assert.True(resumedPreview.CanApply);
        Assert.True(resumedPreview.Members.Single(member =>
            member.Manifest.PackageId == firstPackageId).ClientPreview!.AlreadyApplied);
        Assert.True(resumedPreview.Members.Single(member =>
            member.Manifest.PackageId == secondPackageId).CanApply);

        var secondImport = await familyTransfers.ApplyAsync(
            encrypted, passphrase, "live-importer@example.test", "Approve remaining household member.",
            [secondPackageId]);
        Assert.Equal("Applied", Assert.Single(secondImport.Members).Status);
        var completedPreview = await familyTransfers.PreviewAsync(encrypted, passphrase);
        Assert.False(completedPreview.CanApply);
        Assert.All(completedPreview.Members, member => Assert.True(member.ClientPreview!.AlreadyApplied));
    }

    [Fact]
    public async Task Batch_candidates_group_completed_assessments_since_date()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var transferService = scope.ServiceProvider.GetRequiredService<ClientReviewTransferService>();
        var readiness = scope.ServiceProvider.GetRequiredService<ClientEvidenceReadinessService>();
        await readiness.LoadDashboardAsync();
        var methodology = await db.RiskMethodologyVersions
            .Include(item => item.Factors).ThenInclude(item => item.Options)
            .Where(item => item.Status == ComplianceStatuses.Review ||
                           item.Status == ComplianceStatuses.Approved ||
                           item.Status == ComplianceStatuses.Active)
            .OrderByDescending(item => item.Id)
            .FirstAsync();
        var requirements = await db.ClientEvidenceRequirements
            .Where(item => item.Status == ClientEvidenceRequirementStatuses.Active &&
                (item.ClientCategory == "All" || item.ClientCategory == ClientCategories.NaturalPerson))
            .ToListAsync();
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
        var afterSince = DateTime.UtcNow.AddDays(-1);
        var beforeSince = DateTime.UtcNow.AddDays(-20);
        var familyId = $"BATCH-{Guid.NewGuid():N}"[..30];
        var legacySeed = Random.Shared.Next(2_100_001, 2_200_000);
        var recentFamily = ReviewedNaturalPerson(
            legacySeed, familyId, "Batch Family Recent", 'e', methodology, requirements);
        recentFamily.RiskAssessments.Single().FinalisedAtUtc = afterSince;
        var olderFamily = ReviewedNaturalPerson(
            legacySeed + 1, familyId, "Batch Family Older", 'f', methodology, requirements);
        olderFamily.RiskAssessments.Single().FinalisedAtUtc = beforeSince;
        var excludedFamily = new Client
        {
            LegacyClientId = legacySeed + 2,
            KanaanId = familyId,
            DisplayName = "Batch Family Pending",
            SurnameOrEntityName = "Batch Family Pending",
            ClientCategory = ClientCategories.NaturalPerson,
            LifecycleStatus = ClientLifecycleStatuses.Unreviewed
        };
        var standalone = ReviewedNaturalPerson(
            legacySeed + 3, "", "Batch Standalone Recent", 'a', methodology, requirements);
        standalone.RiskAssessments.Single().FinalisedAtUtc = afterSince;
        var tooOld = ReviewedNaturalPerson(
            legacySeed + 4, "", "Batch Standalone Old", 'b', methodology, requirements);
        tooOld.RiskAssessments.Single().FinalisedAtUtc = beforeSince;
        db.Clients.AddRange(recentFamily, olderFamily, excludedFamily, standalone, tooOld);
        await db.SaveChangesAsync();

        var groups = await transferService.LoadBatchCandidatesAsync(since);

        var family = groups.Single(group => group.KanaanId == familyId);
        Assert.True(family.IsFamilyGroup);
        Assert.True(family.CanExportFamilyBundle);
        Assert.Equal(2, family.IncludedMemberCount);
        Assert.Equal(1, family.EligibleSinceCount);
        Assert.Equal(1, family.ExcludedMemberCount);
        Assert.Contains(family.Members, member => member.DisplayName == "Batch Family Older" && !member.IsEligibleByDate);

        var standaloneGroup = groups.Single(group => group.Members.Any(member => member.ClientId == standalone.Id));
        Assert.False(standaloneGroup.IsFamilyGroup);
        Assert.False(standaloneGroup.CanExportFamilyBundle);
        Assert.DoesNotContain(groups, group => group.Members.Any(member => member.ClientId == tooOld.Id));
    }

    [Fact]
    public void Client_folder_mapping_preserves_relative_path_and_uses_active_live_drive()
    {
        Assert.Equal(
            @"E:\Userdata\Kanaan Trust\Clients\BADENHORST PN",
            ClientReviewTransferService.MapClientFolderToLiveRoot(
                @"C:\Download\_kanaan\ClientsKanaan\BADENHORST PN",
                @"E:\Userdata\Kanaan Trust\Clients"));
        Assert.Equal(
            @"Z:\Userdata\Kanaan Trust\Clients\BADENHORST PN",
            ClientReviewTransferService.MapClientFolderToLiveRoot(
                @"C:\Download\_kanaan\ClientsKanaan\BADENHORST PN",
                @"Z:\Userdata\Kanaan Trust\Clients"));
        Assert.Equal(
            @"Z:\Userdata\Kanaan Trust\Clients\Family\BADENHORST PN",
            ClientReviewTransferService.MapClientFolderToLiveRoot(
                @"E:\Userdata\Kanaan Trust\Clients\Family\BADENHORST PN",
                @"Z:\Userdata\Kanaan Trust\Clients"));
        Assert.Null(ClientReviewTransferService.MapClientFolderToLiveRoot(
            @"C:\Download\_kanaan\ClientsKanaan-archive\BADENHORST PN",
            @"E:\Userdata\Kanaan Trust\Clients"));
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

    private static Client ReviewedNaturalPerson(
        int legacyClientId,
        string kanaanId,
        string displayName,
        char evidenceHashCharacter,
        RiskMethodologyVersion methodology,
        IReadOnlyCollection<ClientEvidenceRequirement> requirements)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var client = new Client
        {
            LegacyClientId = legacyClientId,
            KanaanId = kanaanId,
            DisplayName = displayName,
            SurnameOrEntityName = displayName,
            ClientCategory = ClientCategories.NaturalPerson,
            LifecycleStatus = ClientLifecycleStatuses.Current,
            LifecycleReason = "Current relationship confirmed for family transfer test.",
            LifecycleReviewedAtUtc = DateTime.UtcNow,
            LifecycleReviewedBy = "reviewer@example.test",
            IsActive = true
        };
        var identityRequirement = requirements.Single(item => item.EvidenceType == "Identity");
        var evidence = new ClientEvidenceItem
        {
            Client = client,
            ClientEvidenceRequirementId = identityRequirement.Id,
            EvidenceType = "Identity",
            Title = $"Verified identity for {displayName}",
            FileName = $"identity-{legacyClientId}.pdf",
            FileSha256 = new string(evidenceHashCharacter, 64),
            VerifiedDate = today,
            Reviewer = "reviewer@example.test",
            Status = ClientEvidenceStatuses.Verified,
            OwnershipStatus = ClientEvidenceOwnershipStatuses.Confirmed,
            SelectionStatus = ClientEvidenceSelectionStatuses.Current
        };
        client.EvidenceItems.Add(evidence);
        foreach (var requirement in requirements.Where(item => item.Id != identityRequirement.Id))
        {
            client.EvidenceExceptions.Add(new ClientEvidenceException
            {
                Requirement = requirement,
                Reason = $"Family transfer test exception for {requirement.EvidenceType}.",
                ApprovedBy = "reviewer@example.test",
                ReviewDate = today.AddYears(3)
            });
        }
        client.RiskAssessments.Add(new ClientRiskAssessment
        {
            MethodologyVersion = methodology,
            Status = ClientRiskAssessmentStatuses.Finalised,
            CalculatedScore = methodology.Factors.Sum(factor => factor.Options.First().Score),
            CalculatedRating = "Standard",
            FinalRating = "Standard",
            StandardControlsApplied = true,
            Narrative = $"Completed assessment for {displayName}.",
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
        });
        return client;
    }
}
