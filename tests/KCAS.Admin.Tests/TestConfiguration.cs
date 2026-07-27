using System.Text.Json;

namespace KCAS.Admin.Tests;

public static class TestConfiguration
{
    private const string DefaultTestDatabaseName = "kcas_blazor_test";

    public static string GetConnectionString()
    {
        var explicitConnectionString = Environment.GetEnvironmentVariable("KCAS_TEST_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return RequireDedicatedTestDatabase(explicitConnectionString);
        }

        var appSettingsPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "src",
            "KCAS.Admin",
            "appsettings.Development.json"));

        if (!File.Exists(appSettingsPath))
        {
            throw new InvalidOperationException(
                "Set KCAS_TEST_CONNECTION_STRING or create src/KCAS.Admin/appsettings.Development.json.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
        var connectionString = document.RootElement
            .GetProperty("ConnectionStrings")
            .GetProperty("DefaultConnection")
            .GetString();

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Development appsettings does not contain ConnectionStrings:DefaultConnection.");
        }

        return RequireDedicatedTestDatabase(
            ReplaceDatabase(connectionString, DefaultTestDatabaseName));
    }

    private static string ReplaceDatabase(string connectionString, string databaseName)
    {
        var parts = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .ToDictionary(part => part[0], part => part[1], StringComparer.OrdinalIgnoreCase);

        parts["database"] = databaseName;
        return string.Join(';', parts.Select(part => $"{part.Key}={part.Value}"));
    }

    internal static string RequireDedicatedTestDatabase(string connectionString)
    {
        var databaseName = connectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2))
            .Where(part => part.Length == 2)
            .Where(part =>
                part[0].Equals("database", StringComparison.OrdinalIgnoreCase) ||
                part[0].Equals("initial catalog", StringComparison.OrdinalIgnoreCase))
            .Select(part => part[1].Trim())
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(databaseName) ||
            !databaseName.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "KCAS tests may only use a database whose name contains 'test'. " +
                $"Refusing database '{databaseName ?? "(not specified)"}'.");
        }

        return connectionString;
    }
}
