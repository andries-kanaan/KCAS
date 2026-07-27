using System.Text;
using KCAS.Admin.Data;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class InvestmentSummaryServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task LoadAsync_builds_portfolio_client_and_historical_views_from_one_calculation()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new InvestmentSummaryService(db);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var primary = new Client
        {
            LegacyClientId = 99001,
            KanaanId = "IS-990",
            DisplayName = "IS Primary Client",
            SurnameOrEntityName = "IS Primary Client",
            LifecycleStatus = ClientLifecycleStatuses.Current,
            IsActive = true,
            InvestmentAccounts =
            {
                new ClientInvestmentAccount
                {
                    AccountNumber = "IS-ACC-SA",
                    Administrator = "IS Platform",
                    ProductName = "Living Annuity"
                },
                new ClientInvestmentAccount
                {
                    AccountNumber = "IS-ACC-OFF",
                    Administrator = "IS Platform",
                    ProductName = "Offshore Investment"
                }
            },
            FundValuations =
            {
                new ClientFundValuation
                {
                    LegacyFundId = 99001,
                    InvestmentUniqueNumber = "IS-ACC-SA",
                    Administrator = "IS Platform",
                    ProductName = "Living Annuity",
                    FundName = "Stable SA",
                    AmountZar = 100_000m,
                    ValuationDate = today
                },
                new ClientFundValuation
                {
                    LegacyFundId = 99002,
                    InvestmentUniqueNumber = "IS-ACC-OFF",
                    Administrator = "IS Platform",
                    ProductName = "Offshore Investment",
                    FundName = "Moriah Global",
                    AmountForeign = 10_000m,
                    AmountZar = 200_000m,
                    ValuationDate = today
                }
            }
        };
        var secondary = new Client
        {
            LegacyClientId = 99002,
            KanaanId = "IS-990",
            DisplayName = "IS Secondary Client",
            SurnameOrEntityName = "IS Secondary Client",
            LifecycleStatus = ClientLifecycleStatuses.Unreviewed,
            IsActive = true,
            InvestmentAccounts =
            {
                new ClientInvestmentAccount
                {
                    AccountNumber = "IS-NEEDS-CORRECTION",
                    Administrator = "IS Platform",
                    FundName = "Legacy Holding"
                }
            },
            FundValuations =
            {
                new ClientFundValuation
                {
                    LegacyFundId = 99003,
                    InvestmentUniqueNumber = "IS-UNMATCHED",
                    Administrator = "IS Other Platform",
                    FundName = "Equity SA",
                    AmountZar = 50_000m,
                    ValuationDate = today.AddDays(-200)
                }
            }
        };
        var historicalAccount = new ClientInvestmentAccount
        {
            AccountNumber = "IS-HISTORICAL",
            Administrator = "IS Platform",
            FundName = "Stable SA",
            SurrenderDate = today.AddDays(-30),
            Transactions =
            {
                new ClientInvestmentTransaction
                {
                    LegacyInvestmentHistoryId = 99001,
                    TransactionDate = today.AddDays(-40),
                    BalanceZar = 25_000m,
                    IsFinal = true
                }
            }
        };
        var closed = new Client
        {
            LegacyClientId = 99003,
            KanaanId = "IS-990",
            DisplayName = "IS Closed Client",
            SurnameOrEntityName = "IS Closed Client",
            LifecycleStatus = ClientLifecycleStatuses.Closed,
            IsActive = false,
            InvestmentAccounts = { historicalAccount }
        };
        db.Clients.AddRange(primary, secondary, closed);
        await db.SaveChangesAsync();

        var portfolio = await service.LoadAsync(new InvestmentSummaryQuery(KanaanId: "IS-990"));

        Assert.Equal(3, portfolio.Rows.Count);
        Assert.Equal(350_000m, portfolio.TotalCurrentValueZar);
        Assert.Equal(150_000m, portfolio.SouthAfricanValueZar);
        Assert.Equal(200_000m, portfolio.OffshoreValueZar);
        Assert.Equal(2, portfolio.CurrentClientCount);
        Assert.Equal(3, portfolio.CurrentHoldingCount);
        Assert.Equal(2, portfolio.HistoricalHoldingCount);
        Assert.Equal(1, portfolio.StatusCorrectionCount);
        Assert.Equal(1, portfolio.UnmatchedValuationCount);
        Assert.Equal(1, portfolio.StaleValuationCount);
        Assert.Contains(portfolio.Rows, row =>
            row.ClientId == primary.Id &&
            row.FundName == "Moriah Global" &&
            row.ForeignCurrencyCode == "USD" &&
            row.CurrentValueForeign == 10_000m);
        Assert.Contains(portfolio.FundAllocation, item =>
            item.FundName == "Stable SA" &&
            item.AmountZar == 100_000m);

        var client = await service.LoadAsync(new InvestmentSummaryQuery(ClientId: primary.Id));
        Assert.Equal(2, client.Rows.Count);
        Assert.Equal(300_000m, client.TotalCurrentValueZar);
        Assert.All(client.Rows, row => Assert.Equal(primary.Id, row.ClientId));

        var historical = await service.LoadAsync(new InvestmentSummaryQuery(
            KanaanId: "IS-990",
            Scope: InvestmentSummaryScopes.Historical));
        Assert.Equal(2, historical.Rows.Count);
        Assert.Contains(historical.Rows, row => row.ClientId == closed.Id && !row.NeedsStatusCorrection);
        Assert.Contains(historical.Rows, row => row.ClientId == secondary.Id && row.NeedsStatusCorrection);

        var csv = Encoding.UTF8.GetString(await service.ExportCsvAsync(
            new InvestmentSummaryQuery(ClientId: primary.Id)));
        Assert.Contains("Client,Kanaan ID,Lifecycle", csv);
        Assert.Contains("IS Primary Client", csv);
        Assert.Contains("Moriah Global", csv);
        Assert.Contains("10000.00", csv);
        Assert.Contains("200000.00", csv);
    }
}
