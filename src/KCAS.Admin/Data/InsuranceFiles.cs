using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;

namespace KCAS.Admin.Data;

public sealed class InsuranceFiles(IConfiguration configuration)
{
    public string Root => configuration["Insurance:ComplianceRoot"] ??
        (Directory.Exists(@"C:\Download\_kanaan\Compliance") ? @"C:\Download\_kanaan\Compliance" : @"E:\Userdata\Kanaan Trust\Compliance");

    public string Relative(string path)
    {
        var value = path.Trim().Replace('\\', '/');
        var root = Root.Replace('\\', '/').TrimEnd('/');
        if (value.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase)) value = "Compliance/" + value[(root.Length + 1)..];
        var parts = value.Split('/');
        if (parts.Length < 4 || !parts[0].Equals("Compliance", StringComparison.OrdinalIgnoreCase) ||
            !parts[1].Equals("PI Cover", StringComparison.OrdinalIgnoreCase) ||
            parts.Any(x => string.IsNullOrWhiteSpace(x) || x is "." or ".." || x.IndexOfAny([':', '\0', '*', '?']) >= 0))
            throw new ValidationException("Use a relative file path under Compliance\\PI Cover; parent paths and network paths are not allowed.");
        return "Compliance/PI Cover/" + string.Join('/', parts.Skip(2));
    }

    public string Resolve(string relative)
    {
        var parts = Relative(relative).Split('/').Skip(1).ToArray();
        var path = Path.Combine(new[] { Root }.Concat(parts).ToArray());
        // A junction/symlink must not let a permitted relative name escape the configured evidence tree.
        var current = Path.GetFullPath(Root);
        if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new ValidationException("The configured evidence root cannot be a symbolic link or junction.");
        foreach (var part in parts)
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ValidationException("Evidence paths cannot traverse symbolic links or junctions.");
        }
        return path;
    }

    public async Task<string> FingerprintAsync(string relative, CancellationToken ct = default)
    {
        var path = Resolve(relative);
        if (!File.Exists(path)) throw new ValidationException($"Linked file is missing: {relative}");
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }
}
