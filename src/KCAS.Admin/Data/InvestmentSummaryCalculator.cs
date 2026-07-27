namespace KCAS.Admin.Data;

public static class InvestmentSummaryCalculator
{
    public static List<ClientFundSummaryRowModel> BuildRows(
        IEnumerable<ClientInvestmentAccount> accounts,
        IEnumerable<ClientFundValuation> valuations)
    {
        var accountList = accounts.ToList();
        var valuationList = valuations.ToList();
        var rows = new List<ClientFundSummaryRowModel>();
        var today = DateOnly.FromDateTime(DateTime.Today);

        // A current valuation is the value-bearing source record. It must appear exactly once,
        // regardless of how many legacy account records happen to share its account number.
        foreach (var valuation in valuationList)
        {
            var candidates = MatchingAccounts(valuation, accountList);
            var account = PreferredAccount(valuation, candidates, today);
            var correctionReasons = new List<string>();

            if (candidates.Count == 0)
            {
                correctionReasons.Add("No investment account matches this current valuation");
            }
            else
            {
                if (candidates.Count > 1)
                {
                    correctionReasons.Add(
                        $"{candidates.Count} investment account records match this valuation");
                }

                if (candidates.All(item =>
                        item.SurrenderDate.HasValue && item.SurrenderDate.Value <= today))
                {
                    correctionReasons.Add(
                        "Current valuation is linked only to surrendered investment account records");
                }
            }

            rows.Add(new ClientFundSummaryRowModel
            {
                AccountId = account?.Id,
                AccountNumber = valuation.InvestmentUniqueNumber ?? account?.AccountNumber,
                Administrator = valuation.Administrator ?? account?.Administrator,
                ProductName = valuation.ProductName ?? account?.ProductName,
                ProductType = valuation.ProductType ?? account?.ProductType,
                FundName = valuation.FundName,
                CurrentValueZar = valuation.AmountZar,
                CurrentValueForeign = valuation.AmountForeign,
                CurrentValueDate = valuation.ValuationDate,
                Source = candidates.Count == 0 ? "Unmatched fund valuation" : "Fund valuation",
                TransactionCount = account?.Transactions.Count(transaction => !transaction.IsDeleted) ?? 0,
                NeedsStatusCorrection = correctionReasons.Count > 0,
                StatusReason = string.Join("; ", correctionReasons),
                ForeignCurrencyCode = ForeignCurrencyCode(valuation.FundName, valuation.AmountForeign)
            });
        }

        var valuationAccountNumbers = valuationList
            .Select(valuation =>
                ClientInvestmentStatusClassifier.NormalizeAccountNumber(valuation.InvestmentUniqueNumber))
            .Where(accountNumber => accountNumber is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Account records without a current valuation remain accessible as history.
        foreach (var account in accountList)
        {
            var accountNumber =
                ClientInvestmentStatusClassifier.NormalizeAccountNumber(account.AccountNumber);
            if (accountNumber is not null && valuationAccountNumbers.Contains(accountNumber))
            {
                continue;
            }

            var status = ClientInvestmentStatusClassifier.Evaluate(account, valuationList);
            var latestBalance = LatestBalanceTransaction(account);
            rows.Add(new ClientFundSummaryRowModel
            {
                AccountId = account.Id,
                AccountNumber = account.AccountNumber,
                Administrator = account.Administrator,
                ProductName = account.ProductName,
                ProductType = account.ProductType,
                FundName = account.FundName,
                CurrentValueZar = latestBalance?.BalanceZar,
                CurrentValueForeign = latestBalance?.BalanceForeign,
                CurrentValueDate = latestBalance?.TransactionDate,
                Source = latestBalance is null ? "No current value" : "History balance",
                TransactionCount = account.Transactions.Count(transaction => !transaction.IsDeleted),
                IsHistorical = true,
                NeedsStatusCorrection = status.NeedsStatusCorrection,
                StatusReason = status.Reason,
                ForeignCurrencyCode = ForeignCurrencyCode(account.FundName, latestBalance?.BalanceForeign)
            });
        }

        return rows
            .OrderBy(row => row.Administrator)
            .ThenBy(row => row.ProductName)
            .ThenBy(row => row.AccountNumber)
            .ThenBy(row => row.FundName)
            .ToList();
    }

    internal static IReadOnlyList<ClientInvestmentAccount> MatchingAccounts(
        ClientFundValuation valuation,
        IEnumerable<ClientInvestmentAccount> accounts)
    {
        var accountNumber =
            ClientInvestmentStatusClassifier.NormalizeAccountNumber(valuation.InvestmentUniqueNumber);
        if (accountNumber is null)
        {
            return [];
        }

        var matches = accounts
            .Where(account => string.Equals(
                ClientInvestmentStatusClassifier.NormalizeAccountNumber(account.AccountNumber),
                accountNumber,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (string.IsNullOrWhiteSpace(valuation.Administrator))
        {
            return matches;
        }

        var administratorMatches = matches
            .Where(account => AdministratorsMatch(account.Administrator, valuation.Administrator))
            .ToList();
        return administratorMatches.Count > 0 ? administratorMatches : matches;
    }

    internal static ClientInvestmentAccount? PreferredAccount(
        ClientFundValuation valuation,
        IReadOnlyList<ClientInvestmentAccount> candidates,
        DateOnly asAt) =>
        candidates
            .OrderBy(account =>
                account.SurrenderDate.HasValue && account.SurrenderDate.Value <= asAt ? 1 : 0)
            .ThenByDescending(account => FundNamesMatch(account.FundName, valuation.FundName))
            .ThenByDescending(account => account.InvestmentDate)
            .ThenByDescending(account => account.Id)
            .FirstOrDefault();

    private static bool AdministratorsMatch(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) &&
        !string.IsNullOrWhiteSpace(second) &&
        (string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase) ||
         first.Contains(second, StringComparison.OrdinalIgnoreCase) ||
         second.Contains(first, StringComparison.OrdinalIgnoreCase));

    private static bool FundNamesMatch(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) &&
        !string.IsNullOrWhiteSpace(second) &&
        (first.Contains(second, StringComparison.OrdinalIgnoreCase) ||
         second.Contains(first, StringComparison.OrdinalIgnoreCase));

    private static ClientInvestmentTransaction? LatestBalanceTransaction(ClientInvestmentAccount account) =>
        account.Transactions
            .Where(transaction =>
                !transaction.IsDeleted &&
                ((transaction.BalanceZar.HasValue && transaction.BalanceZar.Value != 0) ||
                 (transaction.BalanceForeign.HasValue && transaction.BalanceForeign.Value != 0)))
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenByDescending(transaction => transaction.LegacyInvestmentHistoryId)
            .FirstOrDefault();

    private static string? ForeignCurrencyCode(string? fundName, decimal? foreignAmount) =>
        foreignAmount.HasValue && foreignAmount.Value != 0
            ? fundName?.Contains("GBP", StringComparison.OrdinalIgnoreCase) == true ? "GBP" : "USD"
            : null;
}
