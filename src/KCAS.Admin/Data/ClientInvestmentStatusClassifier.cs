namespace KCAS.Admin.Data;

public static class ClientInvestmentStatusClassifier
{
    public static ClientInvestmentStatus Evaluate(
        ClientInvestmentAccount account,
        IEnumerable<ClientFundValuation> valuations,
        DateOnly? asAtDate = null)
    {
        var asAt = asAtDate ?? DateOnly.FromDateTime(DateTime.Today);
        var matchedValuations = MatchingValuations(account, valuations);
        var hasCurrentValue = matchedValuations.Any(valuation =>
            valuation.AmountZar.HasValue || valuation.AmountForeign.HasValue);
        var isSurrendered = account.SurrenderDate.HasValue && account.SurrenderDate.Value <= asAt;

        if (!isSurrendered && hasCurrentValue)
        {
            return new ClientInvestmentStatus(true, false, "Current valuation available");
        }

        if (isSurrendered)
        {
            return new ClientInvestmentStatus(false, false, $"Surrendered {account.SurrenderDate:yyyy-MM-dd}");
        }

        return new ClientInvestmentStatus(
            false,
            true,
            "No current valuation and no effective surrender date");
    }

    public static IReadOnlyList<ClientFundValuation> MatchingValuations(
        ClientInvestmentAccount account,
        IEnumerable<ClientFundValuation> valuations)
    {
        var accountNumber = NormalizeAccountNumber(account.AccountNumber);
        if (accountNumber is null)
        {
            return [];
        }

        var matches = valuations
            .Where(valuation =>
                string.Equals(
                    NormalizeAccountNumber(valuation.InvestmentUniqueNumber),
                    accountNumber,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();

        var administrator = NormalizeLookup(account.Administrator);
        if (administrator is null)
        {
            return matches;
        }

        var administratorMatches = matches
            .Where(valuation => AdministratorsMatch(administrator, NormalizeLookup(valuation.Administrator)))
            .ToList();

        return administratorMatches.Count > 0 ? administratorMatches : matches;
    }

    public static string? NormalizeAccountNumber(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static bool AdministratorsMatch(string accountAdministrator, string? valuationAdministrator) =>
        valuationAdministrator is not null &&
        (string.Equals(accountAdministrator, valuationAdministrator, StringComparison.OrdinalIgnoreCase) ||
         accountAdministrator.Contains(valuationAdministrator, StringComparison.OrdinalIgnoreCase) ||
         valuationAdministrator.Contains(accountAdministrator, StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeLookup(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ClientInvestmentStatus(
    bool IsCurrent,
    bool NeedsStatusCorrection,
    string Reason);
