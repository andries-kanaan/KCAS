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

await ImportReferenceDataAsync(db, legacyConnection, options.DryRun);

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
    .Where(account => account.LegacyInvestmentAccountId.HasValue)
    .ToDictionaryAsync(account => account.LegacyInvestmentAccountId!.Value);

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
        if (!investmentAccountsByLegacyId.TryGetValue(mapped.LegacyInvestmentAccountId!.Value, out var existing))
        {
            db.ClientInvestmentAccounts.Add(mapped);
            investmentAccountsByLegacyId[mapped.LegacyInvestmentAccountId!.Value] = mapped;
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
    .Where(account => account.LegacyInvestmentAccountId.HasValue)
    .ToDictionaryAsync(account => account.LegacyInvestmentAccountId!.Value);

var investmentTransactionImported = 0;
var investmentTransactionUpdated = 0;
var investmentTransactionSkipped = 0;
var investmentTransactionFailed = 0;
var pendingInvestmentTransactionChanges = 0;
const int investmentTransactionSaveBatchSize = 250;

var investmentTransactionsByLegacyId = await db.ClientInvestmentTransactions
    .Where(transaction => transaction.LegacyInvestmentHistoryId.HasValue)
    .ToDictionaryAsync(transaction => transaction.LegacyInvestmentHistoryId!.Value);

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
        if (!investmentTransactionsByLegacyId.TryGetValue(mapped.LegacyInvestmentHistoryId!.Value, out var existing))
        {
            db.ClientInvestmentTransactions.Add(mapped);
            investmentTransactionsByLegacyId[mapped.LegacyInvestmentHistoryId!.Value] = mapped;
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

static async Task ImportReferenceDataAsync(ApplicationDbContext db, MySqlConnection legacyConnection, bool dryRun)
{
    var productTypeCount = 0;
    var administratorCount = 0;
    var mainClassCount = 0;
    var subClassCount = 0;
    var fundCount = 0;
    var marketValueCount = 0;

    var productTypesByLegacyId = await db.InvestmentProductTypeReferences
        .Where(reference => reference.LegacyCompanyProductId.HasValue)
        .ToDictionaryAsync(reference => reference.LegacyCompanyProductId!.Value);

    await foreach (var row in ReadLegacyRowsAsync(legacyConnection, "tbl_companyproduct"))
    {
        var legacyId = ReadInt(row, "id");
        var name = ReadString(row, "name");
        if (legacyId is null || string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        productTypeCount++;
        if (dryRun)
        {
            continue;
        }

        if (!productTypesByLegacyId.TryGetValue(legacyId.Value, out var reference))
        {
            reference = new InvestmentProductTypeReference { LegacyCompanyProductId = legacyId.Value };
            db.InvestmentProductTypeReferences.Add(reference);
            productTypesByLegacyId[legacyId.Value] = reference;
        }

        reference.Name = name;
        ApplyLegacyAudit(reference, row);
    }

    var administratorsByLegacyId = await db.InvestmentAdministratorReferences
        .Where(reference => reference.LegacyLispId.HasValue)
        .ToDictionaryAsync(reference => reference.LegacyLispId!.Value);

    await foreach (var row in ReadLegacyRowsAsync(legacyConnection, "tbl_lispname"))
    {
        var legacyId = ReadInt(row, "id");
        var name = ReadString(row, "name");
        if (legacyId is null || string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        administratorCount++;
        if (dryRun)
        {
            continue;
        }

        if (!administratorsByLegacyId.TryGetValue(legacyId.Value, out var reference))
        {
            reference = new InvestmentAdministratorReference { LegacyLispId = legacyId.Value };
            db.InvestmentAdministratorReferences.Add(reference);
            administratorsByLegacyId[legacyId.Value] = reference;
        }

        reference.Name = name;
        reference.ShortName = ReadString(row, "short_name");
        reference.IsCurrent = ReadBool(row, "current_lisp") ?? true;
        reference.MonthlyUpload = ReadBool(row, "monthly_upload") ?? false;
        ApplyLegacyAudit(reference, row);
    }

    var mainClassesByLegacyId = await db.KycMainClassReferences
        .Where(reference => reference.LegacyMainClassId.HasValue)
        .ToDictionaryAsync(reference => reference.LegacyMainClassId!.Value);

    await foreach (var row in ReadLegacyRowsAsync(legacyConnection, "tbl_mainclass"))
    {
        var legacyId = ReadInt(row, "id");
        var name = ReadString(row, "name");
        if (legacyId is null || string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        mainClassCount++;
        if (dryRun)
        {
            continue;
        }

        if (!mainClassesByLegacyId.TryGetValue(legacyId.Value, out var reference))
        {
            reference = new KycMainClassReference { LegacyMainClassId = legacyId.Value };
            db.KycMainClassReferences.Add(reference);
            mainClassesByLegacyId[legacyId.Value] = reference;
        }

        reference.Name = name;
        reference.AfrikaansDescription = StripTags(ReadString(row, "afullname"));
        reference.EnglishDescription = StripTags(ReadString(row, "efullname"));
        ApplyLegacyAudit(reference, row);
    }

    if (!dryRun)
    {
        await db.SaveChangesAsync();
        mainClassesByLegacyId = await db.KycMainClassReferences
            .Where(reference => reference.LegacyMainClassId.HasValue)
            .ToDictionaryAsync(reference => reference.LegacyMainClassId!.Value);
    }

    var subClassesByLegacyId = await db.KycSubClassReferences
        .Where(reference => reference.LegacySubClassId.HasValue)
        .ToDictionaryAsync(reference => reference.LegacySubClassId!.Value);

    await foreach (var row in ReadLegacyRowsAsync(legacyConnection, "tbl_subclass"))
    {
        var legacyId = ReadInt(row, "id");
        var legacyMainClassId = ReadInt(row, "mainclass_id");
        var name = ReadString(row, "name");
        if (legacyId is null || legacyMainClassId is null || string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        var mainClassReferenceId = 0;
        if (!dryRun)
        {
            if (!mainClassesByLegacyId.TryGetValue(legacyMainClassId.Value, out var mainClass))
            {
                continue;
            }

            mainClassReferenceId = mainClass.Id;
        }

        if (!dryRun && mainClassReferenceId == 0)
        {
            continue;
        }

        subClassCount++;
        if (dryRun)
        {
            continue;
        }

        if (!subClassesByLegacyId.TryGetValue(legacyId.Value, out var reference))
        {
            reference = new KycSubClassReference { LegacySubClassId = legacyId.Value };
            db.KycSubClassReferences.Add(reference);
            subClassesByLegacyId[legacyId.Value] = reference;
        }

        reference.KycMainClassReferenceId = mainClassReferenceId;
        reference.LegacyMainClassId = legacyMainClassId.Value;
        reference.Name = name;
        ApplyLegacyAudit(reference, row);
    }

    var fundsByLegacyId = await db.InvestmentFundReferences
        .Where(reference => reference.LegacyFundNameId.HasValue)
        .ToDictionaryAsync(reference => reference.LegacyFundNameId!.Value);

    await foreach (var row in ReadLegacyRowsAsync(legacyConnection, "tbl_fundname"))
    {
        var legacyId = ReadInt(row, "id");
        var name = ReadString(row, "name");
        if (legacyId is null || string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        fundCount++;
        if (dryRun)
        {
            continue;
        }

        if (!fundsByLegacyId.TryGetValue(legacyId.Value, out var reference))
        {
            reference = new InvestmentFundReference { LegacyFundNameId = legacyId.Value };
            db.InvestmentFundReferences.Add(reference);
            fundsByLegacyId[legacyId.Value] = reference;
        }

        reference.Name = name;
        reference.ShortName = ReadString(row, "short_name");
        reference.IsCurrent = ReadBool(row, "current_fund") ?? true;
        reference.MonthlyUpload = ReadBool(row, "monthly_upload") ?? false;
        reference.Currency = ReadString(row, "currency");
        reference.LegacyMainClassId = ReadInt(row, "mainclass_id");
        reference.LegacySubClassId = ReadInt(row, "subclass_id");
        reference.LegacyAdministratorId = ReadInt(row, "administrator_id");
        ApplyLegacyAudit(reference, row);
    }

    var marketValuesByLegacyId = await db.MarketReferenceValues
        .Where(reference => reference.LegacyMiscInfoId.HasValue)
        .ToDictionaryAsync(reference => reference.LegacyMiscInfoId!.Value);

    await foreach (var row in ReadLegacyRowsAsync(legacyConnection, "tbl_miscinfo"))
    {
        var legacyId = ReadInt(row, "id");
        var name = ReadString(row, "name");
        if (legacyId is null || string.IsNullOrWhiteSpace(name))
        {
            continue;
        }

        marketValueCount++;
        if (dryRun)
        {
            continue;
        }

        if (!marketValuesByLegacyId.TryGetValue(legacyId.Value, out var reference))
        {
            reference = new MarketReferenceValue { LegacyMiscInfoId = legacyId.Value };
            db.MarketReferenceValues.Add(reference);
            marketValuesByLegacyId[legacyId.Value] = reference;
        }

        reference.Name = name;
        reference.PriceDate = ReadDateOnly(row, "price_date");
        reference.Value = ReadDecimal(row, "value");
        ApplyLegacyAudit(reference, row);
    }

    if (!dryRun)
    {
        await db.SaveChangesAsync();
    }

    Console.WriteLine($"Legacy reference import complete. Product types: {productTypeCount}; Administrators: {administratorCount}; KYC main classes: {mainClassCount}; KYC sub classes: {subClassCount}; Funds: {fundCount}; Market values: {marketValueCount}; Dry run: {dryRun}");
}

static string? ReadString(IReadOnlyDictionary<string, string?> row, string key)
{
    return row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value.Trim()
        : null;
}

static bool? ReadBool(IReadOnlyDictionary<string, string?> row, string key)
{
    return ReadString(row, key)?.ToUpperInvariant() switch
    {
        "Y" or "YES" or "TRUE" or "1" => true,
        "N" or "NO" or "FALSE" or "0" => false,
        _ => null
    };
}

static DateOnly? ReadDateOnly(IReadOnlyDictionary<string, string?> row, string key)
{
    var value = ReadString(row, key);
    if (DateOnly.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedDate))
    {
        return parsedDate;
    }

    return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var parsedDateTime)
        ? DateOnly.FromDateTime(parsedDateTime)
        : null;
}

static DateTime? ReadDateTime(IReadOnlyDictionary<string, string?> row, string key)
{
    var value = ReadString(row, key);
    return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var parsed)
        ? parsed
        : null;
}

static decimal? ReadDecimal(IReadOnlyDictionary<string, string?> row, string key)
{
    var value = ReadString(row, key);
    return decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
        ? parsed
        : null;
}

static string? StripTags(string? value)
{
    return string.IsNullOrWhiteSpace(value)
        ? null
        : System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline).Trim();
}

static void ApplyLegacyAudit(dynamic reference, IReadOnlyDictionary<string, string?> row)
{
    reference.OpenedBy = ReadString(row, "opened_by");
    reference.UpdatedBy = ReadString(row, "updated_by");
    reference.LegacyOpenedAt = ReadDateTime(row, "date_opened");
    reference.LegacyUpdatedAt = ReadDateTime(row, "date_updated");
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
