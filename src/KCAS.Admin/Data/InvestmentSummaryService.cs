using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class InvestmentSummaryService(ApplicationDbContext db)
{
    public async Task<InvestmentSummaryModel> LoadAsync(
        InvestmentSummaryQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new InvestmentSummaryQuery();
        var staleCutoff = DateOnly.FromDateTime(DateTime.Today.AddDays(-query.StaleAfterDays));

        var clientOptions = await db.Clients
            .AsNoTracking()
            .Where(client => client.InvestmentAccounts.Any() || client.FundValuations.Any())
            .OrderBy(client => client.DisplayName)
            .Select(client => new InvestmentSummaryClientOption(
                client.Id,
                client.KanaanId,
                client.DisplayName,
                client.LifecycleStatus))
            .ToListAsync(cancellationToken);

        var clientsQuery = db.Clients
            .AsNoTracking()
            .Where(client => client.InvestmentAccounts.Any() || client.FundValuations.Any());
        if (query.ClientId.HasValue)
        {
            clientsQuery = clientsQuery.Where(client => client.Id == query.ClientId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(query.KanaanId))
        {
            var kanaanId = query.KanaanId.Trim();
            clientsQuery = clientsQuery.Where(client => client.KanaanId == kanaanId);
        }

        var clients = await clientsQuery
            .Include(client => client.InvestmentAccounts)
                .ThenInclude(account => account.Transactions)
            .Include(client => client.FundValuations)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var allRows = clients
            .SelectMany(client => InvestmentSummaryCalculator
                .BuildRows(client.InvestmentAccounts, client.FundValuations)
                .Select(row => InvestmentSummaryRow.From(client, row, staleCutoff)))
            .ToList();

        var fundOptions = allRows
            .Select(row => row.FundName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .Cast<string>()
            .ToList();
        var administratorOptions = allRows
            .Select(row => row.Administrator)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .Cast<string>()
            .ToList();

        IEnumerable<InvestmentSummaryRow> filtered = allRows;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            filtered = filtered.Where(row =>
                Contains(row.ClientDisplayName, search) ||
                Contains(row.KanaanId, search) ||
                Contains(row.FundName, search) ||
                Contains(row.Administrator, search) ||
                Contains(row.ProductName, search) ||
                Contains(row.ProductType, search) ||
                Contains(row.AccountNumber, search));
        }

        if (!string.IsNullOrWhiteSpace(query.LifecycleStatus))
        {
            filtered = filtered.Where(row =>
                string.Equals(row.LifecycleStatus, query.LifecycleStatus, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.FundName))
        {
            filtered = filtered.Where(row =>
                string.Equals(row.FundName, query.FundName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Administrator))
        {
            filtered = filtered.Where(row =>
                string.Equals(row.Administrator, query.Administrator, StringComparison.OrdinalIgnoreCase));
        }

        var summaryRows = filtered.ToList();
        var displayedRows = query.Scope switch
        {
            InvestmentSummaryScopes.Historical => summaryRows.Where(row => row.IsHistorical),
            InvestmentSummaryScopes.All => summaryRows,
            _ => summaryRows.Where(row => !row.IsHistorical)
        };

        displayedRows = ApplySort(displayedRows, query.SortColumn, query.SortDescending);
        var currentRows = summaryRows.Where(row => !row.IsHistorical).ToList();
        var totalCurrentValueZar = Sum(currentRows.Select(row => row.CurrentValueZar));
        var allocation = currentRows
            .GroupBy(row => string.IsNullOrWhiteSpace(row.FundName) ? "Not captured" : row.FundName.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var amount = Sum(group.Select(row => row.CurrentValueZar));
                var percentage = totalCurrentValueZar.HasValue &&
                                 totalCurrentValueZar.Value != 0 &&
                                 amount.HasValue
                    ? amount.Value / totalCurrentValueZar.Value * 100
                    : (decimal?)null;
                return new InvestmentFundAllocation(
                    group.Key,
                    InvestmentGeographies.Classify(group),
                    amount,
                    percentage,
                    group.Select(row => row.ClientId).Distinct().Count());
            })
            .OrderByDescending(item => item.AmountZar)
            .ThenBy(item => item.FundName)
            .ToList();

        return new InvestmentSummaryModel
        {
            Query = query,
            Rows = displayedRows.ToList(),
            ClientOptions = clientOptions,
            KanaanIdOptions = clientOptions
                .Select(option => option.KanaanId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .Cast<string>()
                .ToList(),
            FundOptions = fundOptions,
            AdministratorOptions = administratorOptions,
            FundAllocation = allocation,
            TotalCurrentValueZar = totalCurrentValueZar,
            CurrentClientCount = currentRows.Select(row => row.ClientId).Distinct().Count(),
            CurrentHoldingCount = currentRows.Count,
            HistoricalHoldingCount = summaryRows.Count(row => row.IsHistorical),
            StatusCorrectionCount = summaryRows.Count(row => row.NeedsStatusCorrection),
            UnmatchedValuationCount = currentRows.Count(row => row.Source == "Unmatched fund valuation"),
            StaleValuationCount = currentRows.Count(row => row.IsValuationStale),
            LatestValuationDate = currentRows
                .Where(row => row.CurrentValueDate.HasValue)
                .MaxBy(row => row.CurrentValueDate)
                ?.CurrentValueDate,
            SouthAfricanValueZar = Sum(currentRows
                .Where(row => row.Geography == InvestmentGeographies.SouthAfrica)
                .Select(row => row.CurrentValueZar)),
            OffshoreValueZar = Sum(currentRows
                .Where(row => row.Geography == InvestmentGeographies.Offshore)
                .Select(row => row.CurrentValueZar))
        };
    }

    public async Task<byte[]> ExportCsvAsync(
        InvestmentSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        var model = await LoadAsync(query, cancellationToken);
        var csv = new StringBuilder();
        csv.AppendLine(
            "Client,Kanaan ID,Lifecycle,Valuation date,Fund,Geography,Administrator,Product,Product type,Account,Native currency,Native value,ZAR value,Position,Source,Correction");
        foreach (var row in model.Rows)
        {
            csv.AppendLine(string.Join(",",
                Csv(row.ClientDisplayName),
                Csv(row.KanaanId),
                Csv(row.LifecycleStatus),
                Csv(row.CurrentValueDate?.ToString("yyyy-MM-dd")),
                Csv(row.FundName),
                Csv(row.Geography),
                Csv(row.Administrator),
                Csv(row.ProductName),
                Csv(row.ProductType),
                Csv(row.AccountNumber),
                Csv(row.ForeignCurrencyCode),
                Csv(Number(row.CurrentValueForeign)),
                Csv(Number(row.CurrentValueZar)),
                Csv(row.IsHistorical ? "Historical" : "Current"),
                Csv(row.Source),
                Csv(row.NeedsStatusCorrection ? row.StatusReason : null)));
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString());
    }

    private static IOrderedEnumerable<InvestmentSummaryRow> ApplySort(
        IEnumerable<InvestmentSummaryRow> rows,
        string? column,
        bool descending) =>
        (column, descending) switch
        {
            ("kanaanId", true) => rows.OrderByDescending(row => row.KanaanId),
            ("kanaanId", false) => rows.OrderBy(row => row.KanaanId),
            ("lifecycle", true) => rows.OrderByDescending(row => row.LifecycleStatus),
            ("lifecycle", false) => rows.OrderBy(row => row.LifecycleStatus),
            ("date", true) => rows.OrderByDescending(row => row.CurrentValueDate),
            ("date", false) => rows.OrderBy(row => row.CurrentValueDate),
            ("fund", true) => rows.OrderByDescending(row => row.FundName),
            ("fund", false) => rows.OrderBy(row => row.FundName),
            ("administrator", true) => rows.OrderByDescending(row => row.Administrator),
            ("administrator", false) => rows.OrderBy(row => row.Administrator),
            ("account", true) => rows.OrderByDescending(row => row.AccountNumber),
            ("account", false) => rows.OrderBy(row => row.AccountNumber),
            ("value", true) => rows.OrderByDescending(row => row.CurrentValueZar),
            ("value", false) => rows.OrderBy(row => row.CurrentValueZar),
            ("client", true) => rows.OrderByDescending(row => row.ClientDisplayName).ThenBy(row => row.FundName),
            _ => rows.OrderBy(row => row.ClientDisplayName).ThenBy(row => row.FundName)
        };

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        var captured = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return captured.Count == 0 ? null : captured.Sum();
    }

    private static string? Number(decimal? value) =>
        value?.ToString("0.00####", CultureInfo.InvariantCulture);

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}

public sealed record InvestmentSummaryQuery(
    int? ClientId = null,
    string? KanaanId = null,
    string? Search = null,
    string? LifecycleStatus = null,
    string? FundName = null,
    string? Administrator = null,
    string Scope = InvestmentSummaryScopes.Current,
    string SortColumn = "client",
    bool SortDescending = false,
    int StaleAfterDays = 90);

public static class InvestmentSummaryScopes
{
    public const string Current = "Current";
    public const string Historical = "Historical";
    public const string All = "All";
}

public static class InvestmentGeographies
{
    public const string SouthAfrica = "South Africa";
    public const string Offshore = "Offshore";

    public static string Classify(IEnumerable<InvestmentSummaryRow> rows) =>
        rows.Any(row => row.Geography == Offshore) ? Offshore : SouthAfrica;

    public static string Classify(string? fundName, string? currencyCode)
    {
        if (!string.IsNullOrWhiteSpace(currencyCode) ||
            fundName?.Contains("Offshore", StringComparison.OrdinalIgnoreCase) == true ||
            fundName?.Contains("Moriah", StringComparison.OrdinalIgnoreCase) == true ||
            fundName?.Contains("Cash account", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Offshore;
        }

        return SouthAfrica;
    }
}

public sealed class InvestmentSummaryModel
{
    public InvestmentSummaryQuery Query { get; init; } = new();
    public List<InvestmentSummaryRow> Rows { get; init; } = [];
    public List<InvestmentSummaryClientOption> ClientOptions { get; init; } = [];
    public List<string> KanaanIdOptions { get; init; } = [];
    public List<string> FundOptions { get; init; } = [];
    public List<string> AdministratorOptions { get; init; } = [];
    public List<InvestmentFundAllocation> FundAllocation { get; init; } = [];
    public decimal? TotalCurrentValueZar { get; init; }
    public decimal? SouthAfricanValueZar { get; init; }
    public decimal? OffshoreValueZar { get; init; }
    public int CurrentClientCount { get; init; }
    public int CurrentHoldingCount { get; init; }
    public int HistoricalHoldingCount { get; init; }
    public int StatusCorrectionCount { get; init; }
    public int UnmatchedValuationCount { get; init; }
    public int StaleValuationCount { get; init; }
    public DateOnly? LatestValuationDate { get; init; }
}

public sealed record InvestmentSummaryClientOption(
    int Id,
    string? KanaanId,
    string DisplayName,
    string LifecycleStatus);

public sealed record InvestmentFundAllocation(
    string FundName,
    string Geography,
    decimal? AmountZar,
    decimal? Percentage,
    int ClientCount);

public sealed class InvestmentSummaryRow
{
    public int ClientId { get; init; }
    public string? KanaanId { get; init; }
    public string ClientDisplayName { get; init; } = "";
    public string LifecycleStatus { get; init; } = ClientLifecycleStatuses.Unreviewed;
    public int? AccountId { get; init; }
    public string? AccountNumber { get; init; }
    public string? Administrator { get; init; }
    public string? ProductName { get; init; }
    public string? ProductType { get; init; }
    public string? FundName { get; init; }
    public string Geography { get; init; } = InvestmentGeographies.SouthAfrica;
    public decimal? CurrentValueZar { get; init; }
    public decimal? CurrentValueForeign { get; init; }
    public DateOnly? CurrentValueDate { get; init; }
    public string Source { get; init; } = "";
    public bool IsHistorical { get; init; }
    public bool NeedsStatusCorrection { get; init; }
    public string StatusReason { get; init; } = "";
    public string? ForeignCurrencyCode { get; init; }
    public bool IsValuationStale { get; init; }

    public static InvestmentSummaryRow From(
        Client client,
        ClientFundSummaryRowModel row,
        DateOnly staleCutoff) =>
        new()
        {
            ClientId = client.Id,
            KanaanId = client.KanaanId,
            ClientDisplayName = client.DisplayName,
            LifecycleStatus = client.LifecycleStatus,
            AccountId = row.AccountId,
            AccountNumber = row.AccountNumber,
            Administrator = row.Administrator,
            ProductName = row.ProductName,
            ProductType = row.ProductType,
            FundName = row.FundName,
            Geography = InvestmentGeographies.Classify(row.FundName, row.ForeignCurrencyCode),
            CurrentValueZar = row.CurrentValueZar,
            CurrentValueForeign = row.CurrentValueForeign,
            CurrentValueDate = row.CurrentValueDate,
            Source = row.Source,
            IsHistorical = row.IsHistorical,
            NeedsStatusCorrection = row.NeedsStatusCorrection,
            StatusReason = row.StatusReason,
            ForeignCurrencyCode = row.ForeignCurrencyCode,
            IsValuationStale = !row.IsHistorical &&
                               (!row.CurrentValueDate.HasValue || row.CurrentValueDate.Value < staleCutoff)
        };
}
