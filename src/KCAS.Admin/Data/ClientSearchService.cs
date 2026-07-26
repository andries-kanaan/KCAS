using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientSearchService
{
    private readonly ApplicationDbContext? db;
    private readonly IDbContextFactory<ApplicationDbContext>? dbFactory;

    public ClientSearchService(ApplicationDbContext db)
    {
        this.db = db;
    }

    public ClientSearchService(IDbContextFactory<ApplicationDbContext> dbFactory)
    {
        this.dbFactory = dbFactory;
    }

    public async Task<List<ClientSearchResult>> SearchAsync(string? searchText, int take = 100)
    {
        return await SearchAsync(new ClientSearchRequest(GlobalSearch: searchText), take);
    }

    public async Task<List<ClientSearchResult>> SearchAsync(ClientSearchRequest request, int take = 500)
    {
        await using var ownedDb = dbFactory is null ? null : await dbFactory.CreateDbContextAsync();
        var searchDb = ownedDb ?? db ?? throw new InvalidOperationException("Client search database context is not configured.");

        var normalizedQuery = request.GlobalSearch?.Trim();
        var kanaanId = request.KanaanId?.Trim();
        var name = request.Name?.Trim();
        var surname = request.Surname?.Trim();
        var email = request.Email?.Trim();
        var phone = request.Phone?.Trim();
        var status = request.Status?.Trim();
        var investmentPosition = request.InvestmentPosition?.Trim();

        var query = searchDb.Clients
            .AsNoTracking()
            .Include(client => client.PersonalProfile)
            .Include(client => client.ContactPoints)
            .Include(client => client.InvestmentAccounts)
            .Include(client => client.FundValuations)
            .AsSplitQuery()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            query = query.Where(client =>
                (client.KanaanId != null && client.KanaanId.Contains(normalizedQuery)) ||
                client.DisplayName.Contains(normalizedQuery) ||
                client.SurnameOrEntityName.Contains(normalizedQuery) ||
                (client.FullName != null && client.FullName.Contains(normalizedQuery)) ||
                (client.PersonalProfile != null && client.PersonalProfile.SouthAfricanIdNumber != null && client.PersonalProfile.SouthAfricanIdNumber.Contains(normalizedQuery)) ||
                client.ContactPoints.Any(contact => contact.Value.Contains(normalizedQuery)));
        }

        if (!string.IsNullOrWhiteSpace(kanaanId))
        {
            query = query.Where(client => client.KanaanId != null && client.KanaanId.Contains(kanaanId));
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            query = query.Where(client =>
                client.DisplayName.Contains(name) ||
                (client.FullName != null && client.FullName.Contains(name)));
        }

        if (!string.IsNullOrWhiteSpace(surname))
        {
            query = query.Where(client => client.SurnameOrEntityName.Contains(surname));
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            query = query.Where(client => client.ContactPoints.Any(contact => contact.ContactType == "Email" && contact.Value.Contains(email)));
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            query = query.Where(client => client.ContactPoints.Any(contact => contact.ContactType != "Email" && contact.Value.Contains(phone)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (ClientLifecycleStatuses.All.Contains(status))
            {
                query = query.Where(client => client.LifecycleStatus == status);
            }
            else
            {
                query = query.Where(client => client.LifecycleStatus.Contains(status));
            }
        }

        var clients = await query.ToListAsync();

        var results = clients.Select(client =>
        {
            var investment = BuildInvestmentPosition(client);
            return new ClientSearchResult(
                client.Id,
                client.KanaanId,
                string.IsNullOrWhiteSpace(client.FullName) ? client.DisplayName : client.FullName,
                client.SurnameOrEntityName,
                client.ContactPoints
                    .Where(contact => contact.ContactType == "Email")
                    .OrderByDescending(contact => contact.IsPrimary)
                    .ThenBy(contact => contact.SortOrder)
                    .Select(contact => contact.Value)
                    .FirstOrDefault(),
                client.ContactPoints
                    .Where(contact => contact.ContactType is "Mobile" or "Phone")
                    .OrderByDescending(contact => contact.IsPrimary)
                    .ThenBy(contact => contact.SortOrder)
                    .Select(contact => contact.Value)
                    .FirstOrDefault(),
                client.IsActive,
                client.LifecycleStatus,
                investment.Label,
                investment.HasCurrentInvestments,
                investment.TotalCurrentValueZar,
                investment.HistoricalAccountCount,
                investment.StatusCorrectionCount);
        });

        results = investmentPosition switch
        {
            ClientInvestmentPositionFilters.Current =>
                results.Where(client => client.HasCurrentInvestments),
            ClientInvestmentPositionFilters.NoCurrent =>
                results.Where(client => !client.HasCurrentInvestments),
            ClientInvestmentPositionFilters.HistoricalOnly =>
                results.Where(client =>
                    !client.HasCurrentInvestments &&
                    client.HistoricalInvestmentAccountCount > 0 &&
                    client.InvestmentStatusCorrectionCount == 0),
            ClientInvestmentPositionFilters.NeedsCorrection =>
                results.Where(client => client.InvestmentStatusCorrectionCount > 0),
            _ => results
        };

        results = (request.SortColumn, request.SortDescending) switch
        {
            ("kanaanId", true) => results.OrderByDescending(client => client.KanaanId),
            ("kanaanId", false) => results.OrderBy(client => client.KanaanId),
            ("name", true) => results.OrderByDescending(client => client.Name),
            ("name", false) => results.OrderBy(client => client.Name),
            ("surname", true) => results.OrderByDescending(client => client.Surname),
            ("surname", false) => results.OrderBy(client => client.Surname),
            ("email", true) => results.OrderByDescending(client => client.PrimaryEmail),
            ("email", false) => results.OrderBy(client => client.PrimaryEmail),
            ("phone", true) => results.OrderByDescending(client => client.PrimaryPhone),
            ("phone", false) => results.OrderBy(client => client.PrimaryPhone),
            ("status", true) => results.OrderByDescending(client => client.LifecycleStatus),
            ("status", false) => results.OrderBy(client => client.LifecycleStatus),
            ("investment", true) => results.OrderByDescending(client => client.InvestmentPosition),
            ("investment", false) => results.OrderBy(client => client.InvestmentPosition),
            ("currentValue", true) => results.OrderByDescending(client => client.TotalCurrentValueZar),
            ("currentValue", false) => results.OrderBy(client => client.TotalCurrentValueZar),
            _ => results.OrderBy(client => client.Surname).ThenBy(client => client.Name)
        };

        return results.Take(take).ToList();
    }

    private static ClientInvestmentPosition BuildInvestmentPosition(Client client)
    {
        var valuations = client.FundValuations.ToList();
        var accountStatuses = client.InvestmentAccounts
            .Select(account => new
            {
                Account = account,
                Status = ClientInvestmentStatusClassifier.Evaluate(account, valuations)
            })
            .ToList();
        var currentAccounts = accountStatuses
            .Where(item => item.Status.IsCurrent)
            .Select(item => item.Account)
            .ToList();
        var historicalAccountCount = accountStatuses.Count(item => !item.Status.IsCurrent);
        var correctionCount = accountStatuses.Count(item => item.Status.NeedsStatusCorrection);

        var currentValuationIds = currentAccounts
            .SelectMany(account => ClientInvestmentStatusClassifier.MatchingValuations(account, valuations))
            .Select(valuation => valuation.Id)
            .ToHashSet();
        var accountNumbers = client.InvestmentAccounts
            .Select(account => ClientInvestmentStatusClassifier.NormalizeAccountNumber(account.AccountNumber))
            .Where(accountNumber => accountNumber is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unmatchedCurrentValuations = valuations
            .Where(valuation =>
            {
                var accountNumber =
                    ClientInvestmentStatusClassifier.NormalizeAccountNumber(valuation.InvestmentUniqueNumber);
                return !currentValuationIds.Contains(valuation.Id) &&
                       (accountNumber is null || !accountNumbers.Contains(accountNumber)) &&
                       (valuation.AmountZar.HasValue || valuation.AmountForeign.HasValue);
            })
            .ToList();
        var currentValuations = valuations
            .Where(valuation => currentValuationIds.Contains(valuation.Id))
            .Concat(unmatchedCurrentValuations)
            .DistinctBy(valuation => valuation.Id)
            .ToList();
        var hasCurrentInvestments = currentAccounts.Count > 0 || unmatchedCurrentValuations.Count > 0;
        decimal? totalCurrentValueZar = currentValuations.Any(valuation => valuation.AmountZar.HasValue)
            ? currentValuations.Where(valuation => valuation.AmountZar.HasValue).Sum(valuation => valuation.AmountZar!.Value)
            : null;

        var label = (hasCurrentInvestments, correctionCount, historicalAccountCount) switch
        {
            (true, > 0, _) => "Current investments · correction needed",
            (true, _, _) => "Current investments",
            (false, > 0, _) => "No current investments · correction needed",
            (false, _, > 0) => "Historical investments only",
            _ => "No current investments"
        };

        return new ClientInvestmentPosition(
            label,
            hasCurrentInvestments,
            totalCurrentValueZar,
            historicalAccountCount,
            correctionCount);
    }
}

public sealed record ClientSearchRequest(
    string? GlobalSearch = null,
    string? KanaanId = null,
    string? Name = null,
    string? Surname = null,
    string? Email = null,
    string? Phone = null,
    string? Status = null,
    string? InvestmentPosition = null,
    string SortColumn = "name",
    bool SortDescending = false);

public sealed record ClientSearchResult(
    int Id,
    string? KanaanId,
    string Name,
    string Surname,
    string? PrimaryEmail,
    string? PrimaryPhone,
    bool IsActive,
    string LifecycleStatus,
    string InvestmentPosition,
    bool HasCurrentInvestments,
    decimal? TotalCurrentValueZar,
    int HistoricalInvestmentAccountCount,
    int InvestmentStatusCorrectionCount);

public static class ClientInvestmentPositionFilters
{
    public const string Current = "Current";
    public const string NoCurrent = "NoCurrent";
    public const string HistoricalOnly = "HistoricalOnly";
    public const string NeedsCorrection = "NeedsCorrection";
}

internal sealed record ClientInvestmentPosition(
    string Label,
    bool HasCurrentInvestments,
    decimal? TotalCurrentValueZar,
    int HistoricalAccountCount,
    int StatusCorrectionCount);
