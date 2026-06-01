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
    .Where(policy => policy.LegacyKycId.HasValue)
    .ToDictionaryAsync(policy => policy.LegacyKycId!.Value);

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
        if (!kycPoliciesByLegacyId.TryGetValue(mapped.LegacyKycId!.Value, out var existing))
        {
            db.ClientKycPolicies.Add(mapped);
            kycPoliciesByLegacyId[mapped.LegacyKycId.Value] = mapped;
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

var investmentAccountImported = 0;
var investmentAccountUpdated = 0;
var investmentAccountSkipped = 0;
var investmentAccountFailed = 0;
var pendingInvestmentAccountChanges = 0;
const int investmentAccountSaveBatchSize = 250;

var investmentAccountsByLegacyId = await db.ClientInvestmentAccounts
    .ToDictionaryAsync(account => account.LegacyInvestmentAccountId);

await foreach (var row in ReadLegacyRowsAsync(legacyConnection, "tbl_investmentaccount"))
{
    var legacyClientId = ReadInt(row, "client_id");
    if (legacyClientId is null)
    {
        investmentAccountSkipped++;
        Console.Error.WriteLine($"Skipped legacy investment account id '{ValueOrUnknown(row, "id")}' because it has no client_id.");
        continue;
    }

    if (!clientsByLegacyId.TryGetValue(legacyClientId.Value, out var clientId))
    {
        investmentAccountSkipped++;
        Console.Error.WriteLine($"Skipped legacy investment account id '{ValueOrUnknown(row, "id")}' because legacy client '{legacyClientId}' was not imported.");
        continue;
    }

    ClientInvestmentAccount mapped;
    try
    {
        mapped = LegacyInvestmentAccountImportMapper.Map(row, clientId, startedAtUtc);
    }
    catch (Exception ex)
    {
        investmentAccountFailed++;
        Console.Error.WriteLine($"Failed to map legacy investment account id '{ValueOrUnknown(row, "id")}': {ex.Message}");
        continue;
    }

    if (options.DryRun)
    {
        investmentAccountImported++;
        continue;
    }

    try
    {
        if (!investmentAccountsByLegacyId.TryGetValue(mapped.LegacyInvestmentAccountId, out var existing))
        {
            db.ClientInvestmentAccounts.Add(mapped);
            investmentAccountsByLegacyId[mapped.LegacyInvestmentAccountId] = mapped;
            investmentAccountImported++;
        }
        else
        {
            LegacyInvestmentAccountImportMapper.ApplyUpdatedValues(existing, mapped);
            investmentAccountUpdated++;
        }

        pendingInvestmentAccountChanges++;
        if (pendingInvestmentAccountChanges >= investmentAccountSaveBatchSize)
        {
            await db.SaveChangesAsync();
            pendingInvestmentAccountChanges = 0;
        }
    }
    catch (Exception ex)
    {
        investmentAccountFailed++;
        db.ChangeTracker.Clear();
        Console.Error.WriteLine($"Failed to import legacy investment account id '{mapped.LegacyInvestmentAccountId}': {ex.Message}");
    }
}

if (!options.DryRun && pendingInvestmentAccountChanges > 0)
{
    await db.SaveChangesAsync();
}

investmentAccountsByLegacyId = await db.ClientInvestmentAccounts
    .ToDictionaryAsync(account => account.LegacyInvestmentAccountId);

var investmentTransactionImported = 0;
var investmentTransactionUpdated = 0;
var investmentTransactionSkipped = 0;
var investmentTransactionFailed = 0;
var pendingInvestmentTransactionChanges = 0;
const int investmentTransactionSaveBatchSize = 250;

var investmentTransactionsByLegacyId = await db.ClientInvestmentTransactions
    .ToDictionaryAsync(transaction => transaction.LegacyInvestmentHistoryId);

await foreach (var row in ReadLegacyRowsAsync(legacyConnection, "tbl_investmenthistory"))
{
    var legacyInvestmentAccountId = ReadInt(row, "ia_id");
    if (legacyInvestmentAccountId is null)
    {
        investmentTransactionSkipped++;
        Console.Error.WriteLine($"Skipped legacy investment history id '{ValueOrUnknown(row, "id")}' because it has no ia_id.");
        continue;
    }

    if (!investmentAccountsByLegacyId.TryGetValue(legacyInvestmentAccountId.Value, out var account))
    {
        investmentTransactionSkipped++;
        Console.Error.WriteLine($"Skipped legacy investment history id '{ValueOrUnknown(row, "id")}' because legacy investment account '{legacyInvestmentAccountId}' was not imported.");
        continue;
    }

    ClientInvestmentTransaction mapped;
    try
    {
        mapped = LegacyInvestmentTransactionImportMapper.Map(row, account.Id, startedAtUtc);
    }
    catch (Exception ex)
    {
        investmentTransactionFailed++;
        Console.Error.WriteLine($"Failed to map legacy investment history id '{ValueOrUnknown(row, "id")}': {ex.Message}");
        continue;
    }

    if (options.DryRun)
    {
        investmentTransactionImported++;
        continue;
    }

    try
    {
        if (!investmentTransactionsByLegacyId.TryGetValue(mapped.LegacyInvestmentHistoryId, out var existing))
        {
            db.ClientInvestmentTransactions.Add(mapped);
            investmentTransactionsByLegacyId[mapped.LegacyInvestmentHistoryId] = mapped;
            investmentTransactionImported++;
        }
        else
        {
            LegacyInvestmentTransactionImportMapper.ApplyUpdatedValues(existing, mapped);
            investmentTransactionUpdated++;
        }

        pendingInvestmentTransactionChanges++;
        if (pendingInvestmentTransactionChanges >= investmentTransactionSaveBatchSize)
        {
            await db.SaveChangesAsync();
            pendingInvestmentTransactionChanges = 0;
        }
    }
    catch (Exception ex)
    {
        investmentTransactionFailed++;
        db.ChangeTracker.Clear();
        Console.Error.WriteLine($"Failed to import legacy investment history id '{mapped.LegacyInvestmentHistoryId}': {ex.Message}");
    }
}

if (!options.DryRun && pendingInvestmentTransactionChanges > 0)
{
    await db.SaveChangesAsync();
}

var fundValuationImported = 0;
var fundValuationUpdated = 0;
var fundValuationSkipped = 0;
var fundValuationFailed = 0;
var pendingFundValuationChanges = 0;
const int fundValuationSaveBatchSize = 250;

var fundValuationsByLegacyId = await db.ClientFundValuations
    .ToDictionaryAsync(valuation => valuation.LegacyFundId);

await foreach (var row in ReadLegacyRowsAsync(legacyConnection, "tbl_fund"))
{
    var legacyClientId = ReadInt(row, "client_id");
    if (legacyClientId is null)
    {
        fundValuationSkipped++;
        Console.Error.WriteLine($"Skipped legacy fund id '{ValueOrUnknown(row, "id")}' because it has no client_id.");
        continue;
    }

    if (!clientsByLegacyId.TryGetValue(legacyClientId.Value, out var clientId))
    {
        fundValuationSkipped++;
        Console.Error.WriteLine($"Skipped legacy fund id '{ValueOrUnknown(row, "id")}' because legacy client '{legacyClientId}' was not imported.");
        continue;
    }

    ClientFundValuation mapped;
    try
    {
        mapped = LegacyFundValuationImportMapper.Map(row, clientId, startedAtUtc);
    }
    catch (Exception ex)
    {
        fundValuationFailed++;
        Console.Error.WriteLine($"Failed to map legacy fund id '{ValueOrUnknown(row, "id")}': {ex.Message}");
        continue;
    }

    if (options.DryRun)
    {
        fundValuationImported++;
        continue;
    }

    try
    {
        if (!fundValuationsByLegacyId.TryGetValue(mapped.LegacyFundId, out var existing))
        {
            db.ClientFundValuations.Add(mapped);
            fundValuationsByLegacyId[mapped.LegacyFundId] = mapped;
            fundValuationImported++;
        }
        else
        {
            LegacyFundValuationImportMapper.ApplyUpdatedValues(existing, mapped);
            fundValuationUpdated++;
        }

        pendingFundValuationChanges++;
        if (pendingFundValuationChanges >= fundValuationSaveBatchSize)
        {
            await db.SaveChangesAsync();
            pendingFundValuationChanges = 0;
        }
    }
    catch (Exception ex)
    {
        fundValuationFailed++;
        db.ChangeTracker.Clear();
        Console.Error.WriteLine($"Failed to import legacy fund id '{mapped.LegacyFundId}': {ex.Message}");
    }
}

if (!options.DryRun && pendingFundValuationChanges > 0)
{
    await db.SaveChangesAsync();
}

Console.WriteLine($"Legacy client import complete. Imported: {clientImported}; Updated: {clientUpdated}; Skipped: {clientSkipped}; Failed: {clientFailed}; Dry run: {options.DryRun}");
Console.WriteLine($"Legacy client note import complete. Imported: {noteImported}; Updated: {noteUpdated}; Skipped: {noteSkipped}; Failed: {noteFailed}; Dry run: {options.DryRun}");
Console.WriteLine($"Legacy KYC import complete. Imported: {kycImported}; Updated: {kycUpdated}; Skipped: {kycSkipped}; Failed: {kycFailed}; Dry run: {options.DryRun}");
Console.WriteLine($"Legacy investment account import complete. Imported: {investmentAccountImported}; Updated: {investmentAccountUpdated}; Skipped: {investmentAccountSkipped}; Failed: {investmentAccountFailed}; Dry run: {options.DryRun}");
Console.WriteLine($"Legacy investment history import complete. Imported: {investmentTransactionImported}; Updated: {investmentTransactionUpdated}; Skipped: {investmentTransactionSkipped}; Failed: {investmentTransactionFailed}; Dry run: {options.DryRun}");
Console.WriteLine($"Legacy fund valuation import complete. Imported: {fundValuationImported}; Updated: {fundValuationUpdated}; Skipped: {fundValuationSkipped}; Failed: {fundValuationFailed}; Dry run: {options.DryRun}");
return clientFailed == 0 && noteFailed == 0 && kycFailed == 0 && investmentAccountFailed == 0 && investmentTransactionFailed == 0 && fundValuationFailed == 0 ? 0 : 1;

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
