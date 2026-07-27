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

        foreach (var account in accountList)
        {
            var status = ClientInvestmentStatusClassifier.Evaluate(account, valuationList);
            var matchedValuations = ClientInvestmentStatusClassifier.MatchingValuations(account, valuationList);
            var latestBalance = LatestBalanceTransaction(account);
            if (!status.IsCurrent)
            {
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
                continue;
            }

            foreach (var valuation in matchedValuations)
            {
                rows.Add(new ClientFundSummaryRowModel
                {
                    AccountId = account.Id,
                    AccountNumber = account.AccountNumber,
                    Administrator = valuation.Administrator ?? account.Administrator,
                    ProductName = valuation.ProductName ?? account.ProductName,
                    ProductType = valuation.ProductType ?? account.ProductType,
                    FundName = valuation.FundName,
                    CurrentValueZar = valuation.AmountZar,
                    CurrentValueForeign = valuation.AmountForeign,
                    CurrentValueDate = valuation.ValuationDate,
                    Source = "Fund valuation",
                    TransactionCount = account.Transactions.Count(transaction => !transaction.IsDeleted),
                    StatusReason = status.Reason,
                    ForeignCurrencyCode = ForeignCurrencyCode(valuation.FundName, valuation.AmountForeign)
                });
            }
        }

        var accountNumbers = accountList
            .Select(account => ClientInvestmentStatusClassifier.NormalizeAccountNumber(account.AccountNumber))
            .Where(accountNumber => accountNumber is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var valuation in valuationList)
        {
            var valuationAccountNumber =
                ClientInvestmentStatusClassifier.NormalizeAccountNumber(valuation.InvestmentUniqueNumber);
            if (valuationAccountNumber is not null && accountNumbers.Contains(valuationAccountNumber))
            {
                continue;
            }

            rows.Add(new ClientFundSummaryRowModel
            {
                AccountNumber = valuation.InvestmentUniqueNumber,
                Administrator = valuation.Administrator,
                ProductName = valuation.ProductName,
                ProductType = valuation.ProductType,
                FundName = valuation.FundName,
                CurrentValueZar = valuation.AmountZar,
                CurrentValueForeign = valuation.AmountForeign,
                CurrentValueDate = valuation.ValuationDate,
                Source = "Unmatched fund valuation",
                ForeignCurrencyCode = ForeignCurrencyCode(valuation.FundName, valuation.AmountForeign)
            });
        }

        return rows
            .OrderBy(row => row.Administrator)
            .ThenBy(row => row.ProductName)
            .ThenBy(row => row.AccountNumber)
            .ThenBy(row => row.FundName)
            .ToList();
    }

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
