using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed record FeeStatementUpload(string FileName, byte[] Content);
public sealed record FeeClientSource(int ProfileId, FeeStatementReceipt Receipt);
public sealed record FeeClientAccount(string Number, ClientInvestmentAccount? Account, PerformanceFeeProfile? Profile,
    FeeStatementLedger Ledger, string? Exclusion)
{
    public bool RequiresStatementRebuild => Profile?.Claims.SingleOrDefault(x => x.Status == FeeClaimStages.Draft) is { } draft &&
        (FeeJson.Read<FeePreparation>(draft.InputJson).RequiresRebuild || FeeJson.Read<FeePreparation>(draft.InputJson).StatementHistory?.SourceFingerprint != Ledger.History.SourceFingerprint);
    public string? PreparationHold => Exclusion ?? (Profile?.Claims.Any(x => x.Status is FeeClaimStages.Approved or FeeClaimStages.Submitted or FeeClaimStages.Accepted) == true
        ? "Existing approved claim awaits administrator processing; retained without creating another claim."
        : Ledger.Rows.Count > 0 && Profile?.Claims.Any(x => FeeClaimStages.Frozen(x.Status) && x.To >= Ledger.Rows.Max(r => r.Date)) == true
            ? "Already reviewed through this statement cutoff; retained without repeating the fee period." : null);
}
public sealed record FeeClientWorkspace(Client Client, string Version, List<FeeClientSource> Sources,
    List<FeeClientAccount> Accounts, List<FeeAccountTransfer> Transfers)
{
    public FeeCapitalRouting CapitalRouting { get; init; } = new([], []);
}
public sealed record FeeAccountTransfer(string Key, string FromAccount, string ToAccount, string FromWrap,
    string ToWrap, DateOnly Date, decimal Amount, decimal? BeforeValue, string Reference, string[] SourceKeys, string[] TargetKeys);

public sealed partial class PerformanceFeeService
{
    public async Task<List<int>> PrepareAllClientStatementsAsync(int id, string version, string[] confirmedTransfers, string transferReason, ClaimsPrincipal principal)
    {
        var workspace = await ClientWorkspaceAsync(id, principal);
        ComplianceWorkflowAccess.Expect(workspace.Version, version);
        var ids = workspace.Accounts.Where(x => x.Account != null && x.PreparationHold == null).Select(x => x.Account!.Id).ToArray();
        if (ids.Length == 0) throw new ValidationException("No new supported SA-wrap review period is available. Existing claims and other holdings remain in the client report.");
        return await PrepareClientStatementsAsync(id, version, ids, confirmedTransfers, transferReason, principal);
    }

