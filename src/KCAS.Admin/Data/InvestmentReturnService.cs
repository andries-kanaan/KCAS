using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class InvestmentReturnService(ApplicationDbContext db)
{
    public async Task<InvestmentReturnReport?> LoadAsync(int clientId, int accountId,
        bool includeHiddenClients, string currency = "ZAR", CancellationToken cancellationToken = default)
    {
        if (currency is not ("ZAR" or "USD" or "GBP" or "EUR"))
            throw new ArgumentException("Select ZAR, USD, GBP or EUR.", nameof(currency));
        var account = await db.ClientInvestmentAccounts.AsNoTracking().Include(x => x.Client)
            .Include(x => x.Transactions)
            .SingleOrDefaultAsync(x => x.Id == accountId && x.ClientId == clientId &&
                (includeHiddenClients || !x.Client.ExcludeFromComplianceLists), cancellationToken);
        if (account is null) return null;
        // Only load connected accounts, never the entire clients' transaction history on each view.
        var reviews = await db.ClientInvestmentReconciliationReviews.AsNoTracking()
            .Where(x => includeHiddenClients || !x.Client.ExcludeFromComplianceLists)
            .ToListAsync(cancellationToken);
        var latest = reviews.GroupBy(x => x.ClientInvestmentAccountId)
            .Select(g => g.OrderByDescending(x => x.ReviewedAtUtc).ThenByDescending(x => x.Id).First()).ToList();
        var ids = new HashSet<int> { accountId };
        while (true)
        {
            var incoming = latest.Where(x => x.RelatedClientInvestmentAccountId.HasValue &&
                ids.Contains(x.RelatedClientInvestmentAccountId.Value) &&
                x.Outcome == ClientInvestmentReconciliationOutcomes.Transferred)
                .Select(x => x.ClientInvestmentAccountId).ToList();
            var count = ids.Count;
            ids.UnionWith(incoming);
            if (count == ids.Count) break;
        }
        var accounts = await db.ClientInvestmentAccounts.AsNoTracking().Include(x => x.Transactions)
            .Where(x => (ids.Contains(x.Id) || x.ClientId == clientId) &&
                (includeHiddenClients || !x.Client.ExcludeFromComplianceLists)).ToListAsync(cancellationToken);
        var clientIds = accounts.Select(x => x.ClientId).Distinct().ToList();
        var valuations = await db.ClientFundValuations.AsNoTracking().Where(x => clientIds.Contains(x.ClientId))
            .ToListAsync(cancellationToken);
        var report = InvestmentReturnCalculator.Build(account, accounts, valuations, reviews, currency);
        if (currency != "ZAR")
        {
            var funds = await db.InvestmentFundReferences.AsNoTracking().ToListAsync(cancellationToken);
            ValidateCurrency(report.ShortTerm, [account], funds, valuations, currency);
            ValidateCurrency(report.LongTerm, report.History, funds, valuations, currency);
        }
        return report;
    }

    private static void ValidateCurrency(InvestmentReturnResult result,
        IReadOnlyList<ClientInvestmentAccount> accounts, IReadOnlyList<InvestmentFundReference> funds,
        IReadOnlyList<ClientFundValuation> valuations, string currency)
    {
        foreach (var account in accounts)
        {
            var fund = funds.SingleOrDefault(x => account.LegacyFundId.HasValue && x.LegacyFundNameId == account.LegacyFundId);
            if (fund is null || !string.Equals(fund.Currency?.Trim(), currency, StringComparison.OrdinalIgnoreCase) ||
                ClientInvestmentStatusClassifier.MatchingValuations(account, valuations.Where(x => x.ClientId == account.ClientId)).Any(v =>
                    !funds.Any(f => string.Equals(f.Name, v.FundName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(f.Currency?.Trim(), currency, StringComparison.OrdinalIgnoreCase))))
            {
                result.Issues.Add($"{account.AccountNumber}: the recorded fund currency does not uniquely establish {currency} for all movements and closing values. Use recorded ZAR amounts or complete the currency history.");
                result.AnnualisedPercent = null;
                result.Gain = null;
            }
        }
    }
}
