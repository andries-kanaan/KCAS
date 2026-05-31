using KCAS.Admin.Data;
using KCAS.Admin.LegacyImport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

var options = ImportOptions.Parse(args);
if (!options.IsValid)
{
    Console.Error.WriteLine("""
        Usage:
          KCAS.LegacyImport --legacy "<legacy connection>" --target "<target connection>" [--dry-run]

        Environment variable fallbacks:
          KCAS_LEGACY_CONNECTION
          KCAS_TARGET_CONNECTION
        """);
    return 2;
}

var startedAtUtc = DateTime.UtcNow;
await using var legacyConnection = new MySqlConnection(NormalizeLegacyConnectionString(options.LegacyConnectionString));
await legacyConnection.OpenAsync();

var targetOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseMySQL(options.TargetConnectionString)
    .Options;

await using var db = new ApplicationDbContext(targetOptions);
await db.Database.MigrateAsync();

var clientImported = 0;
var clientUpdated = 0;
var clientSkipped = 0;
var clientFailed = 0;

await foreach (var row in ReadLegacyRowsAsync(legacyConnection, "tbl_client"))
{
    Client mapped;
    try
    {
        mapped = LegacyClientImportMapper.Map(row, startedAtUtc);
    }
    catch (Exception ex)
    {
        clientFailed++;
        Console.Error.WriteLine($"Failed to map legacy client id '{ValueOrUnknown(row, "id")}': {ex.Message}");
        continue;
    }

    if (options.DryRun)
    {
        clientImported++;
        continue;
    }

    try
    {
        var existing = await db.Clients
            .Include(client => client.PersonalProfile)
            .Include(client => client.FinancialProfile)
            .Include(client => client.ContactPoints)
            .Include(client => client.Addresses)
            .Include(client => client.Relationships)
            .Include(client => client.LegacySnapshots)
            .SingleOrDefaultAsync(client => client.LegacyClientId == mapped.LegacyClientId);

        if (existing is null)
        {
            db.Clients.Add(mapped);
            clientImported++;
        }
        else
        {
            LegacyClientImportMapper.ApplyUpdatedGraph(existing, mapped);
            clientUpdated++;
        }

        await db.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        clientFailed++;
        db.ChangeTracker.Clear();
        Console.Error.WriteLine($"Failed to import legacy client id '{mapped.LegacyClientId}': {ex.Message}");
    }
}

var noteImported = 0;
var noteUpdated = 0;
var noteSkipped = 0;
var noteFailed = 0;
var pendingNoteChanges = 0;
const int noteSaveBatchSize = 250;

var clientsByLegacyId = await db.Clients
    .Where(client => client.LegacyClientId != null)
    .ToDictionaryAsync(client => client.LegacyClientId!.Value, client => client.Id);

var notesByLegacyId = await db.ClientNotes
    .Where(note => note.LegacyClientNoteId != null)
    .ToDictionaryAsync(note => note.LegacyClientNoteId!.Value);

await foreach (var row in ReadLegacyRowsAsync(legacyConnection, "tbl_clientnote"))
{
    var legacyClientId = ReadInt(row, "client_id");
    if (legacyClientId is null)
    {
        noteSkipped++;
        Console.Error.WriteLine($"Skipped legacy client note id '{ValueOrUnknown(row, "id")}' because it has no client_id.");
        continue;
    }

    if (!clientsByLegacyId.TryGetValue(legacyClientId.Value, out var clientId))
    {
        noteSkipped++;
        Console.Error.WriteLine($"Skipped legacy client note id '{ValueOrUnknown(row, "id")}' because legacy client '{legacyClientId}' was not imported.");
        continue;
    }

    ClientNote mapped;
    try
    {
        mapped = LegacyClientNoteImportMapper.Map(row, clientId, startedAtUtc);
    }
    catch (Exception ex)
    {
        noteFailed++;
        Console.Error.WriteLine($"Failed to map legacy client note id '{ValueOrUnknown(row, "id")}': {ex.Message}");
        continue;
    }

    if (options.DryRun)
    {
        noteImported++;
        continue;
    }

    try
    {
        if (!notesByLegacyId.TryGetValue(mapped.LegacyClientNoteId!.Value, out var existing))
        {
            db.ClientNotes.Add(mapped);
            notesByLegacyId[mapped.LegacyClientNoteId.Value] = mapped;
            noteImported++;
        }
        else
        {
            LegacyClientNoteImportMapper.ApplyUpdatedValues(existing, mapped);
            noteUpdated++;
        }

        pendingNoteChanges++;
        if (pendingNoteChanges >= noteSaveBatchSize)
        {
            await db.SaveChangesAsync();
            pendingNoteChanges = 0;
        }
    }
    catch (Exception ex)
    {
        noteFailed++;
        db.ChangeTracker.Clear();
        Console.Error.WriteLine($"Failed to import legacy client note id '{mapped.LegacyClientNoteId}': {ex.Message}");
    }
}

if (!options.DryRun && pendingNoteChanges > 0)
{
    await db.SaveChangesAsync();
}

var kycImported = 0;
var kycUpdated = 0;
var kycSkipped = 0;
var kycFailed = 0;
var pendingKycChanges = 0;
const int kycSaveBatchSize = 250;

