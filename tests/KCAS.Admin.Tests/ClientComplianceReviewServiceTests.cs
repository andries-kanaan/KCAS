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
}