    public async Task<FeeClientWorkspace> ClientWorkspaceAsync(int id, ClaimsPrincipal principal, List<FeeStatementUpload>? uploads = null)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, uploads is not null);
        var anchor = await Row(db, id, actor);
        return await ClientWorkspace(db, anchor, uploads);
    }

    public async Task ImportClientStatementsAsync(int id, string version, List<FeeStatementUpload> uploads, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        var anchor = await Row(db, id, actor);
        var workspace = await ClientWorkspace(db, anchor, null);
        ComplianceWorkflowAccess.Expect(workspace.Version, version);
        var profiles = await ClientProfiles(db, anchor.Account.ClientId);
        foreach (var profile in profiles) CheckPending(profile);
        var documents = ReadUploads(uploads);
        var known = (await db.ClientInvestmentAccounts.Where(x => x.ClientId == anchor.Account.ClientId).Select(x => x.AccountNumber).ToListAsync())
            .Where(x => x != null).Select(x => NormalizeAccount(x!)).ToHashSet();
        var hashes = workspace.Sources.Select(x => x.Receipt.Sha256).ToHashSet();
        var changed = false;
        foreach (var (upload, document) in uploads.Zip(documents))
        {
            if (!document.Rows.Any(x => known.Contains(NormalizeAccount(x.Account))))
                throw new ValidationException($"{document.FileName} has no exact account belonging to this client. Nothing was imported.");
            if (!hashes.Add(document.Sha256)) continue;
            db.PerformanceFeeStatements.Add(new() { PerformanceFeeProfileId = id, FileName = document.FileName,
                Sha256 = document.Sha256, Content = upload.Content, DocumentJson = FeeJson.Write(document), ImportedBy = ComplianceWorkflowAccess.Label(actor) });
            Event(anchor, null, "ClientStatementImported", "Manual", actor, Today,
                "Original client statement preserved once for shared account preparation; investments and compliance records unchanged.", document.FileName + "; SHA256 " + document.Sha256);
            changed = true;
        }
        if (!changed) return;
        foreach (var profile in profiles)
        {
            InvalidateDraft(profile, "Client statement history changed. Rebuild the selected account calculations together.");
            profile.Version = ComplianceWorkflowAccess.NewVersion(); profile.CodexRequestVersion = null; Touch(profile, actor);
        }
        await db.SaveChangesAsync();
    }

    public async Task<List<int>> PrepareClientStatementsAsync(int id, string version, int[] accountIds,
        string[] confirmedTransfers, string transferReason, ClaimsPrincipal principal)
    {
        if (accountIds.Length is 0 or > 200 || accountIds.Distinct().Count() != accountIds.Length)
            throw new ValidationException("Select between 1 and 200 different client accounts.");
        if (confirmedTransfers.Length > 0) transferReason = ComplianceWorkflowAccess.Required(transferReason, "Mandate-continuity evidence", 2000);
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        var anchor = await Row(db, id, actor);
        var workspace = await ClientWorkspace(db, anchor, null);
        ComplianceWorkflowAccess.Expect(workspace.Version, version);
        var selected = workspace.Accounts.Where(x => x.Account != null && accountIds.Contains(x.Account.Id) && x.Exclusion == null).ToList();
        if (selected.Count != accountIds.Length) throw new ValidationException("An account is ambiguous, unavailable, or has no supported active SA wrap. Review the account matches.");
        if (confirmedTransfers.Distinct().Count() != confirmedTransfers.Length || confirmedTransfers.Any(x => workspace.Transfers.All(t => t.Key != x) && workspace.CapitalRouting.Routes.All(t => t.Key != x)))
            throw new ValidationException("The transfer selection is stale or not an exact source-row match.");
        var clientProfiles = await ClientProfiles(db, anchor.Account.ClientId);
        foreach (var item in selected.Where(x => x.Profile != null)) CheckPending(item.Profile!);
        var rates = await CpiObservations(db);
        var policy = await SharedPolicy(db);
        var fingerprint = StatementBatchFingerprint(workspace, clientProfiles, accountIds, confirmedTransfers, transferReason, rates, policy);
        var existing = selected.Select(x => x.Profile?.Claims.SingleOrDefault(c => c.Status == FeeClaimStages.Draft)).ToList();
        if (existing.All(x => x != null && FeeJson.Read<FeeCalculationResult>(x.ResultJson).StatementPreparationFingerprint == fingerprint) &&
            selected.All(x => x.Profile!.Claims.Single(c => c.Status == FeeClaimStages.Draft).InputJson == x.Profile.PreparationJson))
            return existing.Select(x => x!.Id).ToList();
        var inputs = new Dictionary<string, FeePreparation>();
        var profiles = new Dictionary<string, PerformanceFeeProfile>();
        var termSources = clientProfiles.Select(x =>
        { var source = FeeJson.Read<FeePreparation>(x.PreparationJson); ApplySharedPolicy(source, policy); source.MandateSourceAccountId = x.ClientInvestmentAccountId; return source; }).ToList();
        foreach (var item in selected)
        {
            var profile = item.Profile;
            if (profile is null)
            {
                profile = new() { Account = item.Account!, ClientInvestmentAccountId = item.Account!.Id,
                    InceptionDate = item.Account.InvestmentDate, NextReviewDate = item.Account.InvestmentDate?.AddYears(1),
                    PreparationJson = FeeJson.Write(new FeePreparation { Inception = item.Account.InvestmentDate, From = item.Account.InvestmentDate,
                        Administrator = item.Account.Administrator ?? "", WrapFund = item.Account.FundName ?? "" }) };
                db.PerformanceFeeProfiles.Add(profile);
                clientProfiles.Add(profile);
            }
            CheckPending(profile);
            var cp = profile.CheckpointJson is null ? null : System.Text.Json.JsonSerializer.Deserialize<FeeCheckpoint>(profile.CheckpointJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if (cp is { Wraps.Count: 0 }) throw new ValidationException($"{item.Number}: allocate the existing pooled checkpoint to evidenced wraps first.");
            var preparation = item.Ledger.Prepare(FeeJson.Read<FeePreparation>(profile.PreparationJson), cp);
            ApplyCoveredMandate(preparation, item.Account!.Id, termSources);
            ApplySharedPolicy(preparation, policy);
            ApplyBenchmarks(preparation, rates);
            if (preparation.Wraps.All(x => x.Input.FeePercent == 0 && x.Input.ZeroRateConfirmed))
            {
                var lastZeroReview = profile.Claims.Where(x => x.Status == FeeClaimStages.NoFee && FeeJson.Read<FeeCalculationResult>(x.ResultJson).IsZeroRateReview).MaxBy(x => x.To);
                if (lastZeroReview is not null)
                {
                    preparation.From = lastZeroReview.To;
                    foreach (var wrap in preparation.Wraps) wrap.Input.From = preparation.From;
                }
            }
            preparation.AccountDependencies.Clear(); preparation.AccountTransferDecisions.Clear();
            preparation.CapitalTraces = workspace.CapitalRouting.Traces.Where(x => x.Account == item.Number).ToList();
            inputs.Add(item.Number, preparation); profiles.Add(item.Number, profile);
        }
        var automatic = workspace.Transfers.Where(t => t.FromWrap != "CASH" && inputs.ContainsKey(t.FromAccount) && inputs.ContainsKey(t.ToAccount) &&
            termSources.Any(s => !string.IsNullOrWhiteSpace(s.MandateCoverageReference) && s.MandateAccountIds.Contains(profiles[t.FromAccount].ClientInvestmentAccountId) && s.MandateAccountIds.Contains(profiles[t.ToAccount].ClientInvestmentAccountId))).ToList();
        var allConfirmed = confirmedTransfers.Concat(automatic.Select(x => x.Key)).Distinct().ToArray();
        var continuity = transferReason + (automatic.Count > 0 ? " Recorded same-client mandate coverage and exact paired administrator unit-transfer rows." : "");
        ApplyAccountTransfers(workspace, inputs, allConfirmed, continuity, actor);
        ApplyCapitalRoutes(workspace, inputs, confirmedTransfers, transferReason, actor);
        // Tie only actual carried transfers, not unrelated accounts merely uploaded in the same workbook.
        foreach (var (number, input) in inputs)
            input.AccountDependencies = inputs.Where(x => x.Key != number && x.Value.AccountTransferDecisions.Any(d => input.AccountTransferDecisions.Any(i => i.Key == d.Key)))
                .Select(x => new FeeAccountDependency(profiles[x.Key].ClientInvestmentAccountId, PreparationHash(x.Value))).ToList();
        var claims = new List<PerformanceFeeClaim>();
        var batch = Guid.NewGuid().ToString("N");
        foreach (var (number, input) in inputs)
        {
            var profile = profiles[number];
            // Only the trusted ledger projection may add unresolved scope-entry review points.
            claims.Add(SavePreparation(profile, input,
                $"Client statement batch {batch}; account {number}; {workspace.Sources.Count} preserved source file(s). Separate wrap protection retained. Existing terms reused; unsupported facts remain explicit issues.", actor, "KCAS", reviewPointChange: true));
        }
        // Fingerprint the saved inputs, not changing profile versions or preparation timestamps.
        fingerprint = StatementBatchFingerprint(workspace, clientProfiles, accountIds, confirmedTransfers, transferReason, rates, policy);
        foreach (var claim in claims)
        {
            var result = FeeJson.Read<FeeCalculationResult>(claim.ResultJson);
            result.StatementPreparationFingerprint = fingerprint;
            claim.ResultJson = FeeJson.Write(result);
        }
        // One SaveChanges transaction prevents a half-created batch if any version changed or any validation fails.
        await db.SaveChangesAsync();
        return claims.Select(x => x.Id).ToList();
    }

    private static string StatementBatchFingerprint(FeeClientWorkspace workspace, List<PerformanceFeeProfile> profiles,
        int[] accountIds, string[] confirmedTransfers, string transferReason, List<FeeCpiObservation> rates, FeeSharedPolicy? policy) => Hash(FeeJson.Write(new
    {
        // Bump when statement preparation/calculation rules change so earlier cached drafts rebuild.
        Algorithm = "ClientStatementPreparationV8FundSpecific", SharedPolicy = policy,
        Accounts = accountIds.Order(), Transfers = confirmedTransfers.Order(),
        TransferReason = confirmedTransfers.Length == 0 ? "" : transferReason,
        Sources = workspace.Sources.Select(x => new { x.Receipt.FileName, x.Receipt.Sha256 }).OrderBy(x => x.Sha256),
        AccountFacts = workspace.Accounts.Where(x => x.Account != null).OrderBy(x => x.Account!.Id).Select(x => new
        { x.Account!.Id, x.Account.AccountNumber, x.Account.InvestmentDate, x.Account.SurrenderDate, x.Account.Administrator, x.Account.FundName, x.Ledger.History.SourceFingerprint }),
        Profiles = profiles.OrderBy(x => x.ClientInvestmentAccountId).Select(x => new
        {
            x.ClientInvestmentAccountId, x.PreparationJson, x.StatementReviewJson, x.CheckpointJson, x.InceptionDate, x.NextReviewDate,
            Completed = x.Claims.Where(c => c.Status != FeeClaimStages.Draft).OrderBy(c => c.Id).Select(c => new { c.Id, c.Status, c.To }),
            // A fixed historical cutoff need not be recalculated each day. Future dates becoming valid do matter.
            DateValidity = PreparationDates(FeeJson.Read<FeePreparation>(x.PreparationJson)).Select(d => d <= Today)
        }),
        Cpi = rates
    }));

    private static IEnumerable<DateOnly?> PreparationDates(FeePreparation input) =>
        new[] { input.Inception, input.To }.Concat(input.Wraps.SelectMany(x => PreparationDates(x.Input)));

    internal static void ApplyAccountTransfers(FeeClientWorkspace workspace, Dictionary<string, FeePreparation> inputs,
        string[] confirmed, string reason, ApplicationUser actor)
    {
        foreach (var t in workspace.Transfers.OrderBy(x => x.Date).ThenBy(x => x.Key))
        {
            var hasSource = inputs.TryGetValue(t.FromAccount, out var sourceRoot);
            var hasTarget = inputs.TryGetValue(t.ToAccount, out var targetRoot);
            if (!hasSource && !hasTarget) continue;
            var source = sourceRoot?.Wraps.SingleOrDefault(x => x.Code == t.FromWrap)?.Input;
            var target = targetRoot?.Wraps.SingleOrDefault(x => x.Code == t.ToWrap)?.Input;
            if (source is null || target is null || !confirmed.Contains(t.Key)) continue;
            if (!t.FromWrap.Equals(t.ToWrap, StringComparison.OrdinalIgnoreCase))
            { target.EvidenceIssues.Add("Different-wrap allocations must use the actual receiving capital, not a carried same-wrap checkpoint."); continue; }
            var sourceLedger = workspace.Accounts.Single(x => x.Number == t.FromAccount).Ledger;
            var targetLedger = workspace.Accounts.Single(x => x.Number == t.ToAccount).Ledger;
            if (t.Date <= source.From || t.Date < target.From || t.Date > source.To || t.Date > target.To ||
                t.BeforeValue is null or <= 0 || t.Amount > t.BeforeValue ||
                source.Method != target.Method || source.FeePercent != target.FeePercent || source.HurdleSpreadPercent != target.HurdleSpreadPercent || source.VatPercent != target.VatPercent ||
                sourceRoot!.Transfers.Any() || source.Movements.Any(x => x.Date == t.Date) || target.Movements.Any(x => x.Date == t.Date))
            { target.EvidenceIssues.Add($"{t.Date:yyyy-MM-dd}: matched account transfer requires compatible terms, full valuation and unambiguous ordering; no HWM reset applied."); continue; }
            var sourceKeys = t.SourceKeys.ToHashSet(); var targetKeys = t.TargetKeys.ToHashSet();
            var sourceRows = sourceLedger.Rows.Where(x => sourceKeys.Contains(x.Key)).ToList();
            var targetRows = targetLedger.Rows.Where(x => targetKeys.Contains(x.Key)).ToList();
            var sourceIssues = source.EvidenceIssues.Where(x => !sourceRows.Any(r => x == TransferIssue(r))).ToList();
            // Validate source protection without the unresolved paired exit; do not discard any unrelated issue.
            var check = FeeJson.Read<FeePreparation>(FeeJson.Write(source)); check.EvidenceIssues = sourceIssues;
            check.CashFlowsComplete = sourceIssues.Count == 0 && check.Movements.All(x => x.Kind != "Withdrawal" || x.ValueBeforeWithdrawal != null);
            var validation = PerformanceFeeCalculator.Calculate(check, Today);
            if (!validation.CanShowCalculation || validation.IsZeroRateReview || check.OpeningBenchmark is null || check.OpeningHighWaterMark is null)
            { target.EvidenceIssues.Add($"{t.Date:yyyy-MM-dd}: complete {t.FromAccount}/{t.FromWrap}'s evidenced protection inputs before carrying its benchmark/HWM."); continue; }
            var protection = PerformanceFeeCalculator.ProtectionAt(check, t.Date);
            var ratio = t.Amount / t.BeforeValue.Value;
            source.EvidenceIssues = sourceIssues;
            target.EvidenceIssues.RemoveAll(x => targetRows.Any(r => x == TransferIssue(r)));
            var reference = $"Matched {t.FromAccount}/{t.FromWrap} to {t.ToAccount}/{t.ToWrap}; {t.Reference}; continuity: {reason}";
            source.Movements.Add(new() { Date = t.Date, Kind = "Withdrawal", Amount = t.Amount, ValueBeforeWithdrawal = t.BeforeValue, Reference = reference });
            if (t.Date == target.From)
            {
                target.OpeningTransfers.Add(new(t.FromAccount, t.FromWrap, t.Date, t.Amount, protection.Benchmark * ratio, protection.Hwm * ratio, reference));
                target.OpeningValue = (target.OpeningValue ?? 0) + t.Amount;
                target.OpeningBenchmark = (target.OpeningBenchmark ?? 0) + protection.Benchmark * ratio;
                target.OpeningHighWaterMark = (target.OpeningHighWaterMark ?? 0) + protection.Hwm * ratio;
                target.OpeningReference += "; " + reference;
                target.PriorFeeReviewReference = "Opening protection carried from an evidenced AIMS/Glacier source calculation. " + reference;
                if (sourceRoot!.Inception is { } original && original < targetRoot!.Inception)
                {
                    targetRoot.Inception = original;
                    foreach (var child in targetRoot.Wraps) child.Input.Inception = original;
                }
            }
            else target.Movements.Add(new() { Date = t.Date, Kind = "TransferIn", Amount = t.Amount,
                TransferredBenchmark = protection.Benchmark * ratio, TransferredHighWaterMark = protection.Hwm * ratio, Reference = reference });
            source.CashFlowsComplete = source.EvidenceIssues.Count == 0 && source.Movements.All(x => x.Kind != "Withdrawal" || x.ValueBeforeWithdrawal != null);
            target.CashFlowsComplete = target.EvidenceIssues.Count == 0 && target.Movements.All(x => x.Kind != "Withdrawal" || x.ValueBeforeWithdrawal != null);
            var targetRequirements = PerformanceFeeCalculator.Calculate(target, Today).Issues;
            target.ReviewIssues.AddRange(validation.Issues.Where(x => !x.Contains("first investment anniversary", StringComparison.Ordinal) && !targetRequirements.Contains(x)).Select(x => $"Carried protection from {t.FromAccount}/{t.FromWrap}: {x}"));
            target.ReviewIssues.AddRange(sourceRoot!.ReviewPoints.Where(x => !x.Resolved).Select(x => $"Carried protection from {t.FromAccount}/{t.FromWrap}: fee disclosure review: {x.Detail}"));
            var decision = new FeeTransferDecision(t.Key, reason, ComplianceWorkflowAccess.Label(actor), DateTime.UtcNow);
            sourceRoot.AccountTransferDecisions.Add(decision); targetRoot!.AccountTransferDecisions.Add(decision);
        }
    }

    public async Task ApproveClientClaimsAsync(int id, string version, int[] claimIds, string reason, ClaimsPrincipal principal)
    {
        if (claimIds.Length is 0 or > 200 || claimIds.Distinct().Count() != claimIds.Length) throw new ValidationException("Select valid client draft claims.");
        reason = ComplianceWorkflowAccess.Required(reason, "KI approval reason", 2000);
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        if (!await ComplianceWorkflowAccess.PermissionAsync(db, actor.Id, KcasPermissions.InvestmentsManage) || !await IsKi(db, actor))
            throw new UnauthorizedAccessException("A current authorised Key Individual must approve these calculations.");
        var anchor = await Row(db, id, actor); var workspace = await ClientWorkspace(db, anchor, null);
        ComplianceWorkflowAccess.Expect(workspace.Version, version);
        var claims = workspace.Accounts.Where(x => x.Profile != null).SelectMany(x => x.Profile!.Claims.Where(c => claimIds.Contains(c.Id)).Select(c => (Profile: x.Profile!, Claim: c))).ToList();
        if (claims.Count != claimIds.Length) throw new ValidationException("The claims must belong to this client's matched statement accounts.");
        foreach (var (profile, claim) in claims) await StageApproval(db, profile, claim, reason, actor);
        await db.SaveChangesAsync();
    }

    private static string TransferIssue(FeeStatementRow row) => $"{row.Date:yyyy-MM-dd} {row.Kind}: review {row.Detail} ({row.Amount:N2}) at {row.Source}.";

    private static async Task<FeeClientWorkspace> ClientWorkspace(ApplicationDbContext db, PerformanceFeeProfile anchor, List<FeeStatementUpload>? uploads)
    {
        var profiles = await ClientProfiles(db, anchor.Account.ClientId);
        var ids = profiles.Select(x => x.Id).ToArray();
        var files = await db.PerformanceFeeStatements.Where(x => ids.Contains(x.PerformanceFeeProfileId)).OrderBy(x => x.Id).ToListAsync();
        var documents = files.DistinctBy(x => x.Sha256).Select(ParsePreservedStatement).ToList();
        if (uploads is not null)
            foreach (var document in ReadUploads(uploads)) if (documents.All(x => x.Sha256 != document.Sha256)) documents.Add(document);
        var accounts = await db.ClientInvestmentAccounts.Include(x => x.Client).Where(x => x.ClientId == anchor.Account.ClientId).ToListAsync();
        var rows = new List<FeeClientAccount>();
        foreach (var number in documents.SelectMany(x => x.Rows).Select(x => NormalizeAccount(x.Account)).Distinct().Order())
        {
            var matches = accounts.Where(x => NormalizeAccount(x.AccountNumber ?? "") == number).ToList();
            var account = matches.Count == 1 ? matches[0] : null;
            var profile = account is null ? null : profiles.SingleOrDefault(x => x.ClientInvestmentAccountId == account.Id);
            var ledger = FeeStatementLedger.Merge(documents, number, profile is null ? null : FeeJson.Read<Dictionary<string, FeeStatementReview>>(profile.StatementReviewJson));
            string? exclusion = matches.Count == 0 ? "Not matched to this client; excluded" : matches.Count > 1 ? "More than one KCAS account matches; resolve before selection" :
                !ledger.Wraps.Any(x => x.Code is "KE1" or "KA6" or "KA9" or "KP1") ? "Outside the supported SA wrap calculation; retained in the client review" : null;
            rows.Add(new(number, account, profile, ledger, exclusion));
        }
        var version = Hash(FeeJson.Write(new { Parser = PerformanceFeeStatementReader.CurrentVersion, SharedPolicy = await SharedPolicy(db), Cpi = await CpiObservations(db), Profiles = profiles.OrderBy(x => x.Id).Select(x => new { x.Id, x.Version }), Files = files.Select(x => new { x.Id, x.Sha256 }),
            Accounts = accounts.OrderBy(x => x.Id).Select(x => new { x.Id, x.AccountNumber, x.InvestmentDate, x.SurrenderDate, x.ClientId }) }));
        return new(anchor.Account.Client, version,
            files.DistinctBy(x => x.Sha256).Select(x => new FeeClientSource(x.PerformanceFeeProfileId, new(x.Id, x.FileName, x.Sha256, x.ImportedBy, x.ImportedAtUtc))).ToList(), rows,
            FeeAccountTransferMatcher.Match(rows.Where(x => x.Account != null).SelectMany(x => x.Ledger.Rows)))
            { CapitalRouting = FeeCapitalRoutes.Build(rows.Where(x => x.Account != null).SelectMany(x => x.Ledger.Rows)) };
    }

    private static List<FeeStatementDocument> ReadUploads(List<FeeStatementUpload> uploads)
    {
        if (uploads.Count is 0 or > 6 || uploads.Sum(x => (long)x.Content.Length) > 60L * 1024 * 1024)
            throw new ValidationException("Select 1 to 6 original XLSX statements, no more than 60 MB combined (20 MB each).");
        return uploads.Select(x => PerformanceFeeStatementReader.Read(x.Content, x.FileName)).ToList();
    }
    private static Task<List<PerformanceFeeProfile>> ClientProfiles(ApplicationDbContext db, int clientId) => db.PerformanceFeeProfiles
        .Include(x => x.Account).ThenInclude(x => x.Client).Include(x => x.Claims).Include(x => x.Events).Where(x => x.Account.ClientId == clientId).ToListAsync();
    private static string NormalizeAccount(string value) => value.Trim().ToUpperInvariant();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string PreparationHash(FeePreparation input)
    {
        var copy = FeeJson.Read<FeePreparation>(FeeJson.Write(input)); copy.AccountDependencies.Clear();
        return Hash(FeeJson.Write(copy));
    }
    private static async Task ValidateAccountDependencies(ApplicationDbContext db, PerformanceFeeProfile row, FeePreparation input)
    {
        foreach (var dependency in input.AccountDependencies)
        {
            var other = await db.PerformanceFeeProfiles.Include(x => x.Account).Include(x => x.Claims).SingleOrDefaultAsync(x => x.ClientInvestmentAccountId == dependency.AccountId);
            var otherInput = other is null ? null : FeeJson.Read<FeePreparation>(other.PreparationJson);
            var draft = other?.Claims.SingleOrDefault(x => x.Status == FeeClaimStages.Draft);
            if (other is null || other.CodexRequestVersion != null || other.Account.ClientId != row.Account.ClientId || PreparationHash(otherInput!) != dependency.PreparationHash ||
                (draft is not null && PreparationHash(FeeJson.Read<FeePreparation>(draft.InputJson)) != dependency.PreparationHash) ||
                otherInput!.AccountDependencies.SingleOrDefault(x => x.AccountId == row.ClientInvestmentAccountId)?.PreparationHash != PreparationHash(input))
                throw new ValidationException("A connected account's preparation changed. Rebuild and review the client calculations together before approval.");
        }
    }
}

