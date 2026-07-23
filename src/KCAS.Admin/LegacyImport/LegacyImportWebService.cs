using System.Diagnostics;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;
using KCAS.Admin.Data;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace KCAS.Admin.LegacyImport;

public sealed class LegacyImportWebService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<LegacyImportWebService> logger)
{
    private static readonly SemaphoreSlim ImportGate = new(1, 1);
    private static readonly ConcurrentDictionary<Guid, LegacyImportJobState> Jobs = new();

    private static readonly Regex UnsafeDatabaseStatement = new(
        @"^\s*(CREATE\s+DATABASE|DROP\s+DATABASE|USE\s+`?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ExpectedTables =
    [
        "tbl_client",
        "tbl_clientnote",
        "tbl_kyc",
        "tbl_investmentaccount",
        "tbl_investmenthistory",
        "tbl_fund",
        "tbl_companyproduct",
        "tbl_lispname",
        "tbl_mainclass",
        "tbl_subclass",
        "tbl_fundname",
        "tbl_miscinfo"
    ];

    public bool CanResetImportedLegacyData =>
        environment.IsDevelopment() || configuration.GetValue("LegacyImport:AllowResetImportedData", false);

    public LegacyImportJobSnapshot? GetJob(Guid jobId)
        => Jobs.TryGetValue(jobId, out var job) ? job.Snapshot() : null;

    public async Task<LegacyImportJobSnapshot> StartApplyAllNewRowsJobAsync(long scanRunId, string tableScope = LegacyImportTableScopes.AllMapped, CancellationToken cancellationToken = default)
    {
        return await StartApplyJobAsync(
            "Apply all eligible new rows",
            async job =>
            {
                job.Update("Backing up KCAS before apply...", 10);
                var result = await ApplyApprovedRowsAsync(scanRunId, null, LegacyImportTableScopes.Normalize(tableScope), message =>
                {
                    job.Update(message, message.Contains("verification", StringComparison.OrdinalIgnoreCase) ? 80 : 30);
                    return Task.CompletedTask;
                }, CancellationToken.None, alreadyLocked: true);
                job.Complete(result.Id, $"Eligible new rows from scan #{scanRunId} were applied.");
            },
            cancellationToken);
    }

    public async Task<LegacyImportJobSnapshot> StartApplySingleNewRowJobAsync(long scanRunId, string sourceTable, long sourceId, string tableScope = LegacyImportTableScopes.AllMapped, CancellationToken cancellationToken = default)
    {
        return await StartApplyJobAsync(
            $"Apply {sourceTable} #{sourceId}",
            async job =>
            {
                job.Update("Backing up KCAS before apply...", 10);
                var approvedRows = new HashSet<(string Table, long Id)> { (sourceTable, sourceId) };
                var result = await ApplyApprovedRowsAsync(scanRunId, approvedRows, LegacyImportTableScopes.Normalize(tableScope), message =>
                {
                    job.Update(message, message.Contains("verification", StringComparison.OrdinalIgnoreCase) ? 80 : 30);
                    return Task.CompletedTask;
                }, CancellationToken.None, alreadyLocked: true);
                job.Complete(result.Id, $"{sourceTable} #{sourceId} was applied from scan #{scanRunId}.");
            },
            cancellationToken);
    }

    private async Task<LegacyImportJobSnapshot> StartApplyJobAsync(
        string title,
        Func<LegacyImportJobState, Task> work,
        CancellationToken cancellationToken)
    {
        if (!await ImportGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Another legacy import is already running. Wait for it to finish, then refresh the page.");
        }

        var job = LegacyImportJobState.Start(title);
        Jobs[job.Id] = job;
        _ = Task.Run(async () =>
        {
            try
            {
                await work(job);
            }
            catch (Exception ex)
            {
                job.Fail(ex.GetBaseException().Message);
                logger.LogError(ex, "Legacy import job {JobId} failed.", job.Id);
            }
            finally
            {
                ImportGate.Release();
            }
        }, CancellationToken.None);

        return job.Snapshot();
    }

    public async Task<LegacyImportJobSnapshot> StartScanUploadedSqlJobAsync(
        IBrowserFile file,
        bool applyAllAfterScan,
        bool resetImportedDataFirst,
        string tableScope,
        CancellationToken cancellationToken = default)
    {
        if (resetImportedDataFirst && !CanResetImportedLegacyData)
        {
            throw new InvalidOperationException("Reset and import is not enabled for this environment.");
        }
        if (!string.Equals(Path.GetExtension(file.Name), ".sql", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Legacy imports require a .sql export.");
        }
        if (!await ImportGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Another legacy import is already running. Wait for it to finish, then refresh the page.");
        }

        var normalizedScope = LegacyImportTableScopes.Normalize(tableScope);
        var scopeLabel = LegacyImportTableScopes.Options.Single(option => option.Value == (string.IsNullOrWhiteSpace(tableScope) ? LegacyImportTableScopes.AllMapped : tableScope)).Label;
        var job = LegacyImportJobState.Start(resetImportedDataFirst ? $"Baseline import: {scopeLabel}" : applyAllAfterScan ? $"Upload, scan and apply: {scopeLabel}" : $"Upload and scan: {scopeLabel}");
        Jobs[job.Id] = job;
        var maxUploadBytes = configuration.GetValue<long?>("LegacyImport:MaxUploadBytes") ?? 1_500_000_000L;
        var tempDirectory = Path.Combine(Path.GetTempPath(), "kcas-legacy-imports");
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.sql");

        try
        {
            job.Update("Copying SQL upload...", 5);
            await CopyAndHashUploadAsync(file, tempPath, maxUploadBytes, cancellationToken);
        }
        catch
        {
            ImportGate.Release();
            throw;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await ExecuteUploadedSqlPathAsync(
                    tempPath,
                    Path.GetFileName(file.Name),
                    applyAllAfterScan || resetImportedDataFirst,
                    resetImportedDataFirst,
                    normalizedScope,
                    job,
                    CancellationToken.None);
                job.Complete(result.RunId, result.Message);
            }
            catch (Exception ex)
            {
                job.Fail(ex.GetBaseException().Message);
                logger.LogError(ex, "Legacy import job {JobId} failed.", job.Id);
            }
            finally
            {
                try { File.Delete(tempPath); } catch (IOException ex) { logger.LogWarning(ex, "Could not delete temporary legacy import upload {TempPath}.", tempPath); }
                ImportGate.Release();
            }
        }, CancellationToken.None);

        return job.Snapshot();
    }

