using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed record ClientFolderRecommendation(string Path, string FolderName, int Score, string Reason);

internal static class ClientFolderRecommendations
{
    public static async Task<List<ClientFolderRecommendation>> BuildAsync(
        ApplicationDbContext db,
        Client client,
        string? activeScanRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(activeScanRoot) || !Directory.Exists(activeScanRoot))
        {
            return [];
        }

        List<Client> relatedClients = string.IsNullOrWhiteSpace(client.KanaanId)
            ? []
            : await db.Clients.AsNoTracking()
                .Where(item => item.Id != client.Id && item.KanaanId == client.KanaanId)
                .ToListAsync(cancellationToken);
        var sourceTokens = BuildClientFolderTokens([client, .. relatedClients]);
        if (sourceTokens.Count == 0)
        {
            return [];
        }

        return Directory.EnumerateDirectories(activeScanRoot)
            .Select(path => ScoreFolderRecommendation(path, sourceTokens, client.SurnameOrEntityName))
            .Where(item => item.Score >= 50)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.FolderName)
            .Take(5)
            .Select(item => new ClientFolderRecommendation(
                item.Path,
                item.FolderName,
                item.Score,
                item.Reason))
            .ToList();
    }

    private static FolderRecommendationScore ScoreFolderRecommendation(
        string folderPath,
        IReadOnlySet<string> sourceTokens,
        string? surnameOrEntityName)
    {
        var folderName = Path.GetFileName(folderPath);
        var folderTokens = Tokenize(folderName);
        var score = 0;
        var reasons = new List<string>();
        var surnameTokens = Tokenize(surnameOrEntityName).Where(token => token.Length > 3).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matchedSurname = folderTokens.Intersect(surnameTokens, StringComparer.OrdinalIgnoreCase).ToList();
        if (matchedSurname.Count > 0)
        {
            score += 40;
            reasons.Add($"surname/entity match: {string.Join(", ", matchedSurname)}");
        }

        var exactMatches = folderTokens
            .Intersect(sourceTokens, StringComparer.OrdinalIgnoreCase)
            .Where(token => !surnameTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
        if (exactMatches.Count > 0)
        {
            score += exactMatches.Count * 10;
            reasons.Add($"exact token match: {string.Join(", ", exactMatches)}");
        }

        var prefixMatches = sourceTokens
            .Where(source => source.Length >= 2)
            .SelectMany(source => folderTokens
                .Where(folder => folder.Length >= 2 &&
                    !string.Equals(folder, source, StringComparison.OrdinalIgnoreCase) &&
                    folder.StartsWith(source, StringComparison.OrdinalIgnoreCase))
                .Select(folder => $"{source}->{folder}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        if (prefixMatches.Count > 0)
        {
            score += prefixMatches.Count * 8;
            reasons.Add($"initial/prefix match: {string.Join(", ", prefixMatches)}");
        }

        return new FolderRecommendationScore(
            folderPath,
            folderName,
            score,
            reasons.Count == 0 ? "Weak name match; verify before scanning." : string.Join("; ", reasons));
    }

    private static HashSet<string> BuildClientFolderTokens(IEnumerable<Client> clients)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var client in clients)
        {
            AddTokens(tokens, client.DisplayName);
            AddTokens(tokens, client.FullName);
            AddTokens(tokens, client.SurnameOrEntityName);
            AddTokens(tokens, client.Initials);
            AddTokens(tokens, LastPathSegment(client.ClientFolder));
        }

        return tokens;
    }

    private static void AddTokens(HashSet<string> tokens, string? value)
    {
        foreach (var token in Tokenize(value))
        {
            tokens.Add(token);
        }
    }

    private static HashSet<string> Tokenize(string? value)
    {
        var stopWords = new HashSet<string>(["and", "the", "clients", "client", "kanaan", "trust"], StringComparer.OrdinalIgnoreCase);
        return Regex.Matches(value ?? "", "[A-Za-z0-9]+")
            .Select(match => match.Value.ToUpperInvariant())
            .Where(token => token.Length > 0 && !stopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? LastPathSegment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.GetFileName(trimmed);
    }

    private sealed record FolderRecommendationScore(string Path, string FolderName, int Score, string Reason);
}
