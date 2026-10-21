using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace KCAS.Admin.Data;

public sealed partial class PerformanceFeeService
{
    public async Task SaveMandateCoverageAsync(int id, string version, int[] accountIds, string reference, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        var profile = await Row(db, id, actor); ComplianceWorkflowAccess.Expect(profile.Version, version); CheckPending(profile);
        if (accountIds.Length > 200 || accountIds.Distinct().Count() != accountIds.Length ||
            await db.ClientInvestmentAccounts.CountAsync(x => x.ClientId == profile.Account.ClientId && accountIds.Contains(x.Id)) != accountIds.Length)
            throw new ValidationException("Mandate coverage must identify exact accounts belonging to this client.");
        var input = FeeJson.Read<FeePreparation>(profile.PreparationJson);
        input.MandateAccountIds = accountIds.ToList();
        input.MandateCoverageReference = accountIds.Length == 0 ? "" : ComplianceWorkflowAccess.Required(reference, "Actual mandate coverage evidence", 4000);
        profile.PreparationJson = FeeJson.Write(input);
        InvalidateDraft(profile, "Recorded mandate coverage changed. Prepare the account calculations again.");
        profile.Version = ComplianceWorkflowAccess.NewVersion(); profile.CodexRequestVersion = null; Touch(profile, actor);
        Event(profile, null, "MandateCoverageRecorded", "Manual", actor, Today, "Recorded account-specific coverage for reuse of this client's mandate terms; no terms inferred from a shared family ID.", input.MandateCoverageReference);
        await db.SaveChangesAsync();
    }

    internal static void ApplyCoveredMandate(FeePreparation input, int accountId, IEnumerable<FeePreparation> sources)
    {
        var covering = sources.Where(x => x.MandateAccountIds.Contains(accountId) && !string.IsNullOrWhiteSpace(x.MandateCoverageReference)).ToList();
        foreach (var wrap in input.Wraps)
        {
            var child = wrap.Input;
            var candidates = covering.SelectMany(x => x.Wraps.Where(w => w.Code == wrap.Code).Select(w => (Root: x, Input: w.Input)))
                .Where(x => !string.IsNullOrWhiteSpace(x.Input.MandateReference) && x.Input.MandateEffectiveFrom <= child.From && x.Input.FeePercent != null).ToList();
            if (child.MandateSourceAccountId != null && candidates.Count == 0)
                child.ReviewIssues.Add("The recorded source mandate no longer covers this account/period; re-establish actual coverage before approval.");
            if (candidates.Count == 0) continue;
            if (candidates.Select(x => FeeJson.Write(new { x.Input.MandateReference, x.Input.MandateEffectiveFrom, x.Input.FeePercent, x.Input.HurdleSpreadPercent, x.Input.VatPercent, x.Input.Method })).Distinct().Count() != 1)
            { child.EvidenceIssues.Add("Recorded covering mandates disagree for this wrap. Resolve the applicable version rather than copying terms."); continue; }
            var source = candidates[0]; var terms = source.Input;
            if (child.MandateSourceAccountId == null && !string.IsNullOrWhiteSpace(child.MandateReference))
            {
                // Attach provenance to an earlier generated reuse, but never adopt unrelated manual terms.
                if (child.ScopeRationale == $"{child.WrapFund} only; account ID {accountId}. Actual mandate coverage: {source.Root.MandateCoverageReference}" && TermsHash(child) == TermsHash(terms))
                { child.MandateSourceAccountId = source.Root.MandateSourceAccountId; child.MandateTermsSnapshot = TermsHash(terms); }
                continue;
            }
            if (child.MandateSourceAccountId != null && TermsHash(child) != child.MandateTermsSnapshot || child.MandateSourceAccountId == null &&
                (child.FeePercent != null && child.FeePercent != terms.FeePercent || child.HurdleSpreadPercent != null && child.HurdleSpreadPercent != terms.HurdleSpreadPercent || child.VatPercent != null && child.VatPercent != terms.VatPercent))
            { child.EvidenceIssues.Add("Existing account terms differ from the recorded covering mandate; no terms were overwritten."); continue; }
            child.MandateReference = terms.MandateReference; child.MandateEffectiveFrom = terms.MandateEffectiveFrom;
            child.FeePercent = terms.FeePercent; child.HurdleSpreadPercent = terms.HurdleSpreadPercent;
            child.VatPercent = terms.VatPercent; child.VatReference = terms.VatReference;
            child.Method = terms.Method; child.MethodMatchesMandate = terms.MethodMatchesMandate; child.MethodRationale = terms.MethodRationale;
            child.UseSharedPolicy = terms.UseSharedPolicy; child.SharedPolicy = terms.SharedPolicy is null ? null : FeeJson.Read<FeeSharedPolicy>(FeeJson.Write(terms.SharedPolicy));
            child.ZeroRateConfirmed = terms.ZeroRateConfirmed; child.UsePublishedCpi = terms.UsePublishedCpi;
            child.MandateSourceAccountId = source.Root.MandateSourceAccountId;
            child.MandateTermsSnapshot = TermsHash(terms);
            child.ScopeRationale = $"{child.WrapFund} only; account ID {accountId}. Actual mandate coverage: {source.Root.MandateCoverageReference}";
            child.AutomationNotes.Add("Reused evidenced same-client, same-wrap mandate coverage. No amounts, dates of inception, prior fee conclusions or checkpoints were copied.");
        }
    }

    private static string TermsHash(FeePreparation input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FeeJson.Write(new
    { input.MandateReference, input.MandateEffectiveFrom, input.FeePercent, input.HurdleSpreadPercent, input.VatPercent, input.VatReference,
      input.Method, input.MethodMatchesMandate, input.MethodRationale, input.ZeroRateConfirmed }))));

    private static async Task ValidateMandateSources(ApplicationDbContext db, PerformanceFeeProfile profile, FeePreparation input)
    {
        foreach (var wrap in input.Wraps.Where(x => x.Input.MandateSourceAccountId != null))
        {
            var child = wrap.Input;
            var source = await db.PerformanceFeeProfiles.AsNoTracking().Include(x => x.Account).SingleOrDefaultAsync(x => x.ClientInvestmentAccountId == child.MandateSourceAccountId);
            var sourceInput = source is null ? null : FeeJson.Read<FeePreparation>(source.PreparationJson);
            var terms = sourceInput?.Wraps.SingleOrDefault(x => x.Code == wrap.Code)?.Input;
            if (source?.Account.ClientId != profile.Account.ClientId || sourceInput?.MandateAccountIds.Contains(profile.ClientInvestmentAccountId) != true ||
                string.IsNullOrWhiteSpace(sourceInput.MandateCoverageReference) || terms is null || TermsHash(terms) != child.MandateTermsSnapshot || TermsHash(child) != child.MandateTermsSnapshot)
                throw new ValidationException("Reused mandate terms or coverage changed. Prepare the client calculations again before approval; no stale source terms can be claimed.");
        }
    }
}
