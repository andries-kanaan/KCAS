using KCAS.Admin.Data;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class ClientComplianceReviewServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Unified_review_starts_with_folder_and_proposes_current_from_current_investment()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientComplianceReviewService>();
        var client = new Client
        {
            LegacyClientId = 99791,
            KanaanId = "UNIFIED-99791",
            FullName = "Unified Review",
            DisplayName = "Unified Review",
            SurnameOrEntityName = "Client",
            LifecycleStatus = ClientLifecycleStatuses.Unreviewed,
            InvestmentAccounts =
            {
                new ClientInvestmentAccount
                {
                    LegacyInvestmentAccountId = 99791,
                    AccountNumber = "UNIFIED-ACCOUNT",
                    Administrator = "Unified Platform",
                    InvestmentDate = new DateOnly(2022, 1, 1)
                }
            },
            FundValuations =
            {
                new ClientFundValuation
                {
                    LegacyFundId = 99791,
                    InvestmentUniqueNumber = "UNIFIEDACCOUNT",
                    Administrator = "Unified Platform",
                    FundName = "Unified Fund",
                    AmountZar = 100_000m,
                    ValuationDate = DateOnly.FromDateTime(DateTime.Today)
                }
            }
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var review = await service.LoadAsync(client.Id);

        Assert.Equal("folder", review.Sections[0].Code);
        Assert.False(review.Sections[0].IsComplete);
        Assert.Equal("Select or scan client folder", review.NextAction.Label);
        Assert.Equal(ClientLifecycleStatuses.Current, review.LifecycleProposal.Status);
        Assert.True(review.LifecycleProposal.CanConfirm);
        Assert.Equal(1, review.CurrentInvestmentCount);
        Assert.Equal(100_000m, review.CurrentInvestmentValueZar);
        Assert.Equal("Unified Review Client", review.DisplayName);
        Assert.DoesNotContain(review.PendingFacts, item => item.IsBlocking);
    }

    [Fact]
    public async Task Unified_review_recommends_local_folder_when_saved_client_folder_is_unavailable()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientComplianceReviewService>();
        var root = Path.Combine(Path.GetTempPath(), "kcas-compliance-tests", Guid.NewGuid().ToString("N"));
        var correctFolder = Directory.CreateDirectory(Path.Combine(root, "ALPHA PRIMARY and PARTNER (Example Trust)"));
        Directory.CreateDirectory(Path.Combine(root, "RELATED-ALPHA SECONDARY HOUSEHOLD"));

        try
        {
            db.ClientEvidenceScanRoots.Add(new ClientEvidenceScanRoot
            {
                RootPath = root,
                IsActive = true
            });
            var client = new Client
            {
                LegacyClientId = Random.Shared.Next(800000, 899999),
                KanaanId = $"ALPHA-{Guid.NewGuid():N}"[..30],
                DisplayName = "Primary and Partner Alpha",
                FullName = "Primary Example and Partner",
                Initials = "P and P",
                SurnameOrEntityName = "Alpha",
                ClientFolder = @"z:\Kanaan Trust\Clients\Clients\Alpha and Partner Primary"
            };
            db.Clients.Add(client);
            db.Clients.Add(new Client
            {
                LegacyClientId = Random.Shared.Next(800000, 899999),
                KanaanId = client.KanaanId,
                DisplayName = "Primary Example Alpha",
                FullName = "Primary Example",
                Initials = "PE",
                SurnameOrEntityName = "Alpha"
            });
            await db.SaveChangesAsync();

            var review = await service.LoadAsync(client.Id);

            Assert.False(review.ClientFolderExists);
            Assert.Equal(root, review.ActiveScanRoot);
            var recommendation = Assert.Single(review.FolderRecommendations);
            Assert.Equal(correctFolder.FullName, recommendation.Path);
            Assert.DoesNotContain(review.FolderRecommendations, item => item.FolderName.Contains("RELATED", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Review_display_name_does_not_repeat_an_existing_surname()
    {
        var client = new Client
        {
            FullName = "Example Person",
            SurnameOrEntityName = "Person",
            DisplayName = "Example Person"
        };

        Assert.Equal("Example Person", ClientNameFormatter.FullNameAndSurname(client));
    }

    [Fact]
    public void Lifecycle_proposal_marks_fully_surrendered_investments_historical()
    {
        var client = new Client
        {
            LifecycleStatus = ClientLifecycleStatuses.Unreviewed,
            InvestmentAccounts =
            {
                new ClientInvestmentAccount
                {
                    AccountNumber = "HISTORICAL-1",
                    SurrenderDate = new DateOnly(2020, 1, 1)
                }
            }
        };

        var proposal = ClientComplianceReviewService.BuildLifecycleProposal(client);

        Assert.Equal(ClientLifecycleStatuses.Historical, proposal.Status);
        Assert.True(proposal.CanConfirm);
    }

    [Fact]
    public async Task Latest_folder_scan_loads_current_progress_for_live_review_updates()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientComplianceReviewService>();
        var folder = @"C:\KCAS-tests\live-review";
        var client = new Client
        {
            LegacyClientId = 99792,
            KanaanId = "UNIFIED-99792",
            DisplayName = "Live Review Client",
            SurnameOrEntityName = "Live Review Client",
            ClientFolder = folder
        };
        var run = new ClientEvidenceScanRun
        {
            RootPath = folder,
            Status = ClientEvidenceScanStatuses.Running,
            TotalFiles = 75,
            LinkedFiles = 62,
            UnmatchedFiles = 8,
            AmbiguousFiles = 5
        };
        db.Clients.Add(client);
        db.ClientEvidenceScanRuns.Add(run);
        await db.SaveChangesAsync();

        var progress = await service.LoadLatestFolderScanAsync(client.Id);

        Assert.NotNull(progress);
        Assert.Equal(ClientEvidenceScanStatuses.Running, progress.Status);
        Assert.Equal(75, progress.TotalFiles);
        Assert.Equal(62, progress.LinkedFiles);
        Assert.Equal(8, progress.UnmatchedFiles);
        Assert.Equal(5, progress.AmbiguousFiles);
    }

    [Fact]
    public async Task Applied_review_package_satisfies_folder_mapping_without_repeat_scan()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientComplianceReviewService>();
        var folder = Path.Combine(Path.GetTempPath(), "kcas-compliance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var client = new Client
            {
                LegacyClientId = Random.Shared.Next(900000, 999999),
                KanaanId = $"IMPORTED-{Guid.NewGuid():N}"[..30],
                DisplayName = "Imported Review Client",
                SurnameOrEntityName = "Client",
                ClientFolder = folder
            };
            db.Clients.Add(client);
            await db.SaveChangesAsync();
            db.ClientReviewTransferRecords.Add(new ClientReviewTransferRecord
            {
                PackageId = Guid.NewGuid().ToString(),
                Direction = ClientReviewTransferDirections.Incoming,
                ContentSha256 = new string('a', 64),
                ClientId = client.Id,
                Status = ClientReviewTransferStatuses.Applied,
                FileName = "imported.kcas-review",
                StoragePath = Path.Combine(folder, "imported.kcas-review"),
                SummaryJson = "{}",
                AppliedAtUtc = DateTime.UtcNow,
                AppliedBy = "importer@example.test"
            });
            await db.SaveChangesAsync();

            var review = await service.LoadAsync(client.Id);

            var folderSection = Assert.Single(review.Sections, item => item.Code == "folder");
            Assert.True(folderSection.IsComplete);
            Assert.Equal("Client folder and evidence mapping", folderSection.Title);
            Assert.Contains("no repeat scan is required", folderSection.Summary, StringComparison.OrdinalIgnoreCase);
            var factsSection = Assert.Single(review.Sections, item => item.Code == "facts");
            Assert.Equal("Client facts are consistent; no unresolved conflicts.", factsSection.Summary);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Individually_verified_files_satisfy_mapping_only_when_present_in_client_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "kcas-compliance-tests", Guid.NewGuid().ToString("N"));
        var folder = Directory.CreateDirectory(Path.Combine(root, "client")).FullName;
        var otherFolder = Directory.CreateDirectory(Path.Combine(root, "other")).FullName;
        var identityPath = Path.Combine(folder, "identity.pdf");
        var addressPath = Path.Combine(folder, "address.pdf");
        var outsidePath = Path.Combine(otherFolder, "address.pdf");

        try
        {
            File.WriteAllText(identityPath, "identity");
            File.WriteAllText(addressPath, "address");
            File.WriteAllText(outsidePath, "other");
            var requirements = new List<ClientEvidenceRequirementStatusModel>
            {
                new() { EvidenceType = "Identity", VerifiedItemCount = 1 },
                new() { EvidenceType = "Address", VerifiedItemCount = 1 },
                new() { EvidenceType = "SourceOfWealth", IsExceptioned = true }
            };
            var evidence = new List<ClientEvidenceItem>
            {
                new() { EvidenceType = "Identity", SourcePath = identityPath, Status = ClientEvidenceStatuses.Verified, SelectionStatus = ClientEvidenceSelectionStatuses.Current },
                new() { EvidenceType = "Address", SourcePath = addressPath, Status = ClientEvidenceStatuses.Verified, SelectionStatus = ClientEvidenceSelectionStatuses.Current }
            };

            Assert.True(ClientComplianceReviewService.HasVerifiedLocalEvidenceMapping(folder, requirements, evidence));

            evidence[1].SourcePath = outsidePath;
            Assert.False(ClientComplianceReviewService.HasVerifiedLocalEvidenceMapping(folder, requirements, evidence));

            evidence[1].SourcePath = addressPath;
            File.Delete(addressPath);
            Assert.False(ClientComplianceReviewService.HasVerifiedLocalEvidenceMapping(folder, requirements, evidence));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Duplicate_record_is_resolved_without_a_separate_risk_assessment()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ClientComplianceReviewService>();
        var canonical = new Client
        {
            LegacyClientId = Random.Shared.Next(600000, 699999),
            KanaanId = $"CANON-{Guid.NewGuid():N}"[..30],
            DisplayName = "Canonical Review Client",
            FullName = "Canonical Review Client",
            SurnameOrEntityName = "Client",
            LifecycleStatus = ClientLifecycleStatuses.Historical
        };
        db.Clients.Add(canonical);
        await db.SaveChangesAsync();
        var duplicate = new Client
        {
            LegacyClientId = Random.Shared.Next(700000, 799999),
            KanaanId = $"DUP-{Guid.NewGuid():N}"[..30],
            DisplayName = "Duplicate Review Client",
            FullName = "Duplicate Review Client",
            SurnameOrEntityName = "Client",
            LifecycleStatus = ClientLifecycleStatuses.Duplicate,
            LifecycleReason = "Same trust allocation recorded under the canonical client.",
            DuplicateOfClientId = canonical.Id
        };
        db.Clients.Add(duplicate);
        await db.SaveChangesAsync();

        var review = await service.LoadAsync(duplicate.Id);

        Assert.True(review.IsComplete);
        Assert.True(review.IsResolvedDuplicate);
        Assert.Equal(canonical.Id, review.DuplicateOfClientId);
        Assert.Equal("Canonical Review Client", review.DuplicateOfDisplayName);
        Assert.Equal("View canonical client review", review.NextAction.Label);
        Assert.Null(review.Risk.Assessment);
    }
}
