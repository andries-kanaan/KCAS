using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class FeeSharedPolicy
{
    public string Version { get; set; } = "";
    public string Method { get; set; } = FeePreparation.SupportedMethod;
    public string MethodReference { get; set; } = "";
    public decimal VatPercent { get; set; } = 15;
    public string VatReference { get; set; } = "";
    public string RecordedBy { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string ApprovalReason { get; set; } = "";
    public const string Convention = "Separate client/wrap capital benchmark and high-water mark; published SA headline annual CPI plus the signed mandate spread, compounded (1 + annual rate)^(actual days/365) from each release date. Actual dated contributions increase protected capital; withdrawals reduce protection proportionally to the evidenced pre-withdrawal value. Matched same-wrap account transfers carry protection. A documented allocation to a different wrap starts that receiving fund's protection at its actual capital/date under the applicable-fund mandate; it does not inherit another fund's hurdle. A verified same-wrap cash rebalance retains protection; if exact full-wrap prices are missing for its cash residue, retain full protection conservatively rather than estimate a reduction. Prior underperformance survives zero-fee reviews. Positive fees reset protection to actual capital after the fee and applicable VAT, only after verified deduction. Annual anniversary review; no reset from a top-up or change of account. This is Kanaan's selected convention, not a universally prescribed industry formula.";
}

public sealed class FeeReviewPoint
{
    public string Key { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? ResolvedBy { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public string ResolutionReference { get; set; } = "";
    public bool Resolved => !string.IsNullOrWhiteSpace(ResolvedBy) && ResolvedAtUtc != null && !string.IsNullOrWhiteSpace(ResolutionReference);
}

public sealed partial class PerformanceFeeService
{
    internal const string FeePolicyCategory = "PerformanceFeePolicy";
    private const string FeePolicyCode = "SA-wrap-convention";

    public async Task<FeeSharedPolicy?> SharedPolicyAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); await Actor(db, principal, false);
        return await SharedPolicy(db);
    }

    internal static async Task<FeeSharedPolicy?> SharedPolicy(ApplicationDbContext db) =>
        await db.ComplianceReferenceValues.AsNoTracking().SingleOrDefaultAsync(x => x.Category == FeePolicyCategory && x.Code == FeePolicyCode && x.IsActive) is { Description: { } json }
            ? FeeJson.Read<FeeSharedPolicy>(json) : null;

    public async Task SaveSharedPolicyAsync(string? expectedVersion, string methodReference, decimal vatPercent, string vatReference, string reason, ClaimsPrincipal principal)
    {
        methodReference = ComplianceWorkflowAccess.Required(methodReference, "Calculation convention evidence / rationale", 4000);
        vatReference = ComplianceWorkflowAccess.Required(vatReference, "Kanaan VAT treatment evidence", 4000);
        reason = ComplianceWorkflowAccess.Required(reason, "Policy change reason", 2000);
        if (vatPercent is < 0 or > 100) throw new ValidationException("Use an evidenced VAT percentage between 0 and 100.");
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        if (!await ComplianceWorkflowAccess.ReceivesReviewsAsync(db, actor.Id)) throw new UnauthorizedAccessException("An authorised Compliance reviewer records the shared convention and VAT evidence.");
        var policy = new FeeSharedPolicy { Version = ComplianceWorkflowAccess.NewVersion(), MethodReference = methodReference,
            VatPercent = vatPercent, VatReference = vatReference, RecordedBy = ComplianceWorkflowAccess.Label(actor), RecordedAtUtc = DateTime.UtcNow };
        await WritePolicy(db, expectedVersion, policy, actor, reason);
    }

    public async Task ApproveSharedPolicyAsync(string version, string reason, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        if (!await IsKi(db, actor) || !await ComplianceWorkflowAccess.PermissionAsync(db, actor.Id, KcasPermissions.InvestmentsManage))
            throw new UnauthorizedAccessException("A current authorised KI must adopt the shared calculation convention.");
        var policy = await SharedPolicy(db) ?? throw new ValidationException("Record the proposed convention and VAT evidence first.");
        ComplianceWorkflowAccess.Expect(policy.Version, version);
        if (policy.ApprovedAtUtc != null) throw new ValidationException("This convention is already adopted.");
        policy.ApprovalReason = ComplianceWorkflowAccess.Required(reason, "KI convention adoption reason", 2000);
        policy.ApprovedBy = ComplianceWorkflowAccess.Label(actor); policy.ApprovedAtUtc = DateTime.UtcNow;
        policy.Version = ComplianceWorkflowAccess.NewVersion();
        await WritePolicy(db, version, policy, actor, policy.ApprovalReason);
    }

    private static async Task WritePolicy(ApplicationDbContext db, string? expected, FeeSharedPolicy policy, ApplicationUser actor, string reason)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var row = await db.ComplianceReferenceValues.AsNoTracking().SingleOrDefaultAsync(x => x.Category == FeePolicyCategory && x.Code == FeePolicyCode && x.IsActive);
        var old = row?.Description;
        ComplianceWorkflowAccess.Expect(row is null ? "" : FeeJson.Read<FeeSharedPolicy>(old!).Version, expected ?? "");
        var json = FeeJson.Write(policy);
        if (row is null)
        {
            row = new() { Category = FeePolicyCategory, Code = FeePolicyCode, Name = "SA wrap calculation convention and VAT", Description = json,
                UpdatedBy = ComplianceWorkflowAccess.Label(actor), UpdatedAtUtc = DateTime.UtcNow };
            db.ComplianceReferenceValues.Add(row); await db.SaveChangesAsync();
        }
        else if (await db.ComplianceReferenceValues.Where(x => x.Id == row.Id && x.Description == old).ExecuteUpdateAsync(x =>
            x.SetProperty(p => p.Description, json).SetProperty(p => p.UpdatedBy, ComplianceWorkflowAccess.Label(actor)).SetProperty(p => p.UpdatedAtUtc, DateTime.UtcNow)) != 1)
            throw new ValidationException("The shared convention changed. Reload before saving.");
        // Approved/submitted claims keep their frozen policy and result. Only dependent drafts are invalidated.
        var profiles = await db.PerformanceFeeProfiles.Include(x => x.Claims).Where(x => x.Claims.Any(c => c.Status == FeeClaimStages.Draft)).ToListAsync();
        foreach (var profile in profiles.Where(x => PolicyScopes(FeeJson.Read<FeePreparation>(x.PreparationJson)).Any(s => s.UseSharedPolicy)))
        {
            InvalidateDraft(profile, "Shared convention or VAT evidence changed. Prepare the client accounts again.");
            profile.Version = ComplianceWorkflowAccess.NewVersion(); profile.CodexRequestVersion = null; Touch(profile, actor);
        }
        ComplianceWorkflowAccess.Audit(db, "PerformanceFeePolicy", row.Id, policy.ApprovedAtUtc == null ? "Proposed" : "Adopted", actor, reason, policy);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    internal static IEnumerable<FeePreparation> PolicyScopes(FeePreparation input) => input.Wraps.Count > 0 ? input.Wraps.Select(x => x.Input) : [input];
    internal static bool PolicyEligible(FeePreparation child) => child.IsSouthAfricanWrap && child.Method == FeePreparation.SupportedMethod && child.FeePercent == 15 &&
        child.HurdleSpreadPercent == 3 && !string.IsNullOrWhiteSpace(child.MandateReference) && child.MandateEffectiveFrom != null && child.MandateEffectiveFrom <= child.From;

    internal static void ApplySharedPolicy(FeePreparation input, FeeSharedPolicy? policy)
    {
        foreach (var child in PolicyScopes(input).Where(x => x.UseSharedPolicy))
        {
            if (policy is null || !PolicyEligible(child))
            {
                child.MethodMatchesMandate = false;
                child.ReviewIssues.Add("Shared convention is unavailable or these mandate terms are outside its recorded 15% / CPI+3 SA-wrap scope.");
                continue;
            }
            var derivedUnchanged = child.MandateSourceAccountId != null && TermsHash(child) == child.MandateTermsSnapshot;
            child.SharedPolicy = FeeJson.Read<FeeSharedPolicy>(FeeJson.Write(policy));
            child.VatPercent = policy.VatPercent; child.VatReference = policy.VatReference;
            child.MethodMatchesMandate = policy.ApprovedAtUtc != null;
            child.MethodRationale = FeeSharedPolicy.Convention + " Evidence: " + policy.MethodReference +
                (policy.ApprovedAtUtc is null ? " Proposed; awaiting KI adoption." : $" Adopted by {policy.ApprovedBy} at {policy.ApprovedAtUtc:O}; version {policy.Version}.");
            if (derivedUnchanged) child.MandateTermsSnapshot = TermsHash(child);
        }
    }

    public async Task UseSharedPolicyForClientAsync(int id, string version, string reason, ClaimsPrincipal principal)
    {
        reason = ComplianceWorkflowAccess.Required(reason, "Shared convention applicability evidence", 4000);
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        var anchor = await Row(db, id, actor); var workspace = await ClientWorkspace(db, anchor, null);
        ComplianceWorkflowAccess.Expect(workspace.Version, version);
        var policy = await SharedPolicy(db) ?? throw new ValidationException("Record the shared convention and VAT evidence first.");
        var changed = 0;
        foreach (var profile in await ClientProfiles(db, anchor.Account.ClientId))
        {
            if (profile.Claims.Any(c => c.Status is FeeClaimStages.Approved or FeeClaimStages.Submitted or FeeClaimStages.Accepted)) continue;
            var input = FeeJson.Read<FeePreparation>(profile.PreparationJson);
            var children = PolicyScopes(input).Where(PolicyEligible).ToList();
            if (children.Count == 0) continue;
            if (children.Any(c => !c.UseSharedPolicy && c.VatPercent != null && c.VatPercent != policy.VatPercent))
                throw new ValidationException("Existing client-specific VAT treatment differs. Resolve it explicitly before using shared settings.");
            foreach (var child in children) child.UseSharedPolicy = true;
            ApplySharedPolicy(input, policy); profile.PreparationJson = FeeJson.Write(input);
            InvalidateDraft(profile, "Shared convention and VAT selected. Prepare the connected accounts again.");
            profile.Version = ComplianceWorkflowAccess.NewVersion(); profile.CodexRequestVersion = null; Touch(profile, actor);
            Event(profile, null, "SharedPolicySelected", "Manual", actor, Today, reason, policy.Version); changed++;
        }
        if (changed == 0) throw new ValidationException("No evidenced matching 15% / CPI+3 SA-wrap mandate is available; no client terms were inferred.");
        await db.SaveChangesAsync();
    }

    public async Task RecordFeeReviewPointAsync(int id, string version, string key, string detail, ClaimsPrincipal principal)
    {
        key = ComplianceWorkflowAccess.Required(key, "Review item key", 80); detail = ComplianceWorkflowAccess.Required(detail, "Disclosure discrepancy", 4000);
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        var row = await Row(db, id, actor); ComplianceWorkflowAccess.Expect(row.Version, version); CheckPending(row);
        var input = FeeJson.Read<FeePreparation>(row.PreparationJson);
        if (input.ReviewPoints.Any(x => x.Key == key)) throw new ValidationException("This review item already exists.");
        input.ReviewPoints.Add(new() { Key = key, Detail = detail });
        SavePreparation(row, input, "Recorded fee disclosure discrepancy: " + detail, actor, "Manual", reviewPointChange: true); await db.SaveChangesAsync();
    }

    public async Task ResolveFeeReviewPointAsync(int id, string version, string key, string reference, ClaimsPrincipal principal)
    {
        reference = ComplianceWorkflowAccess.Required(reference, "Actual reconciliation evidence and conclusion", 4000);
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        if (!await IsKi(db, actor) || !await ComplianceWorkflowAccess.PermissionAsync(db, actor.Id, KcasPermissions.InvestmentsManage))
            throw new UnauthorizedAccessException("An authorised KI records the evidence resolving a fee disclosure discrepancy.");
        var row = await Row(db, id, actor); ComplianceWorkflowAccess.Expect(row.Version, version); CheckPending(row);
        var input = FeeJson.Read<FeePreparation>(row.PreparationJson); var point = input.ReviewPoints.Single(x => x.Key == key);
        if (point.Resolved) throw new ValidationException("This review item has already been resolved.");
        point.ResolutionReference = reference; point.ResolvedBy = ComplianceWorkflowAccess.Label(actor); point.ResolvedAtUtc = DateTime.UtcNow;
        SavePreparation(row, input, "Fee disclosure discrepancy resolved: " + reference, actor, "Manual", reviewPointChange: true); await db.SaveChangesAsync();
    }

    private static async Task ValidateSharedPolicy(ApplicationDbContext db, FeePreparation input)
    {
        var scopes = PolicyScopes(input).Where(x => x.UseSharedPolicy).ToList();
        if (scopes.Count == 0) return;
        var policy = await SharedPolicy(db);
        foreach (var child in scopes)
            if (policy is null || policy.ApprovedAtUtc == null || !PolicyEligible(child) || FeeJson.Write(child.SharedPolicy) != FeeJson.Write(policy) ||
                !child.MethodMatchesMandate || child.VatPercent != policy.VatPercent || child.VatReference != policy.VatReference)
                throw new ValidationException("Refresh the shared convention/VAT snapshot and obtain KI adoption before approving this claim.");
    }
}
