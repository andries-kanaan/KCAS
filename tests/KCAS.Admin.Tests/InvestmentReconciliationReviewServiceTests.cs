using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class InvestmentReconciliationReviewServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Client_review_requires_explicit_verification_applies_date_and_detects_stale_review()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new InvestmentReconciliationService(db);
        var transferDate = new DateOnly(2024, 5, 31);
        var client = new Client
        {
            LegacyClientId = 99801,
            KanaanId = "RECON-998",
            DisplayName = "Reconciliation Review Client",
            SurnameOrEntityName = "Reconciliation Review Client",
            LifecycleStatus = ClientLifecycleStatuses.Current,
            InvestmentAccounts =
            {
                new ClientInvestmentAccount
                {
                    LegacyInvestmentAccountId = 99801,
                    AccountNumber = "CURRENT-998",
                    Administrator = "Test Platform",
                    InvestmentDate = new DateOnly(2020, 1, 1)
                },
                new ClientInvestmentAccount
                {
                    LegacyInvestmentAccountId = 99802,
                    AccountNumber = "HIST-998",
                    Administrator = "Test Platform",
                    InvestmentDate = new DateOnly(2018, 1, 1),
                    Transactions =
                    {
                        new ClientInvestmentTransaction
                        {
                            LegacyInvestmentHistoryId = 99801,
                            TransactionDate = transferDate,
                            Description = "Full repurchase and transfer",
                            WithdrawalAmountZar = 75_000m,
                            IsFinal = true
                        }
                    }
                }
            },
            FundValuations =
            {
                new ClientFundValuation
                {
                    LegacyFundId = 99801,
                    InvestmentUniqueNumber = "CURRENT998",
                    Administrator = "Test Platform",
                    FundName = "Current Fund",
                    AmountZar = 125_000m,
                    ValuationDate = DateOnly.FromDateTime(DateTime.Today)
                }
            }
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var initial = await service.LoadClientReviewAsync(client.Id);

        Assert.False(initial.IsComplete);
        Assert.Equal(2, initial.Accounts.Count);
        var current = initial.Accounts.Single(item => item.AccountNumber == "CURRENT-998");
        var historical = initial.Accounts.Single(item => item.AccountNumber == "HIST-998");
        Assert.Equal(ClientInvestmentReconciliationOutcomes.Current, current.ProposedOutcome);
        Assert.Equal(ClientInvestmentReconciliationOutcomes.Transferred, historical.ProposedOutcome);
        Assert.Equal(transferDate, historical.ProposedSurrenderDate);

        await service.ReviewAccountAsync(client.Id, current.AccountId, new ClientInvestmentReconciliationReviewRequest
        {
            Outcome = ClientInvestmentReconciliationOutcomes.Current,
            EvidenceReference = "Current valuation",
            Reason = "Current valuation and account match verified."
        }, "reviewer@example.test");
        await service.ReviewAccountAsync(client.Id, historical.AccountId, new ClientInvestmentReconciliationReviewRequest
        {
            Outcome = ClientInvestmentReconciliationOutcomes.Transferred,
            SurrenderDate = transferDate,
            RelatedAccountId = current.AccountId,
            EvidenceReference = "Signed transfer instruction",
            Reason = "Full transfer and effective date verified."
        }, "reviewer@example.test");

        var verified = await service.LoadClientReviewAsync(client.Id);
        Assert.True(verified.IsComplete,
            $"Unmatched issues: {string.Join(" | ", verified.UnmatchedIssues.Select(item => item.Details))}; " +
            $"accounts: {string.Join(" | ", verified.Accounts.Select(item => $"{item.AccountNumber}: verified={item.IsVerified}, stale={item.ReviewIsStale}, follow-up={item.NeedsFollowUp}"))}");
        Assert.All(verified.Accounts, item => Assert.True(item.IsVerified));
        Assert.Equal(transferDate, await db.ClientInvestmentAccounts
            .Where(item => item.Id == historical.AccountId)
            .Select(item => item.SurrenderDate)
            .SingleAsync());
        Assert.Equal(2, await db.ComplianceAuditEvents.CountAsync(item =>
            item.EntityType == nameof(ClientInvestmentAccount) &&
            item.Action == "InvestmentReconciliationVerified" &&
            (item.EntityId == current.AccountId || item.EntityId == historical.AccountId)));

        var currentAccount = await db.ClientInvestmentAccounts.SingleAsync(item => item.Id == current.AccountId);
        currentAccount.ProductName = "Updated product description";
        await db.SaveChangesAsync();

        var stale = await service.LoadClientReviewAsync(client.Id);
        Assert.False(stale.IsComplete);
        Assert.True(stale.Accounts.Single(item => item.AccountId == current.AccountId).ReviewIsStale);
    }

    [Fact]
    public async Task Duplicate_continuation_requires_related_account()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new InvestmentReconciliationService(db);
        var client = new Client
        {
            LegacyClientId = 99811,
            KanaanId = "RECON-999",
            DisplayName = "Duplicate Review Client",
            SurnameOrEntityName = "Duplicate Review Client",
            InvestmentAccounts =
            {
                new ClientInvestmentAccount { LegacyInvestmentAccountId = 99811, AccountNumber = "DUP-999", Administrator = "Old Platform" },
                new ClientInvestmentAccount { LegacyInvestmentAccountId = 99812, AccountNumber = "DUP999", Administrator = "New Platform" }
            }
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var accountId = client.InvestmentAccounts.First().Id;

        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            service.ReviewAccountAsync(client.Id, accountId, new ClientInvestmentReconciliationReviewRequest
            {
                Outcome = ClientInvestmentReconciliationOutcomes.DuplicateContinuation,
                EvidenceReference = "Account comparison",
                Reason = "Duplicate records reviewed."
            }, "reviewer@example.test"));
    }

    [Fact]
    public async Task Batch_verification_rolls_back_all_rows_when_one_proposal_is_invalid()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = new InvestmentReconciliationService(db);
        var client = new Client
        {
            LegacyClientId = 99821,
            KanaanId = "RECON-1000",
            DisplayName = "Atomic Reconciliation Client",
            SurnameOrEntityName = "Atomic Reconciliation Client",
            InvestmentAccounts =
            {
                new ClientInvestmentAccount { LegacyInvestmentAccountId = 99821, AccountNumber = "ATOMIC-1", Administrator = "Platform A" },
                new ClientInvestmentAccount { LegacyInvestmentAccountId = 99822, AccountNumber = "ATOMIC-2", Administrator = "Platform B" }
            }
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        var accounts = client.InvestmentAccounts.OrderBy(item => item.Id).ToList();

        var requests = new[]
        {
            new ClientInvestmentReconciliationBatchRequest(accounts[0].Id, new ClientInvestmentReconciliationReviewRequest
            {
                Outcome = ClientInvestmentReconciliationOutcomes.NeedsFollowUp,
                EvidenceReference = "Initial review",
                Reason = "Requires a supporting statement."
            }),
            new ClientInvestmentReconciliationBatchRequest(accounts[1].Id, new ClientInvestmentReconciliationReviewRequest
            {
                Outcome = ClientInvestmentReconciliationOutcomes.DuplicateContinuation,
                EvidenceReference = "Account comparison",
                Reason = "Missing the required related account."
            })
        };

        await Assert.ThrowsAsync<System.ComponentModel.DataAnnotations.ValidationException>(() =>
            service.ReviewAccountsAsync(client.Id, requests, "reviewer@example.test"));

        Assert.Equal(0, await db.ClientInvestmentReconciliationReviews.CountAsync(item => item.ClientId == client.Id));
        Assert.Equal(0, await db.ComplianceAuditEvents.CountAsync(item =>
            item.EntityType == nameof(ClientInvestmentAccount) &&
            (item.EntityId == accounts[0].Id || item.EntityId == accounts[1].Id)));
    }
}
