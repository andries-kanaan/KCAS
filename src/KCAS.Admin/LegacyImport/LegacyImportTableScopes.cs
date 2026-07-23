namespace KCAS.Admin.LegacyImport;

public static class LegacyImportTableScopes
{
    public const string AllMapped = "AllMapped";
    public const string ReferenceData = "ReferenceData";
    public const string Clients = "Clients";
    public const string Notes = "Notes";
    public const string KycPolicies = "KycPolicies";
    public const string InvestmentAccounts = "InvestmentAccounts";
    public const string InvestmentTransactions = "InvestmentTransactions";
    public const string FundValuations = "FundValuations";

    public static readonly IReadOnlyList<LegacyImportTableScopeOption> Options =
    [
        new(AllMapped, "All mapped tables"),
        new(ReferenceData, "Reference data"),
        new(Clients, "Clients"),
        new(Notes, "Client notes"),
        new(KycPolicies, "KYC policies"),
        new(InvestmentAccounts, "Investment accounts"),
        new(InvestmentTransactions, "Investment transactions"),
        new(FundValuations, "Fund valuations")
    ];

    public static bool IsValid(string value)
        => Options.Any(option => option.Value == value);

    public static IReadOnlySet<string> Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) || value == AllMapped
            ? Options.Select(option => option.Value).Where(value => value != AllMapped).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : IsValid(value)
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { value }
                : throw new InvalidOperationException($"Unsupported legacy import table scope '{value}'.");
}

public sealed record LegacyImportTableScopeOption(string Value, string Label);
