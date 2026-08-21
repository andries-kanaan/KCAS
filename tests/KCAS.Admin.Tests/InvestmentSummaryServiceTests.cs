using System.Text;
using KCAS.Admin.Data;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class InvestmentSummaryServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public void BuildRows_counts_each_valuation_once_and_does_not_suppress_surrender_conflicts()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var accounts = new[]
        {
            new ClientInvestmentAccount
            {
                Id = 1,
                AccountNumber = "DUP-001",
                Administrator = "Platform",
                SurrenderDate = today.AddYears(-1)
            },
            new ClientInvestmentAccount
            {
                Id = 2,
                AccountNumber = "DUP 001",
                Administrator = "Platform",
                SurrenderDate = today.AddMonths(-6)
            }
        };
        var valuations = new[]
        {
            new ClientFundValuation
            {
                LegacyFundId = 1234,
                InvestmentUniqueNumber = "DUP001",
                Administrator = "Platform",
                FundName = "Current Fund",
                AmountZar = 125_000m,
                ValuationDate = today
            }
        };

        var rows = InvestmentSummaryCalculator.BuildRows(accounts, valuations);

        var row = Assert.Single(rows);
        Assert.False(row.IsHistorical);
        Assert.Equal(125_000m, row.CurrentValueZar);
        Assert.True(row.NeedsStatusCorrection);
        Assert.Contains("2 investment account records", row.StatusReason);
        Assert.Contains("surrendered", row.StatusReason);
    }

    [Fact]
    public void Reconciliation_identifies_issues_and_clears_them_after_source_correction()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var client = new Client
        {
            Id = 77,
            DisplayName = "Reconciliation Client",
            SurnameOrEntityName = "Reconciliation Client",
            InvestmentAccounts =
            {
                new ClientInvestmentAccount
                {
                    Id = 1,
                    AccountNumber = "REC-001",
                    Administrator = "Platform",
                    SurrenderDate = today.AddDays(-10)
                },
                new ClientInvestmentAccount
                {
                    Id = 2,
                    AccountNumber = "REC001",
                    Administrator = "Platform",
                    SurrenderDate = today.AddDays(-5)
                },
                new ClientInvestmentAccount
                {
                    Id = 3,
                    AccountNumber = "NO-VALUE",
                    Administrator = "Platform"
                }
            },
            FundValuations =
            {
                new ClientFundValuation
                {
                    LegacyFundId = 5001,
                    InvestmentUniqueNumber = "REC001",
                    Administrator = "Platform",
                    FundName = "Current Fund",
                    AmountZar = 10_000m,
                    ValuationDate = today
                },
                new ClientFundValuation
                {
                    LegacyFundId = 5002,
                    InvestmentUniqueNumber = "UNMATCHED",
                    Administrator = "Other Platform",
                    FundName = "Other Fund",
                    AmountZar = 20_000m,
                    ValuationDate = today
                }
            }
        };

        var issues = InvestmentReconciliationService.BuildIssues(client);
        Assert.Contains(issues, issue =>
            issue.IssueType == InvestmentReconciliationIssueTypes.DuplicateAccountMatch);
        Assert.Contains(issues, issue =>
            issue.IssueType == InvestmentReconciliationIssueTypes.CurrentValuationAfterSurrender);
        Assert.Contains(issues, issue =>
            issue.IssueType == InvestmentReconciliationIssueTypes.UnmatchedValuation);
        Assert.Contains(issues, issue =>
            issue.IssueType == InvestmentReconciliationIssueTypes.MissingCurrentValuation);

        client.InvestmentAccounts.Remove(client.InvestmentAccounts.Single(account => account.Id == 2));
        client.InvestmentAccounts.Single(account => account.Id == 1).SurrenderDate = null;
        client.InvestmentAccounts.Single(account => account.Id == 3).SurrenderDate = today;
        client.InvestmentAccounts.Add(new ClientInvestmentAccount
        {
            Id = 4,
            AccountNumber = "UNMATCHED",
            Administrator = "Other Platform"
        });

        Assert.Empty(InvestmentReconciliationService.BuildIssues(client));
    }

    [Fact]
    public async Task LoadAsync_clears_correction_for_reviewed_wrong_client_duplicate()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new InvestmentSummaryService(db);

        var source = new Client
        {
            LegacyClientId = 99101,
            KanaanId = "IS-WCD",
            DisplayName = "IS Wrong Duplicate Source",
            SurnameOrEntityName = "IS Wrong Duplicate Source",
            LifecycleStatus = ClientLifecycleStatuses.Current,
            IsActive = true,
            InvestmentAccounts =
            {
                new ClientInvestmentAccount
                {
                    AccountNumber = "IS-WRONG-CLIENT",
                    Administrator = "IS Platform",
                    ProductName = "Imported duplicate"
                }
            }
        };
        var owner = new Client
        {
            LegacyClientId = 99102,
            KanaanId = "IS-WCD",
            DisplayName = "IS Correct Owner",
            SurnameOrEntityName = "IS Correct Owner",
            LifecycleStatus = ClientLifecycleStatuses.Current,
            IsActive = true,
            InvestmentAccounts =
            {
                new ClientInvestmentAccount
                {
                    AccountNumber = "IS-WRONG-CLIENT",
                    Administrator = "IS Platform",
                    ProductName = "Owned account"
                }
            },
            FundValuations =
            {
                new ClientFundValuation
                {
                    LegacyFundId = 99103,
                    InvestmentUniqueNumber = "IS-WRONG-CLIENT",
                    Administrator = "IS Platform",
                    ProductName = "Owned account",
                    FundName = "Stable SA",
                    AmountZar = 75_000m,
                    ValuationDate = DateOnly.FromDateTime(DateTime.Today)
                }
            }
        };

        db.Clients.AddRange(source, owner);
        await db.SaveChangesAsync();

        var sourceAccount = source.InvestmentAccounts.Single();
        var ownerAccount = owner.InvestmentAccounts.Single();
        db.ClientInvestmentReconciliationReviews.Add(new ClientInvestmentReconciliationReview
        {
            ClientId = source.Id,
            ClientInvestmentAccountId = sourceAccount.Id,
            Outcome = ClientInvestmentReconciliationOutcomes.WrongClientDuplicate,
            RelatedClientInvestmentAccountId = ownerAccount.Id,
            EvidenceReference = "Test evidence",
            Reason = "Reviewed as a wrong-client duplicate linked to the correct owner.",
            SnapshotSha256 = new string('a', 64),
            ReviewedAtUtc = DateTime.UtcNow,
            ReviewedBy = "test"
        });
        await db.SaveChangesAsync();

        var model = await service.LoadAsync(new InvestmentSummaryQuery(
            KanaanId: "IS-WCD",
            Scope: InvestmentSummaryScopes.All));

        var duplicateRow = Assert.Single(model.Rows, row => row.ClientId == source.Id);
        Assert.False(duplicateRow.NeedsStatusCorrection);
        Assert.Contains("Wrong client duplicate", duplicateRow.Source);
        Assert.Contains("wrong-client duplicate", duplicateRow.StatusReason);
        Assert.Equal(0, model.StatusCorrectionCount);
    }

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
        Assert.Equal(2, portfolio.StatusCorrectionCount);
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