var mainClassNamesById = await ReadLookupAsync(legacyConnection, "tbl_mainclass");
var subClassNamesById = await ReadLookupAsync(legacyConnection, "tbl_subclass");
var kycPoliciesByLegacyId = await db.ClientKycPolicies
    .ToDictionaryAsync(policy => policy.LegacyKycId);

await foreach (var row in ReadLegacyRowsAsync(legacyConnection, "tbl_kyc"))
{
    var legacyClientId = ReadInt(row, "client_id");
    if (legacyClientId is null)
    {
        kycSkipped++;
        Console.Error.WriteLine($"Skipped legacy KYC id '{ValueOrUnknown(row, "id")}' because it has no client_id.");
        continue;
    }

    if (!clientsByLegacyId.TryGetValue(legacyClientId.Value, out var clientId))
    {
        kycSkipped++;
        Console.Error.WriteLine($"Skipped legacy KYC id '{ValueOrUnknown(row, "id")}' because legacy client '{legacyClientId}' was not imported.");
        continue;
    }

    ClientKycPolicy mapped;
    try
    {
        mapped = LegacyKycImportMapper.Map(row, clientId, mainClassNamesById, subClassNamesById, startedAtUtc);
    }
    catch (Exception ex)
    {
        kycFailed++;
        Console.Error.WriteLine($"Failed to map legacy KYC id '{ValueOrUnknown(row, "id")}': {ex.Message}");
        continue;
    }

    if (options.DryRun)
    {
        kycImported++;
        continue;
    }

    try
    {
        if (!kycPoliciesByLegacyId.TryGetValue(mapped.LegacyKycId, out var existing))
        {
            db.ClientKycPolicies.Add(mapped);
            kycPoliciesByLegacyId[mapped.LegacyKycId] = mapped;
            kycImported++;
        }
        else
        {
            LegacyKycImportMapper.ApplyUpdatedValues(existing, mapped);
            kycUpdated++;
        }

        pendingKycChanges++;
        if (pendingKycChanges >= kycSaveBatchSize)
        {
            await db.SaveChangesAsync();
            pendingKycChanges = 0;
        }
    }
    catch (Exception ex)
    {
        kycFailed++;
        db.ChangeTracker.Clear();
        Console.Error.WriteLine($"Failed to import legacy KYC id '{mapped.LegacyKycId}': {ex.Message}");
    }
}

if (!options.DryRun && pendingKycChanges > 0)
{
    await db.SaveChangesAsync();
}

Console.WriteLine($"Legacy client import complete. Imported: {clientImported}; Updated: {clientUpdated}; Skipped: {clientSkipped}; Failed: {clientFailed}; Dry run: {options.DryRun}");
Console.WriteLine($"Legacy client note import complete. Imported: {noteImported}; Updated: {noteUpdated}; Skipped: {noteSkipped}; Failed: {noteFailed}; Dry run: {options.DryRun}");
Console.WriteLine($"Legacy KYC import complete. Imported: {kycImported}; Updated: {kycUpdated}; Skipped: {kycSkipped}; Failed: {kycFailed}; Dry run: {options.DryRun}");
return clientFailed == 0 && noteFailed == 0 && kycFailed == 0 ? 0 : 1;

static async IAsyncEnumerable<IReadOnlyDictionary<string, string?>> ReadLegacyRowsAsync(MySqlConnection connection, string tableName)
{
    await using var command = connection.CreateCommand();
    command.CommandText = $"SELECT * FROM `{tableName}` ORDER BY id";

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            row[reader.GetName(index)] = await reader.IsDBNullAsync(index)
                ? null
                : Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture);
        }

        yield return row;
    }
}

static async Task<Dictionary<int, string>> ReadLookupAsync(MySqlConnection connection, string tableName)
{
    var values = new Dictionary<int, string>();

    await foreach (var row in ReadLegacyRowsAsync(connection, tableName))
    {
        var id = ReadInt(row, "id");
        var name = row.TryGetValue("name", out var value) ? value : null;
        if (id is not null && !string.IsNullOrWhiteSpace(name))
        {
            values[id.Value] = name.Trim();
        }
    }

    return values;
}

static string ValueOrUnknown(IReadOnlyDictionary<string, string?> row, string key)
{
    return row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : "unknown";
}

static int? ReadInt(IReadOnlyDictionary<string, string?> row, string key)
{
    return row.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
        ? parsed
        : null;
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

internal sealed record ImportOptions(string LegacyConnectionString, string TargetConnectionString, bool DryRun)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(LegacyConnectionString) && !string.IsNullOrWhiteSpace(TargetConnectionString);

    public static ImportOptions Parse(string[] args)
    {
        var legacy = Environment.GetEnvironmentVariable("KCAS_LEGACY_CONNECTION") ?? string.Empty;
        var target = Environment.GetEnvironmentVariable("KCAS_TARGET_CONNECTION") ?? string.Empty;
        var dryRun = false;

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
                case "--dry-run":
                    dryRun = true;
                    break;
            }
        }

        return new ImportOptions(legacy, target, dryRun);
    }
}
