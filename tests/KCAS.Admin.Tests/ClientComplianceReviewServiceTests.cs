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
            DisplayName = "Unified Review Client",
            SurnameOrEntityName = "Unified Review Client",
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
        Assert.DoesNotContain(review.PendingFacts, item => item.IsBlocking);
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
}
