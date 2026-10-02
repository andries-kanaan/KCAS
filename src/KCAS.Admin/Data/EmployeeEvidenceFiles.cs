using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;

namespace KCAS.Admin.Data;

public sealed class EmployeeEvidenceFiles(IConfiguration configuration)
{
    public string Root => configuration["EmployeeCompliance:EvidenceRoot"]
        ?? (OperatingSystem.IsWindows() && Directory.Exists(@"C:\Download\_kanaan\Compliance")
            ? @"C:\Download\_kanaan\Compliance" : @"E:\Userdata\Kanaan Trust\Compliance");

    public string Resolve(string reference)
    {
        reference = reference.Trim();
        if (reference.Length == 0) throw new ValidationException("An evidence path is required.");
        // Windows drive parsing is independent of the host filesystem.
        var normalized = reference.Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(x => x is "." or "..")) throw new ValidationException("Evidence cannot traverse folders.");
        var marker = normalized.IndexOf("/Compliance/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) normalized = normalized[(marker + "/Compliance/".Length)..];
        else if (normalized.StartsWith("Compliance/", StringComparison.OrdinalIgnoreCase)) normalized = normalized[11..];
        else if (IsWindowsAbsolute(normalized) || Path.IsPathFullyQualified(normalized))
        {
            var full = Path.GetFullPath(reference);
            var root = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
                throw new ValidationException("Employee evidence must be within the configured Compliance evidence root.");
            normalized = Path.GetRelativePath(root, full);
        }
        if (normalized.Contains(':')) throw new ValidationException("Invalid evidence path.");
        var target = Path.GetFullPath(Path.Combine(Root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var resolvedRoot = Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar);
        if (!target.StartsWith(resolvedRoot + Path.DirectorySeparatorChar, PathComparison))
            throw new ValidationException("Employee evidence must be within the configured Compliance evidence root.");
        // Reject reparse points/symlinks rather than serving a file outside the root.
        for (var current = new FileInfo(target) as FileSystemInfo; current is not null; current = current is FileInfo f ? f.Directory : ((DirectoryInfo)current).Parent)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new ValidationException("Linked employee evidence cannot use symbolic links or reparse points.");
            if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar), resolvedRoot, PathComparison)) break;
        }
        return target;
    }

    public async Task<string> HashAsync(string reference)
    {
        var path = Resolve(reference);
        if (!File.Exists(path)) throw new ValidationException("The employee evidence file does not exist at the configured Compliance root.");
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static bool IsWindowsAbsolute(string path) => path.StartsWith("//") || (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '/');
}
