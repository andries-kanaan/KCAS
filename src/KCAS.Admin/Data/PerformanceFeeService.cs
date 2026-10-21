using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed partial class PerformanceFeeService(IDbContextFactory<ApplicationDbContext> factory)
{
    public async Task<ClientInvestmentAccount> AccountAsync(int id, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await Actor(db, principal, false);
        var account = await db.ClientInvestmentAccounts.AsNoTracking().Include(x => x.Client).SingleAsync(x => x.Id == id);
        await ComplianceWorkflowAccess.VisibleAsync(db, account.Client, actor.Id);
        return account;
    }

    public async Task RequireSubmissionAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await Actor(db, principal, true);
        if (!await ComplianceWorkflowAccess.PermissionAsync(db, actor.Id, KcasPermissions.InvestmentsManage))
            throw new UnauthorizedAccessException("Current investment-management permission is required for administrator submissions.");
    }
    public async Task<List<PerformanceFeeProfile>> ListAsync(ClaimsPrincipal principal, int? accountId = null)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await Actor(db, principal, false);
        var admin = await ComplianceWorkflowAccess.IsAdminAsync(db, actor.Id);
        return await db.PerformanceFeeProfiles.AsNoTracking().Include(x => x.Account).ThenInclude(x => x.Client).Include(x => x.Claims)
            .Where(x => (admin || !x.Account.Client.ExcludeFromComplianceLists) && (accountId == null || x.ClientInvestmentAccountId == accountId))
            .OrderBy(x => x.NextReviewDate).ThenBy(x => x.Id).ToListAsync();
    }

    public async Task<List<ClientInvestmentAccount>> CandidatesAsync(string? search, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await Actor(db, principal, false);
        var admin = await ComplianceWorkflowAccess.IsAdminAsync(db, actor.Id);
        var query = db.ClientInvestmentAccounts.AsNoTracking().Include(x => x.Client)
            .Where(x => (admin || !x.Client.ExcludeFromComplianceLists) && !db.PerformanceFeeProfiles.Any(p => p.ClientInvestmentAccountId == x.Id));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Client.DisplayName.Contains(term) || x.Client.KanaanId != null && x.Client.KanaanId.Contains(term) || x.AccountNumber != null && x.AccountNumber.Contains(term));
        }
        return await query.OrderBy(x => x.Client.KanaanId).ThenBy(x => x.Id).Take(100).ToListAsync();
    }

    public async Task<PerformanceFeeProfile> LoadAsync(int id, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await Actor(db, principal, false);
        return await Row(db, id, actor);
    }

    public async Task<List<ClientInvestmentAccount>> InitialReviewsAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await Actor(db, principal, false);
        return await InitialReviewsAsync(db, await ComplianceWorkflowAccess.IsAdminAsync(db, actor.Id), Today.AddDays(30));
    }

    internal static Task<List<ClientInvestmentAccount>> InitialReviewsAsync(ApplicationDbContext db, bool admin, DateOnly through) =>
        db.ClientInvestmentAccounts.AsNoTracking().Include(x => x.Client)
            .Where(x => (admin || !x.Client.ExcludeFromComplianceLists) && x.SurrenderDate == null &&
                x.InvestmentDate != null && x.InvestmentDate <= through.AddYears(-1) &&
                (x.FundName == "Equities SA" || x.FundName == "Global SA" || x.FundName == "Compulsory SA") &&
                !db.PerformanceFeeProfiles.Any(p => p.ClientInvestmentAccountId == x.Id))
            .OrderBy(x => x.InvestmentDate).ThenBy(x => x.Id).ToListAsync();

    public async Task<string?> HandoffAsync(int id, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await Actor(db, principal, false);
        var row = await Row(db, id, actor);
        return row.CodexRequestVersion == row.Version ? await BuildHandoff(db, row) : null;
    }

    public async Task<int> RegisterAsync(int accountId, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await Actor(db, principal, true);
        var account = await db.ClientInvestmentAccounts.Include(x => x.Client).SingleAsync(x => x.Id == accountId);
        await ComplianceWorkflowAccess.VisibleAsync(db, account.Client, actor.Id);
        var existing = await db.PerformanceFeeProfiles.SingleOrDefaultAsync(x => x.ClientInvestmentAccountId == accountId);
        if (existing is not null) return existing.Id;
        // Registration is discovery, not confirmation of a chargeable mandate or a new fee baseline.
        var row = new PerformanceFeeProfile { ClientInvestmentAccountId = accountId,
            InceptionDate = account.InvestmentDate, NextReviewDate = account.InvestmentDate?.AddYears(1),
            PreparationJson = FeeJson.Write(new FeePreparation { Inception = account.InvestmentDate, From = account.InvestmentDate,
                Administrator = account.Administrator ?? "", WrapFund = account.FundName ?? "" }) };
        row.UpdatedBy = ComplianceWorkflowAccess.Label(actor);
        Event(row, null, "Registered", "Manual", actor, Today, "Account selected for mandate research; no fee terms inferred.", "KCAS account " + accountId);
        db.PerformanceFeeProfiles.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    public async Task<string> RequestCodexAsync(int id, string version, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await Actor(db, principal, true);
        var row = await Row(db, id, actor);
        ComplianceWorkflowAccess.Expect(row.Version, version);
        CheckPending(row);
        row.Version = ComplianceWorkflowAccess.NewVersion();
        row.CodexRequestVersion = row.Version;
        Touch(row, actor);
        Event(row, null, "CodexRequested", "Manual", actor, Today, "Prepare the client-specific PDF calculation from actual evidence.", "");
        await db.SaveChangesAsync();
        return await BuildHandoff(db, row);
    }

    private static async Task<string> BuildHandoff(ApplicationDbContext db, PerformanceFeeProfile row)
    {
        var id = row.Id;
        var account = row.Account;
        var sourceProfiles = db.PerformanceFeeProfiles.Where(x => x.Account.ClientId == account.ClientId).Select(x => x.Id);
        var statementFiles = await db.PerformanceFeeStatements.AsNoTracking().Where(x => sourceProfiles.Contains(x.PerformanceFeeProfileId))
            .Select(x => x.FileName + " / SHA256 " + x.Sha256).ToListAsync();
        var input = FeeJson.Read<FeePreparation>(row.PreparationJson);
        var issues = Calculate(row, input).Issues.Distinct().ToList();
        var scopes = input.Wraps.Count > 0 ? input.Wraps.Select(x => (Code: x.Code, Input: x.Input)) : [(Code: "Account", Input: input)];
        var established = string.Join('\n', scopes.Select(x => $"{x.Code}: mandate: {(string.IsNullOrWhiteSpace(x.Input.MandateReference) ? "not yet linked" : x.Input.MandateReference)}; method confirmed: {x.Input.MethodMatchesMandate}; fee excl. VAT: {x.Input.FeePercent}; CPI spread: {x.Input.HurdleSpreadPercent}; VAT: {x.Input.VatPercent}; previous-fee research: {(string.IsNullOrWhiteSpace(x.Input.PriorFeeReviewReference) ? "not yet recorded" : "already recorded - reuse LoadAsync")} ."));
        return $"Resolve SA performance-fee evidence gaps; profile {id}; request version {row.Version}; /investments/performance-fees/{id}.\n" +
            $"Client: {account.Client.DisplayName}; Kanaan ID {account.Client.KanaanId}; client ID {account.ClientId}; account ID {account.Id}; account {account.AccountNumber}.\n" +
            $"Folder: {account.Client.ClientFolder}. Administrator: {account.Administrator}; fund: {account.FundName}.\n" +
            "Already recorded (reuse; do not research again unless contradictory):\n" + established + "\n" +
            $"Verified module checkpoint: {(row.CheckpointJson is null ? "none recorded; use existing prior-fee research before requesting further evidence" : "already saved - preserve it via LoadAsync")}\n" +
            "Preserved administrator statements: " + string.Join("; ", statementFiles) + "\n" +
            "Outstanding items only:\n" + (issues.Count == 0 ? "None: no further Codex research is required; return for KI review." : string.Join('\n', issues.Select(x => "- " + x))) + "\n" +
            $"Load the saved preparation with LoadAsync({id}) and the client statement workspace with ClientWorkspaceAsync({id}); /investments/performance-fees/{id}/client-review. KCAS merges statement periods and calculates the fees. Do not manually repeat that calculation or completed compliance checks. Reuse the linked mandates, existing compliance evidence and previous PDF research; inspect actual source contents only for the outstanding questions. Do not scan folders, overwrite compliance links, invent facts or infer approval.\n" +
            "Retain separate wrap/account protection and supported mandate terms. Top-ups do not reset the original anniversary. Use actual benchmark segments with their evidenced CPI convention, not a projection assumption. Preserve prior deductions and transferred high-water marks; unsupported facts remain gaps. Do not default fee/VAT rates or mark method compatibility without evidence.\n" +
            $"Save supported evidence using PerformanceFeeService.PrepareAsync(id: {id}, version: \"{row.Version}\", input: updated existing FeePreparation, summary: substantive findings, principal: authorised Compliance account, codex: true). Do not replace established inputs with empty defaults. Use PrepareClientStatementsAsync afterwards to rebuild all connected calculations against the latest inputs; it does not require Codex. Report genuine remaining gaps and the next user action. Do not approve, submit, record payment or fee deduction on anyone's behalf.";
    }

    public async Task<int> PrepareAsync(int id, string version, FeePreparation input, string summary, ClaimsPrincipal principal, bool codex = false)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await Actor(db, principal, true);
        var row = await Row(db, id, actor);
        ComplianceWorkflowAccess.Expect(row.Version, version);
        CheckPending(row);
        if (codex && (row.CodexRequestVersion != version || !await ComplianceWorkflowAccess.ReceivesReviewsAsync(db, actor.Id)))
            throw new UnauthorizedAccessException("A current scoped handoff and authorised Compliance reviewer are required to save Codex results.");
        if (input.MandateAccountIds.Count > 200 || input.MandateAccountIds.Distinct().Count() != input.MandateAccountIds.Count ||
            await db.ClientInvestmentAccounts.CountAsync(x => x.ClientId == row.Account.ClientId && input.MandateAccountIds.Contains(x.Id)) != input.MandateAccountIds.Count)
            throw new ValidationException("Mandate coverage must refer only to this client's exact accounts.");
        if ((input.Wraps.Count > 0 ? input.Wraps.Select(x => x.Input) : new[] { input }).Any(x => x.UsePublishedCpi)) ApplyBenchmarks(input, await CpiObservations(db));
        ApplySharedPolicy(input, await SharedPolicy(db));
        var claim = SavePreparation(row, input, summary, actor, codex ? "Codex" : "Manual");
        await db.SaveChangesAsync();
        return claim.Id;
    }

    private static PerformanceFeeClaim SavePreparation(PerformanceFeeProfile row, FeePreparation input, string summary, ApplicationUser actor, string performer, bool reviewPointChange = false)
    {
        if (!reviewPointChange && FeeJson.Write(input.ReviewPoints) != FeeJson.Write(FeeJson.Read<FeePreparation>(row.PreparationJson).ReviewPoints))
            throw new ValidationException("Retain recorded disclosure review items; use the authorised reconciliation action to resolve them.");
        if (input.Wraps.Count > 20 || input.AccountDependencies.Count > 200 || input.AccountTransferDecisions.Count > 10000 || input.Wraps.Any(x => x.Input.Wraps.Count > 0 || x.Input.Transfers.Count > 0) ||
            input.Movements.Count + input.Wraps.Sum(x => x.Input.Movements.Count) > 10000 || input.Benchmarks.Count + input.Wraps.Sum(x => x.Input.Benchmarks.Count) > 2000 || FeeJson.Write(input).Length > 4000000)
            throw new ValidationException("The preparation exceeds the supported evidence size.");
        if (row.InceptionDate is { } original && row.CheckpointJson is not null && input.Inception != original)
            throw new ValidationException("The original anniversary is frozen after a completed calculation.");
        if (row.CheckpointJson is not null)
        {
            var checkpoint = System.Text.Json.JsonSerializer.Deserialize<FeeCheckpoint>(row.CheckpointJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            ExpectCheckpoint(input, checkpoint);
        }
        var result = Calculate(row, input);
        var claim = row.Claims.SingleOrDefault(x => x.Status == FeeClaimStages.Draft);
        claim ??= new PerformanceFeeClaim();
        if (!row.Claims.Contains(claim)) row.Claims.Add(claim);
        claim.InputJson = FeeJson.Write(input); claim.ResultJson = FeeJson.Write(result);
        claim.From = input.From ?? Today; claim.To = input.To ?? Today;
        claim.PreparedBy = ComplianceWorkflowAccess.Label(actor); claim.Performer = performer;
        claim.PreparedAtUtc = DateTime.UtcNow; claim.Version = ComplianceWorkflowAccess.NewVersion();
        row.PreparationJson = claim.InputJson; row.InceptionDate = input.Inception;
        if (row.CheckpointJson is null) row.NextReviewDate = input.Inception?.AddYears(1);
        row.CodexRequestVersion = null; row.Version = ComplianceWorkflowAccess.NewVersion(); Touch(row, actor);
        Event(row, null, "CalculationPrepared", claim.Performer, actor, Today, summary, $"{claim.Reference}; {result.Issues.Count} blocker(s)");
        return claim;
    }

    public async Task<bool> CanApproveAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await Actor(db, principal, false);
        return await ComplianceWorkflowAccess.PermissionAsync(db, actor.Id, KcasPermissions.InvestmentsManage) && await IsKi(db, actor);
    }

    public async Task ApproveAsync(int id, int claimId, string version, string reason, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await Actor(db, principal, true);
        if (!await ComplianceWorkflowAccess.PermissionAsync(db, actor.Id, KcasPermissions.InvestmentsManage) || !await IsKi(db, actor)) throw new UnauthorizedAccessException("An authorised current Key Individual with investment-management permission must approve the calculation.");
        var row = await Row(db, id, actor);
        ComplianceWorkflowAccess.Expect(row.Version, version);
        var claim = row.Claims.Single(x => x.Id == claimId);
        await StageApproval(db, row, claim, reason, actor);
        await db.SaveChangesAsync();
    }

    private static async Task StageApproval(ApplicationDbContext db, PerformanceFeeProfile row, PerformanceFeeClaim claim, string reason, ApplicationUser actor)
    {
        if (claim.Status != FeeClaimStages.Draft || row.CodexRequestVersion is not null) throw new ValidationException("Finish the current preparation before approval.");
        var input = FeeJson.Read<FeePreparation>(claim.InputJson);
        await ValidateStatementSnapshot(db, row, input);
        await ValidateAccountDependencies(db, row, input);
        await ValidateBenchmarkSnapshot(db, input);
        await ValidateMandateSources(db, row, input);
        await ValidateSharedPolicy(db, input);
        var result = Calculate(row, input);
        if (!result.IsReady) throw new ValidationException(string.Join(" ", result.Issues));
        if (row.Claims.Any(x => x.Id != claim.Id && FeeClaimStages.Frozen(x.Status) && x.From < claim.To && x.To > claim.From))
            throw new ValidationException("This period overlaps an existing approved/completed fee calculation.");
        claim.ResultJson = FeeJson.Write(result);
        claim.ApprovedBy = ComplianceWorkflowAccess.Label(actor); claim.ApprovedAtUtc = DateTime.UtcNow;
        claim.Status = result.Total == 0 ? FeeClaimStages.NoFee : FeeClaimStages.Approved;
        if (result.Total == 0)
        {
            if (result.NextCheckpoint is not null) Complete(row, result, claim.Reference);
            else if (result.IsZeroRateReview && row.InceptionDate is { } first)
                row.NextReviewDate = PerformanceFeeCalculator.NextAnniversary(first, claim.To);
        }
        row.Version = ComplianceWorkflowAccess.NewVersion(); Touch(row, actor);
        Event(row, claim.Id, claim.Status == FeeClaimStages.NoFee ? "ZeroFeeReviewApproved" : "Approved", "Manual", actor, Today, reason, claim.Reference, result.Total);
    }

    public async Task RecordAsync(int id, int claimId, string version, string action, DateOnly occurredOn, string reference, string summary, decimal? amount, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var actor = await Actor(db, principal, true);
        var row = await Row(db, id, actor);
        ComplianceWorkflowAccess.Expect(row.Version, version);
        var claim = row.Claims.Single(x => x.Id == claimId);
        if (!await ComplianceWorkflowAccess.PermissionAsync(db, actor.Id, KcasPermissions.InvestmentsManage)) throw new UnauthorizedAccessException("Investment-management permission is required to record administrator actions.");
        if (occurredOn > Today || occurredOn < claim.To && !(action == FeeClaimStages.Voided && claim.Status == FeeClaimStages.Draft)) throw new ValidationException("Use an actual event date on/after the calculation date, not a future date.");
        if (claim.ApprovedAtUtc is { } approval && occurredOn < DateOnly.FromDateTime(approval.ToLocalTime()))
            throw new ValidationException("An administrator action cannot predate the approval of this claim.");
        if (row.Events.Any(x => x.ClaimId == claim.Id && x.Action != "Voided" && x.OccurredOn > occurredOn))
            throw new ValidationException("The event predates an already recorded claim action.");
        reference = ComplianceWorkflowAccess.Required(reference, "Actual event evidence/reference", 2000);
        if (amount is { } money && (money < 0 || money > 1000000000000000m || money != decimal.Round(money, 2)))
            throw new ValidationException("Record an actual amount in cents within the supported range.");
        var result = FeeJson.Read<FeeCalculationResult>(claim.ResultJson);
        switch (action)
        {
            case FeeClaimStages.Submitted when claim.Status == FeeClaimStages.Approved:
                claim.Status = action; break;
            case FeeClaimStages.Accepted when claim.Status == FeeClaimStages.Submitted:
                claim.Status = action; break;
            case FeeClaimStages.Deducted when claim.Status == FeeClaimStages.Accepted:
                if (amount != result.Total) throw new ValidationException("The evidenced fee deduction must match the approved total including VAT. Resolve administrator adjustments before recording it.");
                claim.Status = action; Complete(row, result, claim.Reference); break;
            case FeeClaimStages.Paid when claim.Status == FeeClaimStages.Deducted:
                if (amount is null or <= 0 || claim.Paid + amount > result.Total) throw new ValidationException("Enter the actual receipt, no more than the unpaid approved fee.");
                if (row.Events.Any(x => x.ClaimId == claimId && x.Action == FeeClaimStages.Paid && string.Equals(x.Reference, reference, StringComparison.OrdinalIgnoreCase)))
                    throw new ValidationException("This payment reference is already recorded for the claim.");
                claim.Paid += amount.Value;
                if (claim.Paid == result.Total) claim.Status = FeeClaimStages.Paid;
                break;
            case FeeClaimStages.Returned when claim.Status is FeeClaimStages.Submitted or FeeClaimStages.Accepted:
                claim.Status = action; break;
            case FeeClaimStages.Voided when claim.Status is FeeClaimStages.Draft or FeeClaimStages.Approved:
                if (claim.Status == FeeClaimStages.Approved && !await IsKi(db, actor)) throw new UnauthorizedAccessException("A KI must withdraw an approved, unsubmitted claim.");
                claim.Status = action; break;
            default: throw new ValidationException("This action is not valid at the current claim stage. Submitted claims cannot be silently voided or rewritten.");
        }
        claim.Version = ComplianceWorkflowAccess.NewVersion(); row.Version = ComplianceWorkflowAccess.NewVersion(); Touch(row, actor);
        Event(row, claim.Id, action, "Manual", actor, occurredOn, summary, reference, amount);
        await db.SaveChangesAsync();
    }

    internal static async Task<ApplicationUser> Actor(ApplicationDbContext db, ClaimsPrincipal principal, bool write)
    {
        var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.IsApproved);
        if (principal.Identity?.IsAuthenticated != true || user is null) throw new UnauthorizedAccessException("An approved account is required.");
        var view = await ComplianceWorkflowAccess.PermissionAsync(db, user.Id, KcasPermissions.InvestmentsView);
        var manage = await ComplianceWorkflowAccess.PermissionAsync(db, user.Id, KcasPermissions.InvestmentsManage);
        var compliance = await ComplianceWorkflowAccess.PermissionAsync(db, user.Id, KcasPermissions.ComplianceManage) && await ComplianceWorkflowAccess.ReceivesReviewsAsync(db, user.Id);
        if (!view && !compliance || write && !manage && !compliance) throw new UnauthorizedAccessException("Investment access and current preparation permission are required.");
        return user;
    }
    private static async Task<PerformanceFeeProfile> Row(ApplicationDbContext db, int id, ApplicationUser actor)
    {
        var row = await db.PerformanceFeeProfiles.Include(x => x.Account).ThenInclude(x => x.Client).Include(x => x.Claims).Include(x => x.Events).SingleAsync(x => x.Id == id);
        await ComplianceWorkflowAccess.VisibleAsync(db, row.Account.Client, actor.Id);
        return row;
    }
    private static void CheckPending(PerformanceFeeProfile row)
    {
        if (row.Claims.Any(x => x.Status is FeeClaimStages.Approved or FeeClaimStages.Submitted or FeeClaimStages.Accepted))
            throw new ValidationException("Complete or formally resolve the existing approved claim before preparing another period.");
    }
    private static FeeCalculationResult Calculate(PerformanceFeeProfile row, FeePreparation input)
    {
        var result = PerformanceFeeCalculator.Calculate(input, Today);
        if (row.CheckpointJson is not null && input.To < row.NextReviewDate)
        {
            result.Issues.Add("The next anniversary has not been reached by this closing valuation. Retain the draft until the scheduled annual review.");
            result.NextCheckpoint = null;
        }
        return result;
    }
    private static void Complete(PerformanceFeeProfile row, FeeCalculationResult result, string reference)
    {
        var checkpoint = result.NextCheckpoint! with { Reference = reference };
        checkpoint = checkpoint with { Wraps = checkpoint.Wraps.Select(x => x with { Checkpoint = x.Checkpoint with { Reference = reference } }).ToList() };
        if (row.CheckpointJson is not null && checkpoint.Wraps.Count > 0)
        {
            var previous = System.Text.Json.JsonSerializer.Deserialize<FeeCheckpoint>(row.CheckpointJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if (previous is not null)
                checkpoint = checkpoint with { Wraps = checkpoint.Wraps.Concat(previous.Wraps.Where(x => checkpoint.Wraps.All(n => n.Code != x.Code))).ToList() };
        }
        row.CheckpointJson = FeeJson.Write(checkpoint);
        row.NextReviewDate = PerformanceFeeCalculator.NextAnniversary(row.InceptionDate!.Value, checkpoint.Date);
    }
    private static void ExpectCheckpoint(FeePreparation input, FeeCheckpoint checkpoint)
    {
        if (input.Wraps.Count == 0 && input.FeePercent == 0 && input.ZeroRateConfirmed || input.Wraps.Count > 0 && input.Wraps.All(x => x.Input.FeePercent == 0 && x.Input.ZeroRateConfirmed)) return;
        if (input.From != checkpoint.Date) throw new ValidationException("Start with the preserved checkpoint date.");
        if (checkpoint.Wraps.Count > 0)
        {
            if (checkpoint.Wraps.Any(x => input.Wraps.All(w => w.Code != x.Code)))
                throw new ValidationException("Preserve every wrap's protected checkpoint; scope changes require an evidenced continuity review.");
            foreach (var wrap in checkpoint.Wraps) ExpectCheckpoint(input.Wraps.Single(x => x.Code == wrap.Code).Input, wrap.Checkpoint);
        }
        else if (input.Wraps.Count > 0 || input.OpeningValue != checkpoint.Value || input.OpeningBenchmark != checkpoint.Benchmark || input.OpeningHighWaterMark != checkpoint.HighWaterMark)
            throw new ValidationException("Start with the preserved verified checkpoint, including its unrecovered hurdle/high-water mark. A pooled checkpoint cannot be silently split.");
    }
    private static void Touch(PerformanceFeeProfile row, ApplicationUser actor)
    { row.UpdatedBy = ComplianceWorkflowAccess.Label(actor); row.UpdatedAtUtc = DateTime.UtcNow; }
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private static async Task<bool> IsKi(ApplicationDbContext db, ApplicationUser actor) =>
        (await db.GovernanceRoleAssignments.AsNoTracking().Where(x => x.IsActive && (x.StartDate == null || x.StartDate <= Today) && (x.EndDate == null || x.EndDate >= Today)).ToListAsync())
        .Any(x => ComplianceApprovalRules.IsKeyIndividualRole(x.RoleType) && !string.IsNullOrWhiteSpace(actor.Email) && string.Equals(actor.Email, x.Email, StringComparison.OrdinalIgnoreCase));
    private static void Event(PerformanceFeeProfile row, int? claimId, string action, string performer, ApplicationUser actor, DateOnly date, string summary, string reference, decimal? amount = null) =>
        row.Events.Add(new() { ClaimId = claimId, Action = action, Performer = performer, RecordedBy = ComplianceWorkflowAccess.Label(actor), OccurredOn = date,
            Summary = ComplianceWorkflowAccess.Required(summary, "Substantive summary"), Reference = reference, Amount = amount });
}
