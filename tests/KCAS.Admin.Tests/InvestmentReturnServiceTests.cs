using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KCAS.Admin.Tests;

[Collection(KcasTestCollection.Name)]
public sealed class InvestmentReturnServiceTests(KcasWebApplicationFactory factory)
{
    [Fact]
    public async Task Service_loads_original_chain_and_later_funding_without_rewriting_history()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var (client, current, valuation) = Fixture();
        var start = current.InvestmentDate!.Value;
        current.InvestmentDate = start.AddDays(365);
        current.Transactions.Clear();
        current.Transactions.Add(Movement(current.InvestmentDate.Value, investment: 80));
        current.Transactions.Add(Movement(current.InvestmentDate.Value, investment: 30));
        current.Transactions.Add(Movement(start.AddDays(500), investment: 50));
        var original = new ClientInvestmentAccount { AccountNumber = $"Original {Guid.NewGuid():N}",
            InvestmentDate = start, SurrenderDate = current.InvestmentDate, Administrator = "Return test" };
        original.Transactions.Add(Movement(start, investment: 100));
        original.Transactions.Add(Movement(start.AddDays(350), withdrawal: 80));
        original.Transactions.Add(Movement(start.AddDays(360), withdrawal: 30));
        var later = new ClientInvestmentAccount { AccountNumber = $"Later {Guid.NewGuid():N}",
            InvestmentDate = start, SurrenderDate = start.AddDays(500), Administrator = "Return test" };
        later.Transactions.Add(Movement(start, investment: 45));
        later.Transactions.Add(Movement(later.SurrenderDate.Value, withdrawal: 50));
        client.InvestmentAccounts.Add(original);
        client.InvestmentAccounts.Add(later);
        valuation.AmountZar = 176;
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        foreach (var source in new[] { original, later })
        {
            var persisted = await db.ClientInvestmentAccounts.AsNoTracking().Include(x => x.Transactions)
                .SingleAsync(x => x.Id == source.Id);
            db.ClientInvestmentReconciliationReviews.Add(new() { ClientId = client.Id,
                ClientInvestmentAccountId = source.Id, RelatedClientInvestmentAccountId = current.Id,
                Outcome = ClientInvestmentReconciliationOutcomes.Transferred, AppliedSurrenderDate = source.SurrenderDate,
                EvidenceReference = "Test statement", Reason = "Reviewed transfer", ReviewedBy = "Test reviewer",
                ReviewedAtUtc = DateTime.UtcNow, SnapshotSha256 = InvestmentReconciliationService.CalculateSnapshot(persisted, []) });
        }
        await db.SaveChangesAsync();
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<InvestmentReturnService>();
            var report = await service.LoadAsync(client.Id, current.Id, true);
            Assert.NotNull(report);
            Assert.True(report.ShortTerm.IsAvailable, string.Join("; ", report.ShortTerm.Issues));
            Assert.True(report.LongTerm.IsAvailable, string.Join("; ", report.LongTerm.Issues));
            Assert.Equal(start, report.LongTerm.StartDate);
            Assert.Equal(new[] { original.Id, current.Id }, report.History.Select(x => x.Id));
            Assert.Equal(4, report.LongTerm.CashFlows.Count(x => x.IsInternalTransfer));
            Assert.Single(report.LongTerm.CashFlows, x => x.AccountId == current.Id && x.Amount == -50 && !x.IsInternalTransfer);
            Assert.Equal(26, report.LongTerm.Gain);
            Assert.False(db.ChangeTracker.HasChanges());
            Assert.Equal(8, await db.ClientInvestmentTransactions.CountAsync(x => x.InvestmentAccount.ClientId == client.Id));
        }
        finally
        {
            await transaction.RollbackAsync();
            db.ChangeTracker.Clear();
        }
    }

    private static ClientInvestmentTransaction Movement(DateOnly date, decimal? investment = null, decimal? withdrawal = null) => new()
    {
        TransactionDate = date, InvestmentAmountZar = investment, WithdrawalAmountZar = withdrawal,
        InvestmentFrequency = "Once Off", IsFinal = true
    };

    [Fact]
    public async Task Hidden_client_is_unavailable_to_non_administrator_and_data_is_read_only()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (client, account, valuation) = Fixture();
        client.ExcludeFromComplianceLists = true;
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<InvestmentReturnService>();
            Assert.Null(await service.LoadAsync(client.Id, account.Id, false));
            var report = await service.LoadAsync(client.Id, account.Id, true);
            Assert.NotNull(report);
            Assert.True(report.ShortTerm.IsAvailable, string.Join("; ", report.ShortTerm.Issues));
            Assert.Null(await service.LoadAsync(client.Id + 1, account.Id, true));
            Assert.False(db.ChangeTracker.HasChanges());
            Assert.Single(account.Transactions);
            Assert.Equal(121m, valuation.AmountZar);
        }
        finally
        {
            db.Clients.Remove(client);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Native_currency_requires_explicit_reference_and_does_not_reuse_zar_values()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var (client, account, valuation) = Fixture();
        var legacyId = Random.Shared.Next(1_000_000_000, 2_000_000_000);
        var fund = new InvestmentFundReference { Name = client.DisplayName, LegacyFundNameId = legacyId, Currency = "USD" };
        account.LegacyFundId = legacyId;
        account.FundName = fund.Name;
        account.Transactions.Single().InvestmentAmountForeign = 10;
        valuation.FundName = fund.Name;
        valuation.AmountForeign = 12.1m;
        db.Clients.Add(client);
        db.InvestmentFundReferences.Add(fund);
        await db.SaveChangesAsync();
        try
        {
            var service = scope.ServiceProvider.GetRequiredService<InvestmentReturnService>();
            var report = await service.LoadAsync(client.Id, account.Id, true, "USD");
            Assert.NotNull(report);
            Assert.True(report.ShortTerm.IsAvailable, string.Join("; ", report.ShortTerm.Issues));
            Assert.Equal(2.1m, report.ShortTerm.Gain);
            Assert.InRange(report.ShortTerm.AnnualisedPercent!.Value, 9.999m, 10.001m);
            Assert.False((await service.LoadAsync(client.Id, account.Id, true, "GBP"))!.ShortTerm.IsAvailable);
            await Assert.ThrowsAsync<ArgumentException>(() => service.LoadAsync(client.Id, account.Id, true, "FAKE"));
        }
        finally
        {
            db.Clients.Remove(client);
            db.InvestmentFundReferences.Remove(fund);
            await db.SaveChangesAsync();
        }
    }

    private static (Client, ClientInvestmentAccount, ClientFundValuation) Fixture()
    {
        var name = $"Return test {Guid.NewGuid():N}";
        var start = new DateOnly(2023, 1, 1);
        var client = new Client { DisplayName = name, SurnameOrEntityName = name };
        var account = new ClientInvestmentAccount { AccountNumber = name, InvestmentDate = start, Administrator = "Return test" };
        account.Transactions.Add(new() { TransactionDate = start, InvestmentAmountZar = 100, IsFinal = true, InvestmentFrequency = "Once Off" });
        var valuation = new ClientFundValuation { LegacyFundId = Random.Shared.Next(1_000_000_000, 2_000_000_000),
            InvestmentUniqueNumber = name, FundName = name, AmountZar = 121, Administrator = account.Administrator, ValuationDate = start.AddDays(730) };
        client.InvestmentAccounts.Add(account);
        client.FundValuations.Add(valuation);
        return (client, account, valuation);
    }
}
