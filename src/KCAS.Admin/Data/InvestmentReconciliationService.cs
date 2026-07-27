using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class InvestmentReconciliationService(ApplicationDbContext db)
{
    public async Task<InvestmentReconciliationModel> LoadAsync(
        InvestmentReconciliationQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new InvestmentReconciliationQuery();
        var clients = await db.Clients
            .AsNoTracking()
            .Where(client => client.InvestmentAccounts.Any() || client.FundValuations.Any())
            .Include(client => client.InvestmentAccounts)
            .Include(client => client.FundValuations)
            .AsSplitQuery()
            .OrderBy(client => client.DisplayName)
            .ToListAsync(cancellationToken);

        var issues = clients.SelectMany(BuildIssues).ToList();
        IEnumerable<InvestmentReconciliationIssue> filtered = issues;

        if (!string.IsNullOrWhiteSpace(query.IssueType))
        {
            filtered = filtered.Where(issue => issue.IssueType == query.IssueType);
        }

        if (query.ClientId.HasValue)
        {
            filtered = filtered.Where(issue => issue.ClientId == query.ClientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            filtered = filtered.Where(issue =>
                Contains(issue.ClientDisplayName, search) ||
                Contains(issue.KanaanId, search) ||
                Contains(issue.AccountNumber, search) ||
                Contains(issue.FundName, search) ||
                Contains(issue.Administrator, search) ||
                Contains(issue.Details, search));
        }

        return new InvestmentReconciliationModel
        {
            Query = query,
            Issues = filtered
                .OrderByDescending(issue => issue.SeverityOrder)
                .ThenBy(issue => issue.ClientDisplayName)
                .ThenBy(issue => issue.AccountNumber)
                .ThenBy(issue => issue.FundName)
                .ToList(),
            ClientOptions = clients
                .Select(client => new InvestmentSummaryClientOption(
                    client.Id, client.KanaanId, client.DisplayName, client.LifecycleStatus))
                .ToList(),
            TotalIssueCount = issues.Count,
            DuplicateMatchCount = issues.Count(issue =>
                issue.IssueType == InvestmentReconciliationIssueTypes.DuplicateAccountMatch),
            SurrenderConflictCount = issues.Count(issue =>
                issue.IssueType == InvestmentReconciliationIssueTypes.CurrentValuationAfterSurrender),
            UnmatchedValuationCount = issues.Count(issue =>
                issue.IssueType == InvestmentReconciliationIssueTypes.UnmatchedValuation),
            MissingCurrentValueCount = issues.Count(issue =>
                issue.IssueType == InvestmentReconciliationIssueTypes.MissingCurrentValuation)
        };
    }

    internal static IReadOnlyList<InvestmentReconciliationIssue> BuildIssues(Client client)
    {
        var issues = new List<InvestmentReconciliationIssue>();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var valuationNumbers = client.FundValuations
            .Select(item => ClientInvestmentStatusClassifier.NormalizeAccountNumber(
                item.InvestmentUniqueNumber))
            .Where(item => item is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var valuation in client.FundValuations)
        {
            var candidates = InvestmentSummaryCalculator.MatchingAccounts(
                valuation, client.InvestmentAccounts);
            if (candidates.Count == 0)
            {
                issues.Add(FromValuation(
                    client,
                    valuation,
                    InvestmentReconciliationIssueTypes.UnmatchedValuation,
                    "No matching account",
                    "Create or correct an investment account so its account number and administrator match this valuation.",
                    [],
                    3));
                continue;
            }

            if (candidates.Count > 1)
            {
                issues.Add(FromValuation(
                    client,
                    valuation,
                    InvestmentReconciliationIssueTypes.DuplicateAccountMatch,
                    $"{candidates.Count} account records match this valuation",
                    "Review the matching account records and correct or remove duplicates. The valuation is counted once while this is unresolved.",
                    candidates.Select(account => account.Id).ToList(),
                    2));
            }

            if (candidates.All(account =>
                    account.SurrenderDate.HasValue && account.SurrenderDate.Value <= today))
            {
                issues.Add(FromValuation(
                    client,
                    valuation,
                    InvestmentReconciliationIssueTypes.CurrentValuationAfterSurrender,
                    "A current valuation is linked only to surrendered accounts",
                    "Confirm the valuation is current, then correct the account surrender date or account number. Do not delete the valuation merely to clear the warning.",
                    candidates.Select(account => account.Id).ToList(),
                    4));
            }
        }

        foreach (var account in client.InvestmentAccounts)
        {
            var accountNumber =
                ClientInvestmentStatusClassifier.NormalizeAccountNumber(account.AccountNumber);
            if (accountNumber is not null && valuationNumbers.Contains(accountNumber))
            {
                continue;
            }

            var isSurrendered =
                account.SurrenderDate.HasValue && account.SurrenderDate.Value <= today;
            if (!isSurrendered)
            {
                issues.Add(new InvestmentReconciliationIssue
                {
                    IssueType = InvestmentReconciliationIssueTypes.MissingCurrentValuation,
                    IssueLabel = "No current valuation",
                    ClientId = client.Id,
                    KanaanId = client.KanaanId,
                    ClientDisplayName = client.DisplayName,
                    AccountNumber = account.AccountNumber,
                    Administrator = account.Administrator,
                    FundName = account.FundName,
                    Details = "This account has no current valuation and no effective surrender date.",
                    RecommendedAction = "Confirm whether the investment remains current. Load/correct the valuation if current, or capture the effective surrender date if historical.",
                    AccountIds = [account.Id],
                    SeverityOrder = 1
                });
            }
        }

        return issues;
    }

    private static InvestmentReconciliationIssue FromValuation(
        Client client,
        ClientFundValuation valuation,
        string issueType,
        string details,
        string recommendedAction,
        IReadOnlyList<int> accountIds,
        int severityOrder) =>
        new()
        {
            IssueType = issueType,
            IssueLabel = InvestmentReconciliationIssueTypes.Label(issueType),
            ClientId = client.Id,
            KanaanId = client.KanaanId,
            ClientDisplayName = client.DisplayName,
            ValuationId = valuation.Id,
            LegacyFundId = valuation.LegacyFundId,
            AccountNumber = valuation.InvestmentUniqueNumber,
            Administrator = valuation.Administrator,
            FundName = valuation.FundName,
            ValuationDate = valuation.ValuationDate,
            AmountZar = valuation.AmountZar,
            Details = details,
            RecommendedAction = recommendedAction,
            AccountIds = accountIds,
            SeverityOrder = severityOrder
        };

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
}

public sealed record InvestmentReconciliationQuery(
    string? IssueType = null,
    int? ClientId = null,
    string? Search = null);

public static class InvestmentReconciliationIssueTypes
{
    public const string DuplicateAccountMatch = "DuplicateAccountMatch";
    public const string CurrentValuationAfterSurrender = "CurrentValuationAfterSurrender";
    public const string UnmatchedValuation = "UnmatchedValuation";
    public const string MissingCurrentValuation = "MissingCurrentValuation";

    public static IReadOnlyList<string> All { get; } =
        [CurrentValuationAfterSurrender, UnmatchedValuation, DuplicateAccountMatch, MissingCurrentValuation];

    public static string Label(string issueType) => issueType switch
    {
        DuplicateAccountMatch => "Multiple account matches",
        CurrentValuationAfterSurrender => "Current valuation / surrender conflict",
        UnmatchedValuation => "No matching account",
        MissingCurrentValuation => "No current valuation",
        _ => issueType
    };
}

public sealed class InvestmentReconciliationModel
{
    public InvestmentReconciliationQuery Query { get; init; } = new();
    public List<InvestmentReconciliationIssue> Issues { get; init; } = [];
    public List<InvestmentSummaryClientOption> ClientOptions { get; init; } = [];
    public int TotalIssueCount { get; init; }
    public int DuplicateMatchCount { get; init; }
    public int SurrenderConflictCount { get; init; }
    public int UnmatchedValuationCount { get; init; }
    public int MissingCurrentValueCount { get; init; }
}

public sealed class InvestmentReconciliationIssue
{
    public string IssueType { get; init; } = "";
    public string IssueLabel { get; init; } = "";
    public int ClientId { get; init; }
    public string? KanaanId { get; init; }
    public string ClientDisplayName { get; init; } = "";
    public int? ValuationId { get; init; }
    public int? LegacyFundId { get; init; }
    public string? AccountNumber { get; init; }
    public string? Administrator { get; init; }
    public string? FundName { get; init; }
    public DateOnly? ValuationDate { get; init; }
    public decimal? AmountZar { get; init; }
    public string Details { get; init; } = "";
    public string RecommendedAction { get; init; } = "";
    public IReadOnlyList<int> AccountIds { get; init; } = [];
    public int SeverityOrder { get; init; }
}
