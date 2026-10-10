using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class InvestmentFundingService(IDbContextFactory<ApplicationDbContext> factory)
{
    public async Task<IReadOnlyList<ClientInvestmentAccount>> CandidatesAsync(int clientId, int accountId, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync();
        var permission = await ComplianceWorkflowAccess.PermissionAsync(db, principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", KcasPermissions.InvestmentsView)
            ? KcasPermissions.InvestmentsView : KcasPermissions.ComplianceManage;
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal, permission);
        var destination = await db.ClientInvestmentAccounts.Include(x => x.Client)
            .SingleAsync(x => x.Id == accountId && x.ClientId == clientId);
        await ComplianceWorkflowAccess.VisibleAsync(db, destination.Client, actor.Id);
        var candidates = await db.ClientInvestmentAccounts.AsNoTracking().Include(x => x.Client).Include(x => x.Transactions)
            .Where(x => x.Id != accountId && x.SurrenderDate != null &&
                (x.ClientId == clientId || (destination.Client.KanaanId != null && destination.Client.KanaanId != "" && x.Client.KanaanId == destination.Client.KanaanId)))
            .OrderBy(x => x.Client.DisplayName).ThenBy(x => x.AccountNumber).ToListAsync();
        if (!await ComplianceWorkflowAccess.IsAdminAsync(db, actor.Id)) candidates.RemoveAll(x => x.Client.ExcludeFromComplianceLists);
        return candidates;
    }

    public async Task<InvestmentFundingConnection> RecordAsync(int clientId, int accountId, int sourceAccountId,
        string evidenceReference, string reason, ClaimsPrincipal principal, string? performer = null, DateOnly? matchedThrough = null)
    {
        evidenceReference = ComplianceWorkflowAccess.Required(evidenceReference, "Evidence reference", 512);
        reason = ComplianceWorkflowAccess.Required(reason, "Funding continuity reason", 1000);
        await using var db = await factory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var permission = await ComplianceWorkflowAccess.PermissionAsync(db, principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", KcasPermissions.InvestmentsManage)
            ? KcasPermissions.InvestmentsManage : KcasPermissions.ComplianceManage;
        var actor = await ComplianceWorkflowAccess.ActorAsync(db, principal, permission);
        var accounts = await db.ClientInvestmentAccounts.Include(x => x.Client).Include(x => x.Transactions)
            .Where(x => x.Id == accountId || x.Id == sourceAccountId).ToListAsync();
        var destination = accounts.SingleOrDefault(x => x.Id == accountId && x.ClientId == clientId)
            ?? throw new ValidationException("Receiving investment not found.");
        var source = accounts.SingleOrDefault(x => x.Id == sourceAccountId && x.Id != accountId)
            ?? throw new ValidationException("Select a different source investment.");
        await ComplianceWorkflowAccess.VisibleAsync(db, source.Client, actor.Id);
        await ComplianceWorkflowAccess.VisibleAsync(db, destination.Client, actor.Id);
        if (source.ClientId != clientId && (string.IsNullOrWhiteSpace(destination.Client.KanaanId) ||
            source.Client.KanaanId != destination.Client.KanaanId))
            throw new ValidationException("Cross-client funding must be within the recorded family; an unrelated account is not combined.");
        var reviews = await db.ClientInvestmentReconciliationReviews.AsNoTracking()
            .Where(x => x.ClientInvestmentAccountId == sourceAccountId || x.ClientInvestmentAccountId == accountId).ToListAsync();
        var sourceReview = reviews.Where(x => x.ClientInvestmentAccountId == sourceAccountId).OrderByDescending(x => x.ReviewedAtUtc).ThenByDescending(x => x.Id).FirstOrDefault();
        if (sourceReview?.Outcome is ClientInvestmentReconciliationOutcomes.DuplicateContinuation or ClientInvestmentReconciliationOutcomes.WrongClientDuplicate)
            throw new ValidationException("Use the canonical source investment, not its duplicate record.");
        var values = await db.ClientFundValuations.AsNoTracking().Where(x => x.ClientId == source.ClientId || x.ClientId == clientId).ToListAsync();
        var familyAccounts = await db.ClientInvestmentAccounts.AsNoTracking().Include(x => x.Transactions)
            .Where(x => x.ClientId == source.ClientId || x.ClientId == clientId).ToListAsync();
        var previous = InvestmentReturnCalculator.AccountPeriod(source, familyAccounts, values, reviews, "ZAR");
        var next = InvestmentReturnCalculator.AccountPeriod(destination, familyAccounts, values, reviews, "ZAR");
        if (previous.Issues.Count != 0 || next.Issues.Count != 0)
            throw new ValidationException(string.Join(" ", previous.Issues.Concat(next.Issues).Distinct()));
        if (matchedThrough.HasValue && (matchedThrough < destination.InvestmentDate || matchedThrough > DateOnly.FromDateTime(DateTime.Today)))
            throw new ValidationException("Use the actual final receiving date, between inception and today.");
        var match = InvestmentReturnCalculator.MatchFundingPayments(previous, next, destination.InvestmentDate, source.SurrenderDate, matchedThrough);
        if (match is null) throw new ValidationException("The terminal payments and receiving capital cannot be matched uniquely. Resolve allocations, fees or missing cash flows before linking.");
        var existing = await db.InvestmentFundingConnections.AsNoTracking().OrderByDescending(x => x.RecordedAtUtc).ThenByDescending(x => x.Id).ToListAsync();
        var latest = existing.GroupBy(x => x.SourceAccountId).Select(g => g.First()).ToList();
        var allReviews = await db.ClientInvestmentReconciliationReviews.AsNoTracking().ToListAsync();
        var seen = new HashSet<int> { sourceAccountId };
        var cursor = accountId;
        while (true)
        {
            if (!seen.Add(cursor)) throw new ValidationException("A funding connection cannot create or extend a circular investment chain.");
            var onward = latest.SingleOrDefault(x => x.SourceAccountId == cursor);
            var review = allReviews.Where(x => x.ClientInvestmentAccountId == cursor).OrderByDescending(x => x.ReviewedAtUtc).ThenByDescending(x => x.Id).FirstOrDefault();
            var nextId = onward?.DestinationAccountId ?? (review?.Outcome == ClientInvestmentReconciliationOutcomes.Transferred ? review.RelatedClientInvestmentAccountId : null);
            if (!nextId.HasValue) break;
            cursor = nextId.Value;
        }
        var label = ComplianceWorkflowAccess.Label(actor);
        if (performer is not null && performer != "Codex") throw new ValidationException("Use the actual saving person, or Codex for an evidenced Codex review.");
        if (performer == "Codex" && !await ComplianceWorkflowAccess.ReceivesReviewsAsync(db, actor.Id))
            throw new UnauthorizedAccessException("Codex findings must be saved through an approved Compliance review account.");
        var connection = new InvestmentFundingConnection { SourceAccountId = sourceAccountId, DestinationAccountId = accountId,
            MatchedThroughDate = match.Incoming.Max(x => x.Date), EvidenceReference = evidenceReference, Reason = reason,
            SourceSnapshot = Snapshot(source), DestinationSnapshot = Snapshot(destination, match.Incoming.Max(x => x.Date)),
            PerformedBy = performer ?? label, RecordedBy = label };
        db.InvestmentFundingConnections.Add(connection);
        await db.SaveChangesAsync();
        ComplianceWorkflowAccess.Audit(db, nameof(InvestmentFundingConnection), connection.Id, "InvestmentFundingContinuityRecorded", actor,
            reason, new { sourceAccountId, accountId, connection.EvidenceReference, connection.PerformedBy, connection.SourceSnapshot,
                connection.DestinationSnapshot, Outgoing = match.Outgoing, Incoming = match.Incoming });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return connection;
    }

    // Portable historical cash-flow fingerprint: no local IDs, live valuations or private paths.
    public static string Snapshot(ClientInvestmentAccount account, DateOnly? through = null) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { account.LegacyInvestmentAccountId, account.AccountNumber,
            account.InvestmentDate, SurrenderDate = through.HasValue ? null : account.SurrenderDate,
            Transactions = account.Transactions.Where(x => !x.IsDeleted && (!through.HasValue || x.TransactionDate == null || x.TransactionDate <= through) &&
                ((x.InvestmentAmountZar ?? 0) != 0 || (x.InvestmentAmountForeign ?? 0) != 0 || (x.WithdrawalAmountZar ?? 0) != 0 || (x.WithdrawalAmountForeign ?? 0) != 0))
                .Select(x => new { x.LegacyInvestmentHistoryId, x.TransactionDate, x.InvestmentAmountZar, x.InvestmentAmountForeign,
                    x.WithdrawalAmountZar, x.WithdrawalAmountForeign, x.IsFinal, Frequency = x.InvestmentFrequency?.Trim().ToUpperInvariant() })
                .OrderBy(x => x.TransactionDate).ThenBy(x => x.LegacyInvestmentHistoryId).ThenBy(x => x.InvestmentAmountZar)
                .ThenBy(x => x.WithdrawalAmountZar).ThenBy(x => x.InvestmentAmountForeign).ThenBy(x => x.WithdrawalAmountForeign).ThenBy(x => x.IsFinal).ThenBy(x => x.Frequency) }))));
}
