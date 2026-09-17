namespace KCAS.Admin.Data;

public static class ClientEvidenceFileResolver
{
    public static string? ResolveExistingPath(
        string? sourcePath,
        string? relativePath,
        string? fileName,
        string? clientFolder,
        string? activeServerRoot)
    {
        foreach (var candidate in CandidatePaths(sourcePath, relativePath, fileName, clientFolder, activeServerRoot))
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static string? PreferredServerPath(
        string? sourcePath,
        string? relativePath,
        string? fileName,
        string? clientFolder,
        string? activeServerRoot)
        => CandidatePaths(sourcePath, relativePath, fileName, clientFolder, activeServerRoot)
            .FirstOrDefault(path => !string.Equals(path, sourcePath, StringComparison.OrdinalIgnoreCase))
           ?? NormalizeOrNull(sourcePath);

    private static IEnumerable<string> CandidatePaths(
        string? sourcePath,
        string? relativePath,
        string? fileName,
        string? clientFolder,
        string? activeServerRoot)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in BuildCandidates())
        {
            var normalized = NormalizeOrNull(candidate);
            if (normalized is not null && seen.Add(normalized)) yield return normalized;
        }

        IEnumerable<string?> BuildCandidates()
        {
            yield return sourcePath;

            if (!string.IsNullOrWhiteSpace(activeServerRoot) && !string.IsNullOrWhiteSpace(sourcePath))
            {
                yield return ClientReviewTransferService.MapClientFolderToLiveRoot(sourcePath, activeServerRoot);
            }

            var mappedClientFolder = string.IsNullOrWhiteSpace(activeServerRoot)
                ? NormalizeOrNull(clientFolder)
                : ClientReviewTransferService.MapClientFolderToLiveRoot(clientFolder, activeServerRoot);
            foreach (var folder in new[] { mappedClientFolder, NormalizeOrNull(clientFolder) })
            {
                yield return CombineWithinFolder(folder, relativePath);
                yield return CombineWithinFolder(folder, fileName);
            }
        }
    }

    private static string? CombineWithinFolder(string? folder, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathFullyQualified(relativePath))
        {
            return null;
        }

        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    private static string? NormalizeOrNull(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim());
}