    public async Task<LegacyImportWebResult> ScanUploadedSqlAsync(
        IBrowserFile file,
        bool applyAllAfterScan,
        Func<string, Task>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!await ImportGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Another legacy import is already running. Wait for it to finish, then refresh the page.");
        }

        if (!string.Equals(Path.GetExtension(file.Name), ".sql", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Legacy imports require a .sql export.");
        }

        var maxUploadBytes = configuration.GetValue<long?>("LegacyImport:MaxUploadBytes") ?? 1_500_000_000L;
        var tempDirectory = Path.Combine(Path.GetTempPath(), "kcas-legacy-imports");
        Directory.CreateDirectory(tempDirectory);
        var tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.sql");

        try
        {
            await ReportProgressAsync(progress, "Copying the SQL upload and calculating its SHA-256...");
            var sha256 = await CopyAndHashUploadAsync(file, tempPath, maxUploadBytes, cancellationToken);
            await ReportProgressAsync(progress, "Staging the SQL export into a checksum-named MySQL database...");
            var stagedDatabase = await StageSnapshotAsync(tempPath, sha256, cancellationToken);
            await ReportProgressAsync(progress, "Scanning the staged legacy database against KCAS...");
            var scanRun = await RunImportAsync(
                LegacyImportModes.Scan,
                stagedDatabase,
                sha256,
                Path.GetFileName(file.Name),
                null,
                null,
                LegacyImportTableScopes.Normalize(LegacyImportTableScopes.AllMapped),
                cancellationToken);

            if (!applyAllAfterScan)
            {
                return new LegacyImportWebResult(scanRun.Id, "Scan completed.");
            }

            var applyRun = await ApplyApprovedRowsAsync(scanRun.Id, null, LegacyImportTableScopes.Normalize(LegacyImportTableScopes.AllMapped), progress, cancellationToken, alreadyLocked: true);
            return new LegacyImportWebResult(applyRun.Id, $"Scan #{scanRun.Id} completed and eligible new rows were applied.");
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not delete temporary legacy import upload {TempPath}.", tempPath);
            }
            ImportGate.Release();
        }
    }

    private async Task<LegacyImportWebResult> ExecuteUploadedSqlPathAsync(
        string sqlPath,
        string sourceFileName,
        bool applyAllAfterScan,
        bool resetImportedDataFirst,
        IReadOnlySet<string> tableScopes,
        LegacyImportJobState job,
        CancellationToken cancellationToken)
    {
        if (resetImportedDataFirst)
        {
            job.Update("Backing up KCAS before reset...", 10);
            await BackupTargetDatabaseAsync(cancellationToken);
            job.Update("Deleting previous imported legacy data...", 20);
            await ResetImportedLegacyDataAsync(tableScopes, cancellationToken);
        }

        job.Update("Calculating SQL snapshot SHA-256...", resetImportedDataFirst ? 30 : 10);
        var sha256 = await HashFileAsync(sqlPath, cancellationToken);
        job.Update("Staging SQL export into MySQL...", resetImportedDataFirst ? 40 : 20);
        var stagedDatabase = await StageSnapshotAsync(sqlPath, sha256, cancellationToken);
        job.Update("Scanning staged legacy data...", resetImportedDataFirst ? 55 : 35);
        var scanRun = await RunImportAsync(
            LegacyImportModes.Scan,
            stagedDatabase,
            sha256,
            sourceFileName,
            null,
            null,
            tableScopes,
            cancellationToken);

        if (!applyAllAfterScan)
        {
            return new LegacyImportWebResult(scanRun.Id, "Scan completed.");
        }

        job.Update("Applying eligible new rows...", resetImportedDataFirst ? 70 : 60);
        var verificationRun = await ApplyApprovedRowsAsync(scanRun.Id, null, tableScopes, message =>
        {
            job.Update(message, resetImportedDataFirst ? 75 : 65);
            return Task.CompletedTask;
        }, cancellationToken, alreadyLocked: true, skipBackup: resetImportedDataFirst);

        return new LegacyImportWebResult(verificationRun.Id, resetImportedDataFirst
            ? $"Imported a clean mapped legacy baseline from {sourceFileName}."
            : $"Scan #{scanRun.Id} completed and eligible new rows were applied.");
    }

    public async Task<LegacyImportWebResult> ApplyAllNewRowsAsync(long scanRunId, string tableScope = LegacyImportTableScopes.AllMapped, Func<string, Task>? progress = null, CancellationToken cancellationToken = default)
    {
        var run = await ApplyApprovedRowsAsync(scanRunId, null, LegacyImportTableScopes.Normalize(tableScope), progress, cancellationToken);
        return new LegacyImportWebResult(run.Id, $"Eligible new rows from scan #{scanRunId} were applied.");
    }

    public async Task<LegacyImportWebResult> ApplySingleNewRowAsync(
        long scanRunId,
        string sourceTable,
        long sourceId,
        string tableScope = LegacyImportTableScopes.AllMapped,
        Func<string, Task>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var approvedRows = new HashSet<(string Table, long Id)> { (sourceTable, sourceId) };
        var run = await ApplyApprovedRowsAsync(scanRunId, approvedRows, LegacyImportTableScopes.Normalize(tableScope), progress, cancellationToken);
        return new LegacyImportWebResult(run.Id, $"{sourceTable} #{sourceId} was applied from scan #{scanRunId}.");
    }

    public async Task RetainKcasForChangedRowAsync(long rowStateId, string reviewedBy, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Retaining KCAS values requires a review reason.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.LegacyImportRowStates
            .Include(item => item.Differences)
            .SingleOrDefaultAsync(item => item.Id == rowStateId, cancellationToken);
        if (row is null)
        {
            throw new InvalidOperationException("The selected reconciliation row no longer exists.");
        }
        if (row.Classification != LegacyImportClassifications.Changed)
        {
            throw new InvalidOperationException("Only changed reconciliation rows can be marked as retained.");
        }

        foreach (var difference in row.Differences)
        {
            difference.Decision = LegacyImportDecisionStatuses.RetainKcas;
            difference.ResolvedValue = difference.BaselineValue;
            difference.ReviewedBy = reviewedBy;
            difference.ReviewedAtUtc = DateTime.UtcNow;
            difference.ReviewReason = reason.Trim();
        }

        row.ApplyStatus = LegacyImportApplyStatuses.NotApplicable;
        await RefreshRunReviewStatusAsync(db, row.LegacyImportRunId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ApplyIncomingLegacyForChangedRowAsync(long rowStateId, string reviewedBy, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Applying incoming legacy values requires a review reason.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.LegacyImportRowStates
            .Include(item => item.Differences)
            .SingleOrDefaultAsync(item => item.Id == rowStateId, cancellationToken);
        if (row is null)
        {
            throw new InvalidOperationException("The selected reconciliation row no longer exists.");
        }
        if (!CanApplyIncomingLegacy(row))
        {
            throw new InvalidOperationException("This changed row does not currently support applying incoming legacy values.");
        }

        var values = DeserializePayload(row.IncomingPayloadJson);
        await ApplyIncomingMappedValuesAsync(db, row, values, cancellationToken);

        foreach (var difference in row.Differences)
        {
            difference.Decision = LegacyImportDecisionStatuses.AcceptLegacy;
            difference.ResolvedValue = difference.IncomingValue;
            difference.ReviewedBy = reviewedBy;
            difference.ReviewedAtUtc = DateTime.UtcNow;
            difference.ReviewReason = reason.Trim();
        }

        row.ApplyStatus = LegacyImportApplyStatuses.Applied;
        await AcceptSourceSnapshotAsync(db, row, cancellationToken);
        await RefreshRunReviewStatusAsync(db, row.LegacyImportRunId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordManualResolutionAsync(long rowStateId, string reviewedBy, string resolvedValue, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resolvedValue))
        {
            throw new InvalidOperationException("Manual resolution requires the resolved value or resolution note.");
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Manual resolution requires a reason.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await LoadReviewRowAsync(db, rowStateId, cancellationToken);
        EnsureReviewable(row);
        EnsureDifferenceAuditRows(row);

        foreach (var difference in row.Differences)
        {
            difference.Decision = LegacyImportDecisionStatuses.Corrected;
            difference.ResolvedValue = resolvedValue.Trim();
            difference.ReviewedBy = reviewedBy;
            difference.ReviewedAtUtc = DateTime.UtcNow;
            difference.ReviewReason = reason.Trim();
        }

        row.ApplyStatus = LegacyImportApplyStatuses.NotApplicable;
        await RefreshRunReviewStatusAsync(db, row.LegacyImportRunId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeferReviewAsync(long rowStateId, string reviewedBy, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Deferring a reconciliation item requires a reason.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await LoadReviewRowAsync(db, rowStateId, cancellationToken);
        EnsureReviewable(row);
        EnsureDifferenceAuditRows(row);

        foreach (var difference in row.Differences)
        {
            difference.Decision = LegacyImportDecisionStatuses.Deferred;
            difference.ResolvedValue = null;
            difference.ReviewedBy = reviewedBy;
            difference.ReviewedAtUtc = DateTime.UtcNow;
            difference.ReviewReason = reason.Trim();
        }

        row.ApplyStatus = LegacyImportApplyStatuses.PendingReview;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RejectReviewAsync(long rowStateId, string reviewedBy, string reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("Rejecting a reconciliation item requires a reason.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await LoadReviewRowAsync(db, rowStateId, cancellationToken);
        EnsureReviewable(row);
        EnsureDifferenceAuditRows(row);

        foreach (var difference in row.Differences)
        {
            difference.Decision = LegacyImportDecisionStatuses.Rejected;
            difference.ResolvedValue = difference.BaselineValue;
            difference.ReviewedBy = reviewedBy;
            difference.ReviewedAtUtc = DateTime.UtcNow;
            difference.ReviewReason = reason.Trim();
        }

        row.ApplyStatus = LegacyImportApplyStatuses.NotApplicable;
        await RefreshRunReviewStatusAsync(db, row.LegacyImportRunId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<long, string?>> GetCurrentKcasPayloadsAsync(IEnumerable<long> rowStateIds, CancellationToken cancellationToken = default)
    {
        var ids = rowStateIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<long, string?>();
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var rows = await db.LegacyImportRowStates
            .AsNoTracking()
            .Where(row => ids.Contains(row.Id))
            .ToListAsync(cancellationToken);
        var payloads = new Dictionary<long, string?>();
        foreach (var row in rows)
        {
            payloads[row.Id] = await GetCurrentKcasPayloadAsync(db, row, cancellationToken);
        }

        return payloads;
    }

    public bool IsEligibleForApply(LegacyImportRowState row)
        => row.Classification == LegacyImportClassifications.New &&
           row.ApplyStatus != LegacyImportApplyStatuses.Applied &&
           !LegacyImportApprovalValidator.ReviewOnlySourceTables.Contains(row.SourceTable);

    public bool CanRetainKcas(LegacyImportRowState row)
        => row.Classification == LegacyImportClassifications.Changed &&
           row.ApplyStatus == LegacyImportApplyStatuses.PendingReview;

    public bool CanApplyIncomingLegacy(LegacyImportRowState row)
        => row.Classification == LegacyImportClassifications.Changed &&
           row.ApplyStatus == LegacyImportApplyStatuses.PendingReview &&
           row.SourceTable is "tbl_client" or "tbl_clientnote" or "tbl_investmentaccount" or "tbl_investmenthistory" or "tbl_fund" or "tbl_kyc";

    public bool CanResolveReview(LegacyImportRowState row)
        => row.Classification is LegacyImportClassifications.Changed or LegacyImportClassifications.MissingFromSource or LegacyImportClassifications.Invalid or LegacyImportClassifications.Orphaned &&
           row.ApplyStatus == LegacyImportApplyStatuses.PendingReview;

    private static IReadOnlyDictionary<string, string?> DeserializePayload(string payloadJson)
    {
        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    private static int ReadRequiredInt(IReadOnlyDictionary<string, string?> row, string key)
        => row.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"Incoming legacy payload is missing numeric '{key}'.");

    private static async Task<LegacyImportRowState> LoadReviewRowAsync(ApplicationDbContext db, long rowStateId, CancellationToken cancellationToken)
    {
        var row = await db.LegacyImportRowStates
            .Include(item => item.Differences)
            .SingleOrDefaultAsync(item => item.Id == rowStateId, cancellationToken);
        return row ?? throw new InvalidOperationException("The selected reconciliation row no longer exists.");
    }

    private static void EnsureReviewable(LegacyImportRowState row)
    {
        if (row.Classification is not (LegacyImportClassifications.Changed or LegacyImportClassifications.MissingFromSource or LegacyImportClassifications.Invalid or LegacyImportClassifications.Orphaned))
        {
            throw new InvalidOperationException("Only changed, missing, invalid or orphaned reconciliation rows can be reviewed this way.");
        }
        if (row.ApplyStatus != LegacyImportApplyStatuses.PendingReview)
        {
            throw new InvalidOperationException("The selected reconciliation row is not pending review.");
        }
    }

    private static void EnsureDifferenceAuditRows(LegacyImportRowState row)
    {
        if (row.Differences.Count > 0)
        {
            return;
        }

        row.Differences.Add(new LegacyImportDifference
        {
            FieldName = "__row__",
            BaselineValue = row.BaselinePayloadJson,
            IncomingValue = row.IncomingPayloadJson
        });
    }

    private static async Task<string?> GetCurrentKcasPayloadAsync(ApplicationDbContext db, LegacyImportRowState row, CancellationToken cancellationToken)
    {
        return row.SourceTable switch
        {
            "tbl_client" => await db.Clients
                .AsNoTracking()
                .Where(item => item.LegacyClientId == row.SourceId)
                .Select(item => item.LegacySnapshots
                    .OrderByDescending(snapshot => snapshot.ImportedAtUtc)
                    .Select(snapshot => snapshot.PayloadJson)
                    .FirstOrDefault())
                .SingleOrDefaultAsync(cancellationToken),
            "tbl_clientnote" => await db.ClientNotes
                .AsNoTracking()
                .Where(item => item.LegacyClientNoteId == row.SourceId)
                .Select(item => item.PayloadJson)
                .SingleOrDefaultAsync(cancellationToken),
            "tbl_investmentaccount" => await db.ClientInvestmentAccounts
                .AsNoTracking()
                .Where(item => item.LegacyInvestmentAccountId == row.SourceId)
                .Select(item => item.PayloadJson)
                .SingleOrDefaultAsync(cancellationToken),
            "tbl_investmenthistory" => await db.ClientInvestmentTransactions
                .AsNoTracking()
                .Where(item => item.LegacyInvestmentHistoryId == row.SourceId)
                .Select(item => item.PayloadJson)
                .SingleOrDefaultAsync(cancellationToken),
            "tbl_fund" => await db.ClientFundValuations
                .AsNoTracking()
                .Where(item => item.LegacyFundId == row.SourceId)
                .Select(item => item.PayloadJson)
                .SingleOrDefaultAsync(cancellationToken),
            "tbl_kyc" => await db.ClientKycPolicies
                .AsNoTracking()
                .Where(item => item.LegacyKycId == row.SourceId)
                .Select(item => item.PayloadJson)
                .SingleOrDefaultAsync(cancellationToken),
            _ => null
        };
    }

    private static async Task ApplyIncomingMappedValuesAsync(
        ApplicationDbContext db,
        LegacyImportRowState row,
        IReadOnlyDictionary<string, string?> values,
        CancellationToken cancellationToken)
    {
        switch (row.SourceTable)
        {
            case "tbl_client":
                var client = await db.Clients
                    .Include(item => item.PersonalProfile)
                    .Include(item => item.FinancialProfile)
                    .Include(item => item.ContactPoints)
                    .Include(item => item.Addresses)
                    .Include(item => item.Relationships)
                    .Include(item => item.LegacySnapshots)
                    .SingleAsync(item => item.LegacyClientId == row.SourceId, cancellationToken);
                var mappedClient = LegacyClientImportMapper.Map(values, DateTime.UtcNow);
                LegacyClientImportMapper.ApplyUpdatedGraph(client, mappedClient);
                client.LegacyReconciliationStatus = LegacyReconciliationStatuses.Reconciled;
                break;

            case "tbl_clientnote":
                var note = await db.ClientNotes.SingleAsync(item => item.LegacyClientNoteId == row.SourceId, cancellationToken);
                var mappedNote = LegacyClientNoteImportMapper.Map(values, note.ClientId, DateTime.UtcNow);
                LegacyClientNoteImportMapper.ApplyUpdatedValues(note, mappedNote);
                break;

            case "tbl_investmentaccount":
                var account = await db.ClientInvestmentAccounts.SingleAsync(item => item.LegacyInvestmentAccountId == row.SourceId, cancellationToken);
                var mappedAccount = LegacyInvestmentAccountImportMapper.Map(values, account.ClientId, DateTime.UtcNow);
                LegacyInvestmentAccountImportMapper.ApplyUpdatedValues(account, mappedAccount);
                break;

            case "tbl_investmenthistory":
                var transaction = await db.ClientInvestmentTransactions.SingleAsync(item => item.LegacyInvestmentHistoryId == row.SourceId, cancellationToken);
                var mappedTransaction = LegacyInvestmentTransactionImportMapper.Map(values, transaction.ClientInvestmentAccountId, DateTime.UtcNow);
                LegacyInvestmentTransactionImportMapper.ApplyUpdatedValues(transaction, mappedTransaction);
                break;

            case "tbl_fund":
                var fund = await db.ClientFundValuations.SingleAsync(item => item.LegacyFundId == row.SourceId, cancellationToken);
                var mappedFund = LegacyFundValuationImportMapper.Map(values, fund.ClientId, DateTime.UtcNow);
                LegacyFundValuationImportMapper.ApplyUpdatedValues(fund, mappedFund);
                break;

            case "tbl_kyc":
                var policy = await db.ClientKycPolicies.SingleAsync(item => item.LegacyKycId == row.SourceId, cancellationToken);
                var mainClasses = await db.KycMainClassReferences
                    .Where(item => item.LegacyMainClassId.HasValue)
                    .ToDictionaryAsync(item => item.LegacyMainClassId!.Value, item => item.Name, cancellationToken);
                var subClasses = await db.KycSubClassReferences
                    .Where(item => item.LegacySubClassId.HasValue)
                    .ToDictionaryAsync(item => item.LegacySubClassId!.Value, item => item.Name, cancellationToken);
                var mappedPolicy = LegacyKycImportMapper.Map(values, policy.ClientId, mainClasses, subClasses, DateTime.UtcNow);
                LegacyKycImportMapper.ApplyUpdatedValues(policy, mappedPolicy);
                break;

            default:
                throw new InvalidOperationException($"Changed-row apply is not supported for '{row.SourceTable}'.");
        }
    }

    private static async Task AcceptSourceSnapshotAsync(ApplicationDbContext db, LegacyImportRowState row, CancellationToken cancellationToken)
    {
        var snapshot = await db.LegacySourceSnapshots
            .SingleOrDefaultAsync(item => item.SourceTable == row.SourceTable && item.SourceId == row.SourceId, cancellationToken);
        if (snapshot is null)
        {
            snapshot = new LegacySourceSnapshot
            {
                SourceTable = row.SourceTable,
                SourceId = row.SourceId
            };
            db.LegacySourceSnapshots.Add(snapshot);
        }

        snapshot.PayloadJson = row.IncomingPayloadJson;
        snapshot.Fingerprint = row.IncomingFingerprint;
        snapshot.AcceptedAtUtc = DateTime.UtcNow;
        snapshot.AcceptedFromRunId = row.LegacyImportRunId;
        snapshot.LastSeenAtUtc = DateTime.UtcNow;
    }

    private static async Task RefreshRunReviewStatusAsync(ApplicationDbContext db, long runId, CancellationToken cancellationToken)
    {
        var hasPendingReview = await db.LegacyImportRowStates
            .AnyAsync(row => row.LegacyImportRunId == runId && row.ApplyStatus == LegacyImportApplyStatuses.PendingReview, cancellationToken);
        var run = await db.LegacyImportRuns.SingleAsync(run => run.Id == runId, cancellationToken);
        if (!hasPendingReview && run.Status == LegacyImportRunStatuses.AwaitingReview)
        {
            run.Status = LegacyImportRunStatuses.Completed;
        }
    }

    private async Task<LegacyImportRun> ApplyApprovedRowsAsync(
        long scanRunId,
        IReadOnlySet<(string Table, long Id)>? approvedRows,
        IReadOnlySet<string> tableScopes,
        Func<string, Task>? progress,
        CancellationToken cancellationToken,
        bool alreadyLocked = false,
        bool skipBackup = false)
    {
        if (!alreadyLocked && !await ImportGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("Another legacy import is already running. Wait for it to finish, then refresh the page.");
        }

        try
        {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var scan = await db.LegacyImportRuns
            .AsNoTracking()
            .Include(run => run.Rows)
            .SingleOrDefaultAsync(run => run.Id == scanRunId, cancellationToken);
        if (scan is null)
        {
            throw new InvalidOperationException($"Scan run #{scanRunId} does not exist.");
        }

        var stagedDatabase = scan.SourceLabel;
        if (!IsStagedDatabaseName(stagedDatabase))
        {
            throw new InvalidOperationException($"Scan run #{scanRunId} does not point to a KCAS staging database.");
        }

        if (!skipBackup)
        {
            await ReportProgressAsync(progress, "Backing up KCAS before applying imported records...");
        }
        _ = await RunImportAsync(
            LegacyImportModes.ApplyNew,
            stagedDatabase,
            scan.SourceSnapshotSha256,
            scan.SourceSnapshotFileName,
            scanRunId,
            approvedRows,
            tableScopes,
            cancellationToken,
            skipBackup);

        await ReportProgressAsync(progress, "Running the verification scan after apply...");
        return await RunImportAsync(
            LegacyImportModes.Scan,
            stagedDatabase,
            scan.SourceSnapshotSha256,
            scan.SourceSnapshotFileName,
            null,
            null,
            tableScopes,
            cancellationToken);
        }
        finally
        {
            if (!alreadyLocked)
            {
                ImportGate.Release();
            }
        }
    }

    private async Task<LegacyImportRun> RunImportAsync(
        string mode,
        string stagedDatabase,
        string snapshotSha256,
        string? sourceFileName,
        long? approvedScanRunId,
        IReadOnlySet<(string Table, long Id)>? approvedRows,
        IReadOnlySet<string> tableScopes,
        CancellationToken cancellationToken,
        bool skipBackup = false)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pendingMigrations = await db.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pendingMigrations.Any())
        {
            throw new InvalidOperationException("KCAS has pending database migrations. Deploy reviewed migrations before importing.");
        }

        Dictionary<(string Table, long Id), string>? approvedNewRows = null;
        if (mode == LegacyImportModes.ApplyNew)
        {
            if (!skipBackup)
            {
                await BackupTargetDatabaseAsync(cancellationToken);
            }
            var approvedScan = await db.LegacyImportRuns
                .AsNoTracking()
                .Include(run => run.Rows)
                .SingleAsync(run => run.Id == approvedScanRunId, cancellationToken);
            approvedNewRows = LegacyImportApprovalValidator.GetApprovedNewRows(
                approvedScan,
                stagedDatabase,
                snapshotSha256,
                approvedRows);
        }

        await using var legacyConnection = new MySqlConnection(BuildSourceConnectionString(stagedDatabase));
        await legacyConnection.OpenAsync(cancellationToken);

        var recorder = await LegacyImportRunRecorder.StartAsync(
            db,
            mode,
            stagedDatabase,
            snapshotSha256,
            sourceFileName,
            approvedScanRunId,
            cancellationToken);
        var importer = new IncrementalLegacyImporter(db, legacyConnection, recorder, approvedNewRows, tableScopes);

        try
        {
            var failed = await importer.ExecuteAsync();
            await recorder.CompleteAsync(failed, cancellationToken: cancellationToken);
            return recorder.Run;
        }
        catch (Exception ex)
        {
            var failureMessage = ex.GetBaseException().Message;
            await recorder.FailAsync(failureMessage, cancellationToken);
            throw new InvalidOperationException($"Legacy import run {recorder.Run.Id} failed: {failureMessage}", ex);
        }
    }

    private async Task<string> StageSnapshotAsync(string sqlPath, string sha256, CancellationToken cancellationToken)
    {
        var database = $"kcas_legacy_stage_{sha256[..12]}";
        await using var serverConnection = new MySqlConnection(BuildServerConnectionString());
        await serverConnection.OpenAsync(cancellationToken);

        var existsCommand = serverConnection.CreateCommand();
        existsCommand.CommandText = "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name = @database;";
        existsCommand.Parameters.AddWithValue("@database", database);
        var exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync(cancellationToken));
        if (exists == 0)
        {
            var createCommand = serverConnection.CreateCommand();
            createCommand.CommandText = $"CREATE DATABASE `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
            await createCommand.ExecuteNonQueryAsync(cancellationToken);
            await RestoreSqlAsync(sqlPath, database, cancellationToken);
        }

        await ValidateStagedDatabaseAsync(database, cancellationToken);
        return database;
    }

    private async Task RestoreSqlAsync(string sqlPath, string database, CancellationToken cancellationToken)
    {
        var mysqlPath = GetMySqlExecutablePath("mysql.exe");
        var target = GetTargetSettings();
        var processInfo = new ProcessStartInfo
        {
            FileName = mysqlPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        var pluginDirectory = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(mysqlPath))!, "lib", "plugin");
        processInfo.ArgumentList.Add($"--plugin-dir={pluginDirectory}");
        processInfo.ArgumentList.Add("--protocol=tcp");
        processInfo.ArgumentList.Add($"--host={target.Host}");
        processInfo.ArgumentList.Add($"--port={target.Port}");
        processInfo.ArgumentList.Add($"--user={target.User}");
        processInfo.ArgumentList.Add($"--database={database}");
        if (!string.IsNullOrWhiteSpace(target.Password))
        {
            processInfo.Environment["MYSQL_PWD"] = target.Password;
        }

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Could not start mysql.exe.");
        await using (var input = File.OpenRead(sqlPath))
        {
            await input.CopyToAsync(process.StandardInput.BaseStream, cancellationToken);
        }
        process.StandardInput.Close();
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Restoring the SQL export failed with exit code {process.ExitCode}: {stderr.Trim()}");
        }
    }

    private async Task ValidateStagedDatabaseAsync(string database, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(BuildSourceConnectionString(database));
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SHOW TABLES;";
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        var missingTables = ExpectedTables.Where(table => !tables.Contains(table)).ToArray();
        if (missingTables.Length > 0)
        {
            throw new InvalidOperationException($"Staged export is missing required table(s): {string.Join(", ", missingTables)}.");
        }
    }

    private async Task BackupTargetDatabaseAsync(CancellationToken cancellationToken)
    {
        var dumpPath = GetMySqlExecutablePath("mysqldump.exe");
        var target = GetTargetSettings();
        var backupDirectory = GetBackupDirectory();
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-before-web-legacy-apply.sql");

        var processInfo = new ProcessStartInfo
        {
            FileName = dumpPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        var pluginDirectory = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(dumpPath))!, "lib", "plugin");
        processInfo.ArgumentList.Add($"--plugin-dir={pluginDirectory}");
        processInfo.ArgumentList.Add("--protocol=tcp");
        processInfo.ArgumentList.Add($"--host={target.Host}");
        processInfo.ArgumentList.Add($"--port={target.Port}");
        processInfo.ArgumentList.Add($"--user={target.User}");
        processInfo.ArgumentList.Add("--single-transaction");
        processInfo.ArgumentList.Add("--routines");
        processInfo.ArgumentList.Add("--triggers");
        processInfo.ArgumentList.Add("--events");
        processInfo.ArgumentList.Add("--no-tablespaces");
        processInfo.ArgumentList.Add("--default-character-set=utf8mb4");
        processInfo.ArgumentList.Add($"--result-file={backupPath}");
        processInfo.ArgumentList.Add(target.Database);
        if (!string.IsNullOrWhiteSpace(target.Password))
        {
            processInfo.Environment["MYSQL_PWD"] = target.Password;
        }

        using var process = Process.Start(processInfo) ?? throw new InvalidOperationException("Could not start mysqldump.exe.");
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 || !File.Exists(backupPath) || new FileInfo(backupPath).Length == 0)
        {
            throw new InvalidOperationException($"Pre-import database backup failed: {stderr.Trim()}");
        }
    }

    private async Task<string> CopyAndHashUploadAsync(
        IBrowserFile file,
        string tempPath,
        long maxUploadBytes,
        CancellationToken cancellationToken)
    {
        await using (var upload = file.OpenReadStream(maxUploadBytes, cancellationToken))
        await using (var output = File.Create(tempPath))
        {
            await upload.CopyToAsync(output, cancellationToken);
        }

        using (var reader = new StreamReader(tempPath))
        {
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                if (UnsafeDatabaseStatement.IsMatch(line))
                {
                    throw new InvalidOperationException("SQL export contains CREATE DATABASE, DROP DATABASE, or USE statements. Export only kanaanclients table data.");
                }
            }
        }

        await using var hashInput = File.OpenRead(tempPath);
        var hash = await SHA256.HashDataAsync(hashInput, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(input, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task ResetImportedLegacyDataAsync(IReadOnlySet<string> tableScopes, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            await DeleteImportHistoryAsync(db, cancellationToken);

            if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.FundValuations))
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `ClientFundValuations` WHERE `LegacyFundId` IS NOT NULL;", cancellationToken);
            }
            if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.InvestmentTransactions))
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `ClientInvestmentTransactions` WHERE `LegacyInvestmentHistoryId` IS NOT NULL;", cancellationToken);
            }
            if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.InvestmentAccounts))
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `ClientInvestmentAccounts` WHERE `LegacyInvestmentAccountId` IS NOT NULL;", cancellationToken);
            }
            if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.KycPolicies))
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `ClientKycRecommendations` WHERE `LegacyRecommendationId` IS NOT NULL;", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `ClientKycPolicies` WHERE `LegacyKycId` IS NOT NULL;", cancellationToken);
            }
            if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.Notes))
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `ClientNotes` WHERE `LegacyClientNoteId` IS NOT NULL;", cancellationToken);
            }
            if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.Clients))
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `ClientLegacySnapshots` WHERE `SourceTable` = 'tbl_client';", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `Clients` WHERE `LegacyClientId` IS NOT NULL;", cancellationToken);
            }
            if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.ReferenceData))
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `MarketReferenceValues` WHERE `LegacyMiscInfoId` IS NOT NULL;", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `InvestmentFundReferences` WHERE `LegacyFundNameId` IS NOT NULL;", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `InvestmentProductTypeReferences` WHERE `LegacyCompanyProductId` IS NOT NULL;", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `InvestmentAdministratorReferences` WHERE `LegacyLispId` IS NOT NULL;", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `KycSubClassReferences` WHERE `LegacySubClassId` IS NOT NULL;", cancellationToken);
                await db.Database.ExecuteSqlRawAsync("DELETE FROM `KycMainClassReferences` WHERE `LegacyMainClassId` IS NOT NULL;", cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        });
    }

    internal static async Task DeleteImportHistoryAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM `LegacyImportDifferences`;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM `LegacyImportRowStates`;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM `LegacySourceSnapshots`;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM `LegacyImportRuns`;", cancellationToken);
    }

    private static IEnumerable<string> GetSourceTablesForScopes(IReadOnlySet<string> tableScopes)
    {
        if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.ReferenceData))
        {
            yield return "tbl_companyproduct";
            yield return "tbl_lispname";
            yield return "tbl_mainclass";
            yield return "tbl_subclass";
            yield return "tbl_fundname";
            yield return "tbl_miscinfo";
        }
        if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.Clients)) { yield return "tbl_client"; }
        if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.Notes)) { yield return "tbl_clientnote"; }
        if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.KycPolicies)) { yield return "tbl_kyc"; }
        if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.InvestmentAccounts)) { yield return "tbl_investmentaccount"; }
        if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.InvestmentTransactions)) { yield return "tbl_investmenthistory"; }
        if (tableScopes.Contains(LegacyImportTableScopes.AllMapped) || tableScopes.Contains(LegacyImportTableScopes.FundValuations)) { yield return "tbl_fund"; }
    }

    private string BuildServerConnectionString()
    {
        var target = GetTargetSettings();
        var builder = new MySqlConnectionStringBuilder
        {
            Server = target.Host,
            Port = (uint)target.Port,
            UserID = target.User,
            Password = target.Password,
            TreatTinyAsBoolean = true,
            SslMode = IsLocalHost(target.Host) ? MySqlSslMode.Disabled : MySqlSslMode.Preferred,
            AllowPublicKeyRetrieval = IsLocalHost(target.Host)
        };
        return builder.ConnectionString;
    }

    private string BuildSourceConnectionString(string stagedDatabase)
    {
        var target = GetTargetSettings();
        var builder = new MySqlConnectionStringBuilder
        {
            Server = target.Host,
            Port = (uint)target.Port,
            Database = stagedDatabase,
            UserID = configuration["LegacyImport:SourceUser"] ?? target.User,
            Password = configuration["LegacyImport:SourcePassword"] ?? target.Password,
            TreatTinyAsBoolean = false,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true,
            SslMode = IsLocalHost(target.Host) ? MySqlSslMode.Disabled : MySqlSslMode.Preferred,
            AllowPublicKeyRetrieval = IsLocalHost(target.Host)
        };
        return builder.ConnectionString;
    }

    private LegacyImportTargetSettings GetTargetSettings()
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        var builder = new MySqlConnectionStringBuilder(connectionString);
        return new LegacyImportTargetSettings(
            builder.Server,
            (int)builder.Port,
            builder.Database,
            builder.UserID,
            builder.Password);
    }

    private string GetMySqlExecutablePath(string executableName)
    {
        var configuredBasePath = configuration["LegacyImport:MySqlBasePath"];
        var candidates = new[]
        {
            string.IsNullOrWhiteSpace(configuredBasePath) ? null : Path.Combine(configuredBasePath, "bin", executableName),
            @"D:\wamp64\bin\mysql\mysql9.1.0\bin\" + executableName,
            @"C:\wamp64\bin\mysql\mysql9.1.0\bin\" + executableName
        }.Where(path => !string.IsNullOrWhiteSpace(path));

        var match = candidates.FirstOrDefault(File.Exists);
        return match ?? throw new InvalidOperationException($"Could not find {executableName}. Configure LegacyImport:MySqlBasePath.");
    }

    private string GetBackupDirectory()
    {
        var configuredDirectory = configuration["LegacyImport:BackupDirectory"];
        if (!string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return configuredDirectory;
        }

        var productionSharedRoot = @"D:\Deploy\KCAS\shared";
        return Directory.Exists(productionSharedRoot)
            ? Path.Combine(productionSharedRoot, "database-backups")
            : Path.Combine(AppContext.BaseDirectory, "App_Data", "LegacyImportBackups");
    }

    private static async Task ReportProgressAsync(Func<string, Task>? progress, string message)
    {
        if (progress is not null)
        {
            await progress(message);
        }
    }

    private static bool IsStagedDatabaseName(string value)
        => Regex.IsMatch(value, "^kcas_legacy_stage_[0-9a-f]{12}$", RegexOptions.IgnoreCase);

    private static bool IsLocalHost(string host)
        => host is "localhost" or "127.0.0.1" or "::1";

    private sealed record LegacyImportTargetSettings(string Host, int Port, string Database, string User, string Password);
}

