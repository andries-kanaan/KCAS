namespace KCAS.Admin.Data;

public sealed class DocumentPathDisplayService(IConfiguration configuration)
{
    private readonly string serverDrive = NormalizeDrive(configuration["DocumentPaths:ServerDrive"] ?? "E:");
    private readonly string workstationDrive = NormalizeDrive(configuration["DocumentPaths:WorkstationDrive"] ?? "Z:");

    public string? ForWorkstation(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        var trimmed = path.Trim();
        return trimmed.StartsWith(serverDrive + "\\", StringComparison.OrdinalIgnoreCase)
            ? workstationDrive + trimmed[serverDrive.Length..]
            : trimmed;
    }

    private static string NormalizeDrive(string value)
    {
        var trimmed = value.Trim().TrimEnd('\\');
        return trimmed.EndsWith(':') ? trimmed : trimmed + ":";
    }
}