public static class FeeAccountTransferMatcher
{
    public static List<FeeAccountTransfer> Match(IEnumerable<FeeStatementRow> rows)
    {
        var all = rows.Where(x => x.Kind == "Transfer" && x.Units != 0 && x.Amount != 0).DistinctBy(x => x.Key).ToList();
        var pairs = new List<(FeeStatementRow From, FeeStatementRow To)>();
        bool Match(FeeStatementRow a, FeeStatementRow b) => a.Account != b.Account && a.Date == b.Date && a.Holding == b.Holding &&
            Math.Abs(a.Units + b.Units) <= 0.0001m && Math.Abs(a.Amount + b.Amount) <= 0.02m;
        foreach (var outgoing in all.Where(x => x.Amount < 0 && x.Units < 0))
        {
            var candidates = all.Where(x => x.Amount > 0 && x.Units > 0 && Match(outgoing, x)).ToList();
            if (candidates.Count != 1 || all.Count(x => x.Amount < 0 && x.Units < 0 && Match(x, candidates[0])) != 1) continue;
            pairs.Add((outgoing, candidates[0]));
        }
        var matches = pairs.GroupBy(x => new { x.From.Account, FromWrap = x.From.Wrap, ToAccount = x.To.Account, ToWrap = x.To.Wrap, x.From.Date })
            .Select(g =>
            {
                var scope = rows.Where(x => x.Account == g.Key.Account && x.Wrap == g.Key.FromWrap).ToList();
                var amount = -g.Sum(x => x.From.Amount);
                var before = FeeStatementLedger.ValueBefore(scope, g.Key.Date);
                var fullyExited = scope.Where(x => x.Date <= g.Key.Date).GroupBy(x => x.Holding)
                    .All(x => x.OrderBy(r => r.Date).ThenBy(r => r.SourceRow).Last().UnitBalance == 0);
                // Printed unit prices can reconstruct a full exit a cent away from its actual settled value.
                if (fullyExited && before is { } value && Math.Abs(value - amount) <= 0.02m) before = amount;
                return new FeeAccountTransfer(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", g.Select(x => x.From.Key + ":" + x.To.Key).Order())))),
                    g.Key.Account, g.Key.ToAccount, g.Key.FromWrap, g.Key.ToWrap, g.Key.Date, amount, before,
                    string.Join("; ", g.SelectMany(x => new[] { x.From.Source, x.To.Source })), g.Select(x => x.From.Key).ToArray(), g.Select(x => x.To.Key).ToArray());
            }).ToList();
        var pairedKeys = matches.SelectMany(x => x.SourceKeys.Concat(x.TargetKeys)).ToHashSet();
        var cashLegs = rows.Where(x => x.Kind == "Transfer" && !pairedKeys.Contains(x.Key) &&
                (x.Detail.Contains("Transfer Rands", StringComparison.OrdinalIgnoreCase) || x.Detail.Contains("Oorplasing Rand", StringComparison.OrdinalIgnoreCase)))
            .GroupBy(x => new { x.Account, x.Wrap, x.Date }).Select(g => new { g.Key, Rows = g.ToList(), Amount = g.Sum(x => x.Amount) })
            .Where(x => x.Amount != 0 && x.Rows.All(r => Math.Sign(r.Amount) == Math.Sign(x.Amount))).ToList();
        foreach (var outgoing in cashLegs.Where(x => x.Amount < 0))
        {
            var candidates = cashLegs.Where(x => x.Key.Account != outgoing.Key.Account && x.Key.Date == outgoing.Key.Date && x.Key.Wrap == outgoing.Key.Wrap && x.Amount > 0 && Math.Abs(x.Amount + outgoing.Amount) <= 0.02m).ToList();
            if (candidates.Count != 1) continue;
            var incoming = candidates[0];
            if (cashLegs.Count(x => x.Amount < 0 && x.Key.Account != incoming.Key.Account && x.Key.Date == incoming.Key.Date && x.Key.Wrap == incoming.Key.Wrap && Math.Abs(x.Amount + incoming.Amount) <= 0.02m) != 1) continue;
            var source = outgoing.Rows; var target = incoming.Rows;
            var before = FeeStatementLedger.ValueBefore(rows.Where(x => x.Account == outgoing.Key.Account && x.Wrap == outgoing.Key.Wrap).ToList(), outgoing.Key.Date);
            matches.Add(new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", source.Concat(target).Select(x => x.Key).Order())))),
                outgoing.Key.Account, incoming.Key.Account, outgoing.Key.Wrap, incoming.Key.Wrap, outgoing.Key.Date, incoming.Amount, before,
                "Unique same-day cash transfer, same wrap and matching settled totals; " + string.Join("; ", source.Concat(target).Select(x => x.Source)), source.Select(x => x.Key).ToArray(), target.Select(x => x.Key).ToArray()));
        }
        return matches;
    }
}
