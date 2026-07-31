using System.Text.RegularExpressions;
using KCAS.Admin.Data;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace KCAS.Admin.LegacyImport;

public static class LegacyImportStagingLifecycle
{
    private static readonly Regex StagedDatabaseName = new(
        "^kcas_legacy_stage_[0-9a-f]{12}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsStagedDatabase(string database)
        => !string.IsNullOrWhiteSpace(database) && StagedDatabaseName.IsMatch(database);

    public static bool CanActivate(LegacyImportRun run)
        => run.Mode == LegacyImportModes.Scan &&
           run.Status is LegacyImportRunStatuses.Completed or LegacyImportRunStatuses.AwaitingReview;

    public static IReadOnlyList<string> InactiveDatabases(IEnumerable<string> databases, string activeDatabase)
    {
        if (!IsStagedDatabase(activeDatabase))
        {
            throw new InvalidOperationException($"Unsupported active legacy staging database '{activeDatabase}'.");
        }

        return databases
            .Where(IsStagedDatabase)
            .Where(database => !string.Equals(database, activeDatabase, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(database => database, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task ActivateAsync(
        ApplicationDbContext db,
        string serverConnectionString,
        long activeScanRunId,
        string activeDatabase,
        CancellationToken cancellationToken = default)
    {
        if (!IsStagedDatabase(activeDatabase))
        {
            throw new InvalidOperationException($"Unsupported active legacy staging database '{activeDatabase}'.");
        }
        var activeRun = await db.LegacyImportRuns
            .SingleAsync(run => run.Id == activeScanRunId, cancellationToken);
        if (!CanActivate(activeRun))
        {
            throw new InvalidOperationException($"Legacy scan run #{activeScanRunId} is not eligible to become the active snapshot.");
        }

        var priorScans = await db.LegacyImportRuns
            .Where(run => run.Id != activeScanRunId)
            .Where(run => run.Mode == LegacyImportModes.Scan)
            .Where(run => run.Status == LegacyImportRunStatuses.Completed ||
                          run.Status == LegacyImportRunStatuses.AwaitingReview)
            .ToListAsync(cancellationToken);
        foreach (var run in priorScans)
        {
            run.Status = LegacyImportRunStatuses.Superseded;
        }
        await db.SaveChangesAsync(cancellationToken);

        var builder = new MySqlConnectionStringBuilder(serverConnectionString)
        {
            Database = string.Empty
        };
        await using var serverConnection = new MySqlConnection(builder.ConnectionString);
        await serverConnection.OpenAsync(cancellationToken);

        var listCommand = serverConnection.CreateCommand();
        listCommand.CommandText = """
            SELECT schema_name
            FROM information_schema.schemata
            WHERE schema_name LIKE 'kcas\_legacy\_stage\_%' ESCAPE '\\';
            """;
        var databases = new List<string>();
        await using (var reader = await listCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                databases.Add(reader.GetString(0));
            }
        }

        foreach (var database in InactiveDatabases(databases, activeDatabase))
        {
            var dropCommand = serverConnection.CreateCommand();
            dropCommand.CommandText = $"DROP DATABASE `{database}`;";
            await dropCommand.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