public sealed record LegacyImportWebResult(long RunId, string Message);

public sealed class LegacyImportJobState
{
    private readonly object sync = new();

    private LegacyImportJobState(string title)
    {
        Id = Guid.NewGuid();
        Title = title;
        Status = "Running";
        Message = "Queued...";
        Percent = 1;
        StartedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = StartedAtUtc;
    }

    public Guid Id { get; }
    public string Title { get; }
    public string Status { get; private set; }
    public string Message { get; private set; }
    public int Percent { get; private set; }
    public long? ResultRunId { get; private set; }
    public DateTime StartedAtUtc { get; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static LegacyImportJobState Start(string title) => new(title);

    public void Update(string message, int percent)
    {
        lock (sync)
        {
            Message = message;
            Percent = Math.Clamp(percent, 1, 99);
            UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    public void Complete(long runId, string message)
    {
        lock (sync)
        {
            Status = "Completed";
            Message = message;
            Percent = 100;
            ResultRunId = runId;
            UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    public void Fail(string message)
    {
        lock (sync)
        {
            Status = "Failed";
            Message = message;
            Percent = 100;
            UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    public LegacyImportJobSnapshot Snapshot()
    {
        lock (sync)
        {
            return new LegacyImportJobSnapshot(Id, Title, Status, Message, Percent, ResultRunId, StartedAtUtc, UpdatedAtUtc);
        }
    }
}

public sealed record LegacyImportJobSnapshot(
    Guid Id,
    string Title,
    string Status,
    string Message,
    int Percent,
    long? ResultRunId,
    DateTime StartedAtUtc,
    DateTime UpdatedAtUtc)
{
    public bool IsRunning => Status == "Running";
    public bool IsCompleted => Status == "Completed";
    public bool IsFailed => Status == "Failed";
}
