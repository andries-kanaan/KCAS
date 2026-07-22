using KCAS.Admin.Data;
using KCAS.Admin.LegacyImport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

var options = ImportOptions.Parse(args);
if (!options.IsValid)
{
    Console.Error.WriteLine("""
        Usage:
          KCAS.LegacyImport --legacy "<legacy connection>" --target "<target connection>" --source-snapshot-sha256 <sha256> [--scan]
          KCAS.LegacyImport --legacy "<legacy connection>" --target "<target connection>" --source-snapshot-sha256 <sha256> --apply-new --approved-scan-run <id>

        Safe defaults:
          --scan is the default and changes only reconciliation metadata.
          --apply-new adds legacy IDs that do not exist in KCAS; it never updates or deletes existing business rows.

        Environment variable fallbacks:
          KCAS_LEGACY_CONNECTION
          KCAS_TARGET_CONNECTION
          KCAS_SOURCE_SNAPSHOT_SHA256
          KCAS_SOURCE_SNAPSHOT_FILE_NAME
        """);
    return 2;
}

await using var legacyConnection = new MySqlConnection(NormalizeLegacyConnectionString(options.LegacyConnectionString));
await legacyConnection.OpenAsync();

var targetOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseMySQL(options.TargetConnectionString)
    .Options;
await using var db = new ApplicationDbContext(targetOptions);
var pendingMigrations = (await db.Database.GetPendingMigrationsAsync()).ToArray();
if (pendingMigrations.Length > 0)
{
    Console.Error.WriteLine($"Target KCAS database has {pendingMigrations.Length} pending migration(s). Deploy reviewed database migrations before importing.");
    return 2;
}

Dictionary<(string Table, long Id), string>? approvedNewRows = null;
if (options.ApplyNew)
{
    var approvedScan = await db.LegacyImportRuns
        .AsNoTracking()
        .Include(run => run.Rows)
        .SingleOrDefaultAsync(run => run.Id == options.ApprovedScanRunId);
    if (approvedScan is null)
    {
        Console.Error.WriteLine($"Approved scan run '{options.ApprovedScanRunId}' does not exist.");
        return 2;
    }
    try
    {
        approvedNewRows = LegacyImportApprovalValidator.GetApprovedNewRows(approvedScan, legacyConnection.Database, options.SourceSnapshotSha256);
    }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
}

var recorder = await LegacyImportRunRecorder.StartAsync(
    db,
    options.Mode,
    legacyConnection.Database,
    options.SourceSnapshotSha256,
    options.SourceSnapshotFileName,
    options.ApprovedScanRunId);
var importer = new IncrementalLegacyImporter(db, legacyConnection, recorder, approvedNewRows);

try
{
    var failed = await importer.ExecuteAsync();
    await recorder.CompleteAsync(failed);
    PrintSummary(recorder.Run);
    return failed == 0 ? 0 : 1;
}
catch (Exception ex)
{
    var failureMessage = ex.GetBaseException().Message;
    await recorder.FailAsync(failureMessage);
    Console.Error.WriteLine($"Incremental legacy import run {recorder.Run.Id} failed: {failureMessage}");
    return 1;
}

static string NormalizeLegacyConnectionString(string connectionString)
{
    var builder = new MySqlConnectionStringBuilder(connectionString)
    {
        AllowZeroDateTime = true,
        ConvertZeroDateTime = true
    };
    return builder.ConnectionString;
}

static void PrintSummary(LegacyImportRun run)
{
    Console.WriteLine($"Legacy reconciliation run {run.Id} complete. Mode: {run.Mode}; Status: {run.Status}");
    Console.WriteLine($"New: {run.NewCount}; Applied: {run.AppliedCount}; Unchanged: {run.UnchangedCount}; Changed pending review: {run.ChangedCount}; Missing pending review: {run.MissingCount}; Invalid: {run.InvalidCount}; Orphaned: {run.OrphanedCount}; Failed: {run.FailedCount}");
}

internal sealed record ImportOptions(
    string LegacyConnectionString,
    string TargetConnectionString,
    string Mode,
    string SourceSnapshotSha256,
    string? SourceSnapshotFileName,
    long? ApprovedScanRunId)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(LegacyConnectionString) &&
        !string.IsNullOrWhiteSpace(TargetConnectionString) &&
        (Mode is LegacyImportModes.Scan or LegacyImportModes.ApplyNew) &&
        System.Text.RegularExpressions.Regex.IsMatch(SourceSnapshotSha256, "^[0-9a-fA-F]{64}$") &&
        (Mode != LegacyImportModes.ApplyNew || ApprovedScanRunId > 0);

    public bool ApplyNew => Mode == LegacyImportModes.ApplyNew;

    public static ImportOptions Parse(string[] args)
    {
        var legacy = Environment.GetEnvironmentVariable("KCAS_LEGACY_CONNECTION") ?? string.Empty;
        var target = Environment.GetEnvironmentVariable("KCAS_TARGET_CONNECTION") ?? string.Empty;
        var snapshotSha256 = Environment.GetEnvironmentVariable("KCAS_SOURCE_SNAPSHOT_SHA256") ?? string.Empty;
        var snapshotFileName = Environment.GetEnvironmentVariable("KCAS_SOURCE_SNAPSHOT_FILE_NAME");
        long? approvedScanRunId = null;
        var mode = LegacyImportModes.Scan;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--legacy" when index + 1 < args.Length:
                    legacy = args[++index];
                    break;
                case "--target" when index + 1 < args.Length:
                    target = args[++index];
                    break;
                case "--source-snapshot-sha256" when index + 1 < args.Length:
                    snapshotSha256 = args[++index];
                    break;
                case "--source-snapshot-file-name" when index + 1 < args.Length:
                    snapshotFileName = args[++index];
                    break;
                case "--approved-scan-run" when index + 1 < args.Length && long.TryParse(args[index + 1], out var runId):
                    approvedScanRunId = runId;
                    index++;
                    break;
                case "--scan" or "--dry-run":
                    mode = LegacyImportModes.Scan;
                    break;
                case "--apply-new":
                    mode = LegacyImportModes.ApplyNew;
                    break;
            }
        }

        return new ImportOptions(legacy, target, mode, snapshotSha256.ToLowerInvariant(), snapshotFileName, approvedScanRunId);
    }
}
