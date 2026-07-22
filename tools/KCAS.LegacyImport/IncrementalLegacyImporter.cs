using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KCAS.Admin.Data;
using KCAS.Admin.LegacyImport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

internal sealed class IncrementalLegacyImporter(
    ApplicationDbContext db,
    MySqlConnection legacyConnection,
    LegacyImportRunRecorder recorder,
    IReadOnlyDictionary<(string Table, long Id), string>? approvedNewRows = null)
{
    private readonly IReadOnlyDictionary<(string Table, long Id), string> approvedNewRows = approvedNewRows
        ?? new Dictionary<(string Table, long Id), string>();
    private readonly HashSet<(string Table, long Id)> observedApprovedRows = [];
    private readonly Dictionary<int, int?> clientTargets = [];
    private readonly HashSet<int> sourceClientIds = [];
    private readonly Dictionary<int, int?> accountTargets = [];
    private readonly HashSet<int> sourceAccountIds = [];
    private long syntheticIssueId = -1;

    public async Task<int> ExecuteAsync()
    {
        var failures = 0;
        failures += await ImportReferenceDataAsync();
        failures += await ImportClientsAsync();
        failures += await ImportNotesAsync();
        failures += await ImportKycAsync();
        failures += await ImportInvestmentAccountsAsync();
        failures += await ImportInvestmentTransactionsAsync();
        failures += await ImportFundValuationsAsync();
        var missingApprovedRows = approvedNewRows.Keys.Except(observedApprovedRows).ToArray();
        if (missingApprovedRows.Length > 0)
        {
            throw new InvalidOperationException($"The approved scan contains {missingApprovedRows.Length} new row(s) that are absent from the staged snapshot.");
        }
        await db.SaveChangesAsync();
        return failures;
    }

    private async Task<int> ImportReferenceDataAsync()
    {
        var failures = 0;
        var productTypes = await db.InvestmentProductTypeReferences
            .Where(item => item.LegacyCompanyProductId.HasValue)
            .ToDictionaryAsync(item => item.LegacyCompanyProductId!.Value);
        await foreach (var row in ReadRowsAsync("tbl_companyproduct"))
        {
            var id = ReadInt(row, "id");
            var name = ReadString(row, "name");
            if (id is null || name is null)
            {
                StageInvalidReference("tbl_companyproduct", id, row);
                failures++;
                continue;
            }
            var mapped = new InvestmentProductTypeReference { LegacyCompanyProductId = id, Name = name };
            ApplyLegacyAudit(mapped, row);
            StageReference("tbl_companyproduct", id.Value, row, productTypes.GetValueOrDefault(id.Value), mapped,
                item => db.InvestmentProductTypeReferences.Add(item), item => item.Id, nameof(InvestmentProductTypeReference));
        }

        var administrators = await db.InvestmentAdministratorReferences
            .Where(item => item.LegacyLispId.HasValue)
            .ToDictionaryAsync(item => item.LegacyLispId!.Value);
        await foreach (var row in ReadRowsAsync("tbl_lispname"))
        {
            var id = ReadInt(row, "id");
            var name = ReadString(row, "name");
            if (id is null || name is null)
            {
                StageInvalidReference("tbl_lispname", id, row);
                failures++;
                continue;
            }
            var mapped = new InvestmentAdministratorReference
            {
                LegacyLispId = id,
                Name = name,
                ShortName = ReadString(row, "short_name"),
                IsCurrent = ReadBool(row, "current_lisp") ?? true,
                MonthlyUpload = ReadBool(row, "monthly_upload") ?? false
            };
            ApplyLegacyAudit(mapped, row);
            StageReference("tbl_lispname", id.Value, row, administrators.GetValueOrDefault(id.Value), mapped,
                item => db.InvestmentAdministratorReferences.Add(item), item => item.Id, nameof(InvestmentAdministratorReference));
        }

        var mainClasses = await db.KycMainClassReferences
            .Where(item => item.LegacyMainClassId.HasValue)
            .ToDictionaryAsync(item => item.LegacyMainClassId!.Value);
        await foreach (var row in ReadRowsAsync("tbl_mainclass"))
        {
            var id = ReadInt(row, "id");
            var name = ReadString(row, "name");
            if (id is null || name is null)
            {
                StageInvalidReference("tbl_mainclass", id, row);
                failures++;
                continue;
            }
            var mapped = new KycMainClassReference
            {
                LegacyMainClassId = id,
                Name = name,
                AfrikaansDescription = StripTags(ReadString(row, "afullname")),
                EnglishDescription = StripTags(ReadString(row, "efullname"))
            };
            ApplyLegacyAudit(mapped, row);
            StageReference("tbl_mainclass", id.Value, row, mainClasses.GetValueOrDefault(id.Value), mapped,
                item => db.KycMainClassReferences.Add(item), item => item.Id, nameof(KycMainClassReference));
            // Keep source-side parents in memory during scan as well as apply. This lets a new
            // subclass be reviewed in the same scan as its new parent; apply saves parents before
            // constructing their approved children.
            if (!mainClasses.ContainsKey(id.Value))
            {
                mainClasses[id.Value] = mapped;
            }
        }
        await db.SaveChangesAsync();

        var subClasses = await db.KycSubClassReferences
            .Where(item => item.LegacySubClassId.HasValue)
            .ToDictionaryAsync(item => item.LegacySubClassId!.Value);
        await foreach (var row in ReadRowsAsync("tbl_subclass"))
        {
            var id = ReadInt(row, "id");
            var mainClassId = ReadInt(row, "mainclass_id");
            var name = ReadString(row, "name");
            if (id is null || mainClassId is null || name is null)
            {
                StageInvalidReference("tbl_subclass", id, row);
                failures++;
                continue;
            }
            if (!mainClasses.TryGetValue(mainClassId.Value, out var parent))
            {
                recorder.StageIssue("tbl_subclass", id.Value, Serialize(row), LegacyImportClassifications.Orphaned, $"Legacy main class {mainClassId} is not available in KCAS.");
                continue;
            }
            var mapped = new KycSubClassReference
            {
                LegacySubClassId = id,
                LegacyMainClassId = mainClassId,
                KycMainClassReferenceId = parent.Id,
                Name = name
            };
            ApplyLegacyAudit(mapped, row);
            StageReference("tbl_subclass", id.Value, row, subClasses.GetValueOrDefault(id.Value), mapped,
                item => db.KycSubClassReferences.Add(item), item => item.Id, nameof(KycSubClassReference));
        }

        var funds = await db.InvestmentFundReferences
            .Where(item => item.LegacyFundNameId.HasValue)
            .ToDictionaryAsync(item => item.LegacyFundNameId!.Value);
        await foreach (var row in ReadRowsAsync("tbl_fundname"))
        {
            var id = ReadInt(row, "id");
            var name = ReadString(row, "name");
            if (id is null || name is null)
            {
                StageInvalidReference("tbl_fundname", id, row);
                failures++;
                continue;
            }
            var mapped = new InvestmentFundReference
            {
                LegacyFundNameId = id,
                Name = name,
                ShortName = ReadString(row, "short_name"),
                IsCurrent = ReadBool(row, "current_fund") ?? true,
                MonthlyUpload = ReadBool(row, "monthly_upload") ?? false,
                Currency = ReadString(row, "currency"),
                LegacyMainClassId = ReadInt(row, "mainclass_id"),
                LegacySubClassId = ReadInt(row, "subclass_id"),
                LegacyAdministratorId = ReadInt(row, "administrator_id")
            };
            ApplyLegacyAudit(mapped, row);
            StageReference("tbl_fundname", id.Value, row, funds.GetValueOrDefault(id.Value), mapped,
                item => db.InvestmentFundReferences.Add(item), item => item.Id, nameof(InvestmentFundReference));
        }

        var marketValues = await db.MarketReferenceValues
            .Where(item => item.LegacyMiscInfoId.HasValue)
            .ToDictionaryAsync(item => item.LegacyMiscInfoId!.Value);
        await foreach (var row in ReadRowsAsync("tbl_miscinfo"))
        {
            var id = ReadInt(row, "id");
            var name = ReadString(row, "name");
            if (id is null || name is null)
            {
                StageInvalidReference("tbl_miscinfo", id, row);
                failures++;
                continue;
            }
            var mapped = new MarketReferenceValue
            {
                LegacyMiscInfoId = id,
                Name = name,
                PriceDate = ReadDateOnly(row, "price_date"),
                Value = ReadDecimal(row, "value")
            };
            ApplyLegacyAudit(mapped, row);
            StageReference("tbl_miscinfo", id.Value, row, marketValues.GetValueOrDefault(id.Value), mapped,
                item => db.MarketReferenceValues.Add(item), item => item.Id, nameof(MarketReferenceValue));
        }

        await db.SaveChangesAsync();
        return failures;
    }

    private void StageReference<T>(
        string table,
        int sourceId,
        IReadOnlyDictionary<string, string?> sourceRow,
        T? current,
        T mapped,
        Action<T> add,
        Func<T, int> targetId,
        string entityType)
        where T : class
    {
        var payload = Serialize(sourceRow);
        var canApply = IsApprovedNew(table, sourceId, payload);
        if (current is not null)
        {
            // Reference entities predate reconciliation snapshots. The first safe scan accepts the
            // observed legacy row as their baseline without overwriting the existing KCAS record.
            recorder.Stage(table, sourceId, payload, payload, entityType, targetId(current), ReadDateTime(sourceRow, "date_updated"));
            return;
        }

        recorder.Stage(table, sourceId, payload, null, entityType, null, ReadDateTime(sourceRow, "date_updated"), canApply);
        if (!canApply)
        {
            return;
        }
        add(mapped);
        // Child rows do not need their generated KCAS key to import later tables. They are saved
        // as a batch at the end of the table, avoiding one database round-trip per source row.
    }

    private void StageInvalidReference(string table, int? sourceId, IReadOnlyDictionary<string, string?> row)
        => recorder.StageIssue(table, sourceId ?? syntheticIssueId--, Serialize(row), LegacyImportClassifications.Invalid, "Reference row requires a numeric id and name.");

    private async Task<int> ImportClientsAsync()
    {
        var failures = 0;
        var existing = await db.Clients
            .Include(client => client.LegacySnapshots)
            .Where(client => client.LegacyClientId.HasValue)
            .ToDictionaryAsync(client => client.LegacyClientId!.Value);

        foreach (var client in existing.Values)
        {
            clientTargets[client.LegacyClientId!.Value] = client.Id;
        }

        await foreach (var sourceRow in ReadRowsAsync("tbl_client"))
        {
            var sourceId = ReadInt(sourceRow, "id");
            var payload = Serialize(sourceRow);
            if (sourceId is null)
            {
                recorder.StageIssue("tbl_client", syntheticIssueId--, payload, LegacyImportClassifications.Invalid, "Row has no numeric id.");
                failures++;
                continue;
            }

            sourceClientIds.Add(sourceId.Value);
            var canApply = IsApprovedNew("tbl_client", sourceId.Value, payload);
            Client mapped;
            try
            {
                mapped = LegacyClientImportMapper.Map(sourceRow, recorder.Run.StartedAtUtc);
            }
            catch (Exception ex)
            {
                recorder.StageIssue("tbl_client", sourceId.Value, payload, LegacyImportClassifications.Invalid, ex.Message);
                failures++;
                continue;
            }

            if (!existing.TryGetValue(sourceId.Value, out var current))
            {
                var state = recorder.Stage("tbl_client", sourceId.Value, payload, null, nameof(Client), null, ReadDateTime(sourceRow, "date_updated"), canApply);
                if (canApply)
                {
                    mapped.LegacyReconciliationStatus = LegacyReconciliationStatuses.NewPendingReview;
                    db.Clients.Add(mapped);
                    await db.SaveChangesAsync();
                    state.TargetEntityId = mapped.Id;
                    clientTargets[sourceId.Value] = mapped.Id;
                    existing[sourceId.Value] = mapped;
                }
                else
                {
                    clientTargets[sourceId.Value] = null;
                }
                continue;
            }

            var baseline = current.LegacySnapshots
                .OrderByDescending(snapshot => snapshot.ImportedAtUtc)
                .Select(snapshot => snapshot.PayloadJson)
                .FirstOrDefault();
            var rowState = recorder.Stage("tbl_client", sourceId.Value, payload, baseline, nameof(Client), current.Id, ReadDateTime(sourceRow, "date_updated"));
            current.LegacyReconciliationStatus = rowState.Classification == LegacyImportClassifications.Unchanged
                ? LegacyReconciliationStatuses.UnchangedReconciled
                : LegacyReconciliationStatuses.ChangedPendingReview;
        }

        foreach (var (sourceId, current) in existing.Where(pair => !sourceClientIds.Contains(pair.Key)))
        {
            var baseline = current.LegacySnapshots.OrderByDescending(snapshot => snapshot.ImportedAtUtc).FirstOrDefault()?.PayloadJson ?? "{}";
            recorder.StageMissing("tbl_client", sourceId, baseline, nameof(Client), current.Id);
            current.LegacyReconciliationStatus = LegacyReconciliationStatuses.ChangedPendingReview;
        }

        await db.SaveChangesAsync();
        return failures;
    }

    private async Task<int> ImportNotesAsync()
    {
        var failures = 0;
        var seen = new HashSet<int>();
        var existing = await db.ClientNotes
            .Where(note => note.LegacyClientNoteId.HasValue)
            .ToDictionaryAsync(note => note.LegacyClientNoteId!.Value);

        await foreach (var sourceRow in ReadRowsAsync("tbl_clientnote"))
        {
            var sourceId = ReadInt(sourceRow, "id");
            var parentId = ReadInt(sourceRow, "client_id");
            var payload = Serialize(sourceRow);
            if (sourceId is null || parentId is null)
            {
                recorder.StageIssue("tbl_clientnote", sourceId ?? syntheticIssueId--, payload, LegacyImportClassifications.Invalid, "Row requires numeric id and client_id.");
                failures++;
                continue;
            }
            seen.Add(sourceId.Value);
            if (!sourceClientIds.Contains(parentId.Value))
            {
                recorder.StageIssue("tbl_clientnote", sourceId.Value, payload, LegacyImportClassifications.Orphaned, $"Legacy client {parentId} is absent from tbl_client.");
                continue;
            }

            var targetClientId = clientTargets.GetValueOrDefault(parentId.Value);
            var mapped = LegacyClientNoteImportMapper.Map(sourceRow, targetClientId ?? 0, recorder.Run.StartedAtUtc);
            StageSimple(
                "tbl_clientnote", sourceId.Value, payload, existing.GetValueOrDefault(sourceId.Value),
                item => item.PayloadJson, nameof(ClientNote), mapped,
                () => targetClientId.HasValue,
                item => db.ClientNotes.Add(item),
                item => item.Id,
                ReadDateTime(sourceRow, "date_updated"));
        }

        StageMissing(existing, seen, "tbl_clientnote", item => item.PayloadJson, item => item.Id, nameof(ClientNote));
        await db.SaveChangesAsync();
        return failures;
    }

    private async Task<int> ImportKycAsync()
    {
        var failures = 0;
        var seen = new HashSet<int>();
        var existing = await db.ClientKycPolicies.Where(item => item.LegacyKycId.HasValue).ToDictionaryAsync(item => item.LegacyKycId!.Value);
        var mainClasses = await ReadLookupAsync("tbl_mainclass");
        var subClasses = await ReadLookupAsync("tbl_subclass");

        await foreach (var sourceRow in ReadRowsAsync("tbl_kyc"))
        {
            var sourceId = ReadInt(sourceRow, "id");
            var parentId = ReadInt(sourceRow, "client_id");
            var payload = Serialize(sourceRow);
            if (sourceId is null || parentId is null)
            {
                recorder.StageIssue("tbl_kyc", sourceId ?? syntheticIssueId--, payload, LegacyImportClassifications.Invalid, "Row requires numeric id and client_id.");
                failures++;
                continue;
            }
            seen.Add(sourceId.Value);
            if (!sourceClientIds.Contains(parentId.Value))
            {
                recorder.StageIssue("tbl_kyc", sourceId.Value, payload, LegacyImportClassifications.Orphaned, $"Legacy client {parentId} is absent from tbl_client.");
                continue;
            }

            var targetClientId = clientTargets.GetValueOrDefault(parentId.Value);
            var mapped = LegacyKycImportMapper.Map(sourceRow, targetClientId ?? 0, mainClasses, subClasses, recorder.Run.StartedAtUtc);
            StageSimple("tbl_kyc", sourceId.Value, payload, existing.GetValueOrDefault(sourceId.Value), item => item.PayloadJson,
                nameof(ClientKycPolicy), mapped, () => targetClientId.HasValue, item => db.ClientKycPolicies.Add(item), item => item.Id,
                ReadDateTime(sourceRow, "date_updated"));
        }

        StageMissing(existing, seen, "tbl_kyc", item => item.PayloadJson, item => item.Id, nameof(ClientKycPolicy));
        await db.SaveChangesAsync();
        return failures;
    }

    private async Task<int> ImportInvestmentAccountsAsync()
    {
        var failures = 0;
        var seen = new HashSet<int>();
        var existing = await db.ClientInvestmentAccounts
            .Where(item => item.LegacyInvestmentAccountId.HasValue)
            .ToDictionaryAsync(item => item.LegacyInvestmentAccountId!.Value);
        foreach (var item in existing.Values)
        {
            accountTargets[item.LegacyInvestmentAccountId!.Value] = item.Id;
        }

        await foreach (var sourceRow in ReadRowsAsync("tbl_investmentaccount"))
        {
            var sourceId = ReadInt(sourceRow, "id");
            var parentId = ReadInt(sourceRow, "client_id");
            var payload = Serialize(sourceRow);
            if (sourceId is null || parentId is null)
            {
                recorder.StageIssue("tbl_investmentaccount", sourceId ?? syntheticIssueId--, payload, LegacyImportClassifications.Invalid, "Row requires numeric id and client_id.");
                failures++;
                continue;
            }
            seen.Add(sourceId.Value);
            sourceAccountIds.Add(sourceId.Value);
            var canApply = IsApprovedNew("tbl_investmentaccount", sourceId.Value, payload);
            if (!sourceClientIds.Contains(parentId.Value))
            {
                recorder.StageIssue("tbl_investmentaccount", sourceId.Value, payload, LegacyImportClassifications.Orphaned, $"Legacy client {parentId} is absent from tbl_client.");
                continue;
            }

            var targetClientId = clientTargets.GetValueOrDefault(parentId.Value);
            var mapped = LegacyInvestmentAccountImportMapper.Map(sourceRow, targetClientId ?? 0, recorder.Run.StartedAtUtc);
            if (!existing.TryGetValue(sourceId.Value, out var current))
            {
                var applyWithParent = canApply && targetClientId.HasValue;
                var state = recorder.Stage("tbl_investmentaccount", sourceId.Value, payload, null, nameof(ClientInvestmentAccount), null, ReadDateTime(sourceRow, "date_updated"), applyWithParent);
                if (applyWithParent)
                {
                    db.ClientInvestmentAccounts.Add(mapped);
                    db.SaveChanges();
                    state.TargetEntityId = mapped.Id;
                    accountTargets[sourceId.Value] = mapped.Id;
                    existing[sourceId.Value] = mapped;
                }
                else
                {
                    accountTargets[sourceId.Value] = null;
                }
                continue;
            }

            recorder.Stage("tbl_investmentaccount", sourceId.Value, payload, current.PayloadJson, nameof(ClientInvestmentAccount), current.Id, ReadDateTime(sourceRow, "date_updated"));
        }

        StageMissing(existing, seen, "tbl_investmentaccount", item => item.PayloadJson, item => item.Id, nameof(ClientInvestmentAccount));
        await db.SaveChangesAsync();
        return failures;
    }

    private async Task<int> ImportInvestmentTransactionsAsync()
    {
        var failures = 0;
        var seen = new HashSet<int>();
        var existing = await db.ClientInvestmentTransactions
            .Where(item => item.LegacyInvestmentHistoryId.HasValue)
            .ToDictionaryAsync(item => item.LegacyInvestmentHistoryId!.Value);

        await foreach (var sourceRow in ReadRowsAsync("tbl_investmenthistory"))
        {
            var sourceId = ReadInt(sourceRow, "id");
            var parentId = ReadInt(sourceRow, "ia_id");
            var payload = Serialize(sourceRow);
            if (sourceId is null || parentId is null)
            {
                recorder.StageIssue("tbl_investmenthistory", sourceId ?? syntheticIssueId--, payload, LegacyImportClassifications.Invalid, "Row requires numeric id and ia_id.");
                failures++;
                continue;
            }
            seen.Add(sourceId.Value);
            if (!sourceAccountIds.Contains(parentId.Value))
            {
                recorder.StageIssue("tbl_investmenthistory", sourceId.Value, payload, LegacyImportClassifications.Orphaned, $"Legacy investment account {parentId} is absent from tbl_investmentaccount.");
                continue;
            }

            var targetAccountId = accountTargets.GetValueOrDefault(parentId.Value);
            var mapped = LegacyInvestmentTransactionImportMapper.Map(sourceRow, targetAccountId ?? 0, recorder.Run.StartedAtUtc);
            StageSimple("tbl_investmenthistory", sourceId.Value, payload, existing.GetValueOrDefault(sourceId.Value), item => item.PayloadJson,
                nameof(ClientInvestmentTransaction), mapped, () => targetAccountId.HasValue,
                item => db.ClientInvestmentTransactions.Add(item), item => item.Id, ReadDateTime(sourceRow, "date_updated"));
        }

        StageMissing(existing, seen, "tbl_investmenthistory", item => item.PayloadJson, item => item.Id, nameof(ClientInvestmentTransaction));
        await db.SaveChangesAsync();
        return failures;
    }

    private async Task<int> ImportFundValuationsAsync()
    {
        var failures = 0;
        var seen = new HashSet<int>();
        var existing = await db.ClientFundValuations.ToDictionaryAsync(item => item.LegacyFundId);

        await foreach (var sourceRow in ReadRowsAsync("tbl_fund"))
        {
            var sourceId = ReadInt(sourceRow, "id");
            var parentId = ReadInt(sourceRow, "client_id");
            var payload = Serialize(sourceRow);
            if (sourceId is null || parentId is null)
            {
                recorder.StageIssue("tbl_fund", sourceId ?? syntheticIssueId--, payload, LegacyImportClassifications.Invalid, "Row requires numeric id and client_id.");
                failures++;
                continue;
            }
            seen.Add(sourceId.Value);
            if (!sourceClientIds.Contains(parentId.Value))
            {
                recorder.StageIssue("tbl_fund", sourceId.Value, payload, LegacyImportClassifications.Orphaned, $"Legacy client {parentId} is absent from tbl_client.");
                continue;
            }

            var targetClientId = clientTargets.GetValueOrDefault(parentId.Value);
            var mapped = LegacyFundValuationImportMapper.Map(sourceRow, targetClientId ?? 0, recorder.Run.StartedAtUtc);
            StageSimple("tbl_fund", sourceId.Value, payload, existing.GetValueOrDefault(sourceId.Value), item => item.PayloadJson,
                nameof(ClientFundValuation), mapped, () => targetClientId.HasValue, item => db.ClientFundValuations.Add(item), item => item.Id,
                ReadDateTime(sourceRow, "date_updated"));
        }

        StageMissing(existing, seen, "tbl_fund", item => item.PayloadJson, item => item.Id, nameof(ClientFundValuation));
        await db.SaveChangesAsync();
        return failures;
    }

    private void StageSimple<T>(
        string table,
        int sourceId,
        string payload,
        T? current,
        Func<T, string> baseline,
        string entityType,
        T mapped,
        Func<bool> parentAvailable,
        Action<T> add,
        Func<T, int> targetId,
        DateTime? sourceUpdatedAt)
        where T : class
    {
        var approvedForApply = IsApprovedNew(table, sourceId, payload);
        if (current is not null)
        {
            recorder.Stage(table, sourceId, payload, baseline(current), entityType, targetId(current), sourceUpdatedAt);
            return;
        }

        var canApply = approvedForApply && parentAvailable();
        recorder.Stage(table, sourceId, payload, null, entityType, null, sourceUpdatedAt, canApply);
        if (!canApply)
        {
            return;
        }

        add(mapped);
    }

    private bool IsApprovedNew(string table, long sourceId, string payload)
    {
        var key = (table, sourceId);
        if (!approvedNewRows.TryGetValue(key, out var approvedFingerprint))
        {
            return false;
        }

        var canonical = LegacyImportReconciler.CanonicalizePayload(payload);
        var currentFingerprint = LegacyImportReconciler.Fingerprint(canonical);
        if (!string.Equals(currentFingerprint, approvedFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Staged source row {table}/{sourceId} changed after approved scan.");
        }

        observedApprovedRows.Add(key);
        return true;
    }

    private void StageMissing<T>(
        IReadOnlyDictionary<int, T> existing,
        ISet<int> seen,
        string table,
        Func<T, string> baseline,
        Func<T, int> targetId,
        string entityType)
    {
        foreach (var (sourceId, item) in existing.Where(pair => !seen.Contains(pair.Key)))
        {
            recorder.StageMissing(table, sourceId, baseline(item), entityType, targetId(item));
        }
    }

    private async IAsyncEnumerable<IReadOnlyDictionary<string, string?>> ReadRowsAsync(string tableName)
    {
        await using var command = legacyConnection.CreateCommand();
        command.CommandText = $"SELECT * FROM `{tableName}` ORDER BY id";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++)
            {
                row[reader.GetName(index)] = await reader.IsDBNullAsync(index)
                    ? null
                    : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture);
            }
            yield return row;
        }
    }

    private async Task<Dictionary<int, string>> ReadLookupAsync(string tableName)
    {
        var result = new Dictionary<int, string>();
        await foreach (var row in ReadRowsAsync(tableName))
        {
            var id = ReadInt(row, "id");
            if (id.HasValue && row.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name))
            {
                result[id.Value] = name.Trim();
            }
        }
        return result;
    }

    private static string Serialize(IReadOnlyDictionary<string, string?> row)
        => JsonSerializer.Serialize(new SortedDictionary<string, string?>(
            row.ToDictionary(item => item.Key, item => item.Value),
            StringComparer.OrdinalIgnoreCase));

    private static int? ReadInt(IReadOnlyDictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string? ReadString(IReadOnlyDictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static bool? ReadBool(IReadOnlyDictionary<string, string?> row, string key)
        => ReadString(row, key)?.ToUpperInvariant() switch
        {
            "Y" or "YES" or "TRUE" or "1" => true,
            "N" or "NO" or "FALSE" or "0" => false,
            _ => null
        };

    private static DateOnly? ReadDateOnly(IReadOnlyDictionary<string, string?> row, string key)
    {
        var value = ReadString(row, key);
        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dateTime)
            ? DateOnly.FromDateTime(dateTime)
            : null;
    }

    private static decimal? ReadDecimal(IReadOnlyDictionary<string, string?> row, string key)
        => decimal.TryParse(ReadString(row, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? StripTags(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : Regex.Replace(value, "<.*?>", string.Empty, RegexOptions.Singleline).Trim();

    private static void ApplyLegacyAudit(dynamic reference, IReadOnlyDictionary<string, string?> row)
    {
        reference.OpenedBy = ReadString(row, "opened_by");
        reference.UpdatedBy = ReadString(row, "updated_by");
        reference.LegacyOpenedAt = ReadDateTime(row, "date_opened");
        reference.LegacyUpdatedAt = ReadDateTime(row, "date_updated");
    }

    private static DateTime? ReadDateTime(IReadOnlyDictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : null;
}
