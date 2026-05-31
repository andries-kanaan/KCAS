using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class ClientSearchService(ApplicationDbContext db)
{
    public async Task<List<ClientSearchResult>> SearchAsync(string? searchText, int take = 100)
    {
        return await SearchAsync(new ClientSearchRequest(GlobalSearch: searchText), take);
    }

    public async Task<List<ClientSearchResult>> SearchAsync(ClientSearchRequest request, int take = 500)
    {
        var normalizedQuery = request.GlobalSearch?.Trim();
        var kanaanId = request.KanaanId?.Trim();
        var name = request.Name?.Trim();
        var surname = request.Surname?.Trim();
        var email = request.Email?.Trim();
        var phone = request.Phone?.Trim();
        var status = request.Status?.Trim();

        var query = db.Clients
            .AsNoTracking()
            .Include(client => client.PersonalProfile)
            .Include(client => client.ContactPoints)
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
            if ("active".Contains(status, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(client => client.IsActive);
            }
            else if ("inactive".Contains(status, StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(client => !client.IsActive);
            }
        }

        var clients = await query.ToListAsync();

        var results = clients.Select(client => new ClientSearchResult(
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
            client.IsActive));

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
            ("status", true) => results.OrderByDescending(client => client.IsActive),
            ("status", false) => results.OrderBy(client => client.IsActive),
            _ => results.OrderBy(client => client.Surname).ThenBy(client => client.Name)
        };

        return results.Take(take).ToList();
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
    string SortColumn = "name",
    bool SortDescending = false);

public sealed record ClientSearchResult(int Id, string? KanaanId, string Name, string Surname, string? PrimaryEmail, string? PrimaryPhone, bool IsActive);
