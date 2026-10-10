using Excel.FinancialFunctions;

namespace KCAS.Admin.Data;

public sealed record InvestmentReturnCashFlow(
    DateOnly Date, decimal Amount, int AccountId, int? TransactionId, string Reference,
    bool IsInternalTransfer = false, bool IsClosingValue = false);

public sealed class InvestmentReturnResult
{
    public string Currency { get; init; } = "ZAR";
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal? AnnualisedPercent { get; set; }
    public decimal? Gain { get; set; }
    public List<string> Issues { get; } = [];
    public List<string> Notes { get; } = [];
    public List<InvestmentReturnCashFlow> CashFlows { get; } = [];
    public bool IsAvailable => AnnualisedPercent.HasValue && Issues.Count == 0;
}

public sealed record InvestmentReturnReport(
    ClientInvestmentAccount Account,
    IReadOnlyList<ClientInvestmentAccount> History,
    InvestmentReturnResult ShortTerm,
    InvestmentReturnResult LongTerm)
{
    public List<InvestmentFundingHistoryEntry> FundingHistory { get; init; } = [];
}

public sealed record InvestmentFundingMatch(IReadOnlyList<InvestmentReturnCashFlow> Outgoing,
    IReadOnlyList<InvestmentReturnCashFlow> Incoming);
public sealed record InvestmentFundingHistoryEntry(int SourceAccountId, int DestinationAccountId,
    string EvidenceReference, string Reason, string PerformedBy, DateTime RecordedAtUtc, InvestmentFundingMatch Match);

public static class InvestmentReturnCalculator
{
    public static InvestmentReturnReport Build(
        ClientInvestmentAccount account,
        IReadOnlyList<ClientInvestmentAccount> accounts,
        IReadOnlyList<ClientFundValuation> valuations,
        IReadOnlyList<ClientInvestmentReconciliationReview> reviews,
        string currency = "ZAR",
        DateOnly? today = null,
        IReadOnlyList<InvestmentFundingConnection>? connections = null)
    {
        var asAt = today ?? DateOnly.FromDateTime(DateTime.Today);
        var latestReviews = reviews.GroupBy(x => x.ClientInvestmentAccountId)
            .Select(g => g.OrderByDescending(x => x.ReviewedAtUtc).ThenByDescending(x => x.Id).First()).ToList();
        var latestConnections = (connections ?? []).GroupBy(x => x.SourceAccountId)
            .Select(g => g.OrderByDescending(x => x.RecordedAtUtc).ThenByDescending(x => x.Id).First()).ToList();
        var fundingHistory = new List<InvestmentFundingHistoryEntry>();
        var shortTerm = BuildAccount(account, accounts, valuations, latestReviews, currency, asAt);
        Calculate(shortTerm);
        var longTerm = new InvestmentReturnResult { Currency = currency };
        var history = new List<ClientInvestmentAccount> { account };
        var seen = new HashSet<int> { account.Id };
        var current = account;
        while (true)
        {
            var incoming = latestReviews.Where(x => x.RelatedClientInvestmentAccountId == current.Id &&
                x.Outcome == ClientInvestmentReconciliationOutcomes.Transferred &&
                !latestConnections.Any(c => c.SourceAccountId == x.ClientInvestmentAccountId)).ToList();
            foreach (var connection in latestConnections.Where(x => x.DestinationAccountId == current.Id))
                incoming.Add(new() { ClientInvestmentAccountId = connection.SourceAccountId,
                    RelatedClientInvestmentAccountId = current.Id, Outcome = ClientInvestmentReconciliationOutcomes.Transferred,
                    EvidenceReference = connection.EvidenceReference, Reason = connection.Reason,
                    ReviewedAtUtc = connection.RecordedAtUtc, ReviewedBy = connection.PerformedBy });
            if (incoming.Count == 0) break;
            var next = BuildAccount(current, accounts, valuations, latestReviews, currency, asAt);
            var inceptionLinks = new List<ClientInvestmentReconciliationReview>();
            foreach (var candidate in incoming)
            {
                var source = accounts.SingleOrDefault(x => x.Id == candidate.ClientInvestmentAccountId);
                if (source is null || seen.Contains(source.Id))
                {
                    longTerm.Issues.Add("The transfer history is missing an account or contains a circular link.");
                    continue;
                }
                if (latestReviews.Any(x => x.ClientInvestmentAccountId == source.Id &&
                    x.Outcome is ClientInvestmentReconciliationOutcomes.DuplicateContinuation or ClientInvestmentReconciliationOutcomes.WrongClientDuplicate))
                {
                    longTerm.Issues.Add("The funding source is a duplicate record; establish the connection from the canonical investment.");
                    continue;
                }
                if (source.ClientId != current.ClientId && (string.IsNullOrWhiteSpace(source.Client?.KanaanId) ||
                    source.Client.KanaanId != current.Client?.KanaanId))
                {
                    longTerm.Issues.Add("The predecessor belongs to another client record outside the established family; capital continuity has not been established.");
                    continue;
                }
                var sourceValues = ClientInvestmentStatusClassifier.MatchingValuations(source,
                    valuations.Where(x => x.ClientId == source.ClientId)).ToList();
                var explicitConnection = latestConnections.SingleOrDefault(x => x.SourceAccountId == source.Id);
                var currentEvidence = explicitConnection is null
                    ? candidate.AppliedSurrenderDate == source.SurrenderDate && candidate.SnapshotSha256 == InvestmentReconciliationService.CalculateSnapshot(source, sourceValues)
                    : explicitConnection.SourceSnapshot == InvestmentFundingService.Snapshot(source) &&
                      explicitConnection.DestinationSnapshot == InvestmentFundingService.Snapshot(current, explicitConnection.MatchedThroughDate);
                if (string.IsNullOrWhiteSpace(candidate.EvidenceReference) || source.SurrenderDate is null || !currentEvidence)
                {
                    longTerm.Issues.Add($"Transfer evidence for {source.AccountNumber} needs a current reconciliation review.");
                    continue;
                }
                var sourcePeriod = BuildAccount(source, accounts, valuations, latestReviews, currency, asAt);
                var finalPaymentDate = sourcePeriod.CashFlows.Where(x => x.Amount > 0 && !x.IsClosingValue)
                    .Select(x => (DateOnly?)x.Date).Max() ?? source.SurrenderDate;
                var fundingMatch = MatchFundingPayments(sourcePeriod, next, current.InvestmentDate, source.SurrenderDate,
                    explicitConnection?.MatchedThroughDate);
                if (current.InvestmentDate.HasValue && finalPaymentDate > current.InvestmentDate && fundingMatch is null)
                {
                    if (!next.CashFlows.Any(x => x.Amount < 0 && x.Date >= finalPaymentDate))
                        longTerm.Issues.Add($"Later transfer from {source.AccountNumber} has no recorded dated top-up in {current.AccountNumber}.");
                    else
                        longTerm.Notes.Add($"Later linked funding from {source.AccountNumber} is counted as recorded top-up capital in {current.AccountNumber}, not as its original predecessor. The source account's earlier returns are outside this investment chain.");
                    continue;
                }
                inceptionLinks.Add(candidate);
            }
            if (longTerm.Issues.Count > 0 || inceptionLinks.Count == 0) break;
            if (inceptionLinks.Count != 1)
            {
                longTerm.Issues.Add("Multiple predecessor investments require a reviewed allocation before a since-inception return can be calculated.");
                break;
            }
            var link = inceptionLinks[0];
            var predecessor = accounts.SingleOrDefault(x => x.Id == link.ClientInvestmentAccountId);
            if (predecessor is null || !seen.Add(predecessor.Id))
            {
                longTerm.Issues.Add("The transfer history is missing an account or contains a circular link.");
                break;
            }
            var previous = BuildAccount(predecessor, accounts, valuations, latestReviews, currency, asAt);
            longTerm.Issues.AddRange(previous.Issues);
            var match = MatchFundingPayments(previous, next, current.InvestmentDate, predecessor.SurrenderDate,
                latestConnections.SingleOrDefault(x => x.SourceAccountId == predecessor.Id)?.MatchedThroughDate);
            if (match is null)
            {
                longTerm.Issues.Add($"Transfer {predecessor.AccountNumber} to {current.AccountNumber} needs uniquely matched paid-out and reinvested amounts/dates (including any fees or retained balance).");
                break;
            }
            foreach (var flow in match.Outgoing) MarkInternal(longTerm, flow);
            foreach (var flow in match.Incoming) MarkInternal(longTerm, flow);
            fundingHistory.Insert(0, new(predecessor.Id, current.Id, link.EvidenceReference, link.Reason,
                link.ReviewedBy, link.ReviewedAtUtc, match));
            if (predecessor.ClientId != current.ClientId)
                longTerm.Notes.Add($"Capital history crosses client records: {predecessor.Client?.DisplayName} to {current.Client?.DisplayName}. This follows the invested capital, not one person's unchanged ownership.");
            history.Insert(0, predecessor);
            current = predecessor;
        }

        foreach (var item in history)
        {
            var segment = BuildAccount(item, accounts, valuations, latestReviews, currency, asAt);
            longTerm.Issues.AddRange(segment.Issues);
            foreach (var flow in segment.CashFlows)
            {
                if (flow.IsClosingValue && item != account) continue;
                if (longTerm.CashFlows.Any(x => x.AccountId == flow.AccountId && x.TransactionId == flow.TransactionId &&
                    x.Date == flow.Date && x.Amount == flow.Amount && !x.IsClosingValue)) continue;
                longTerm.CashFlows.Add(flow);
            }
        }
        if (latestReviews.Any(x => x.ClientInvestmentAccountId == account.Id &&
            x.Outcome is ClientInvestmentReconciliationOutcomes.DuplicateContinuation or
                ClientInvestmentReconciliationOutcomes.WrongClientDuplicate))
        {
            longTerm.Issues.Add("This is a duplicate/continuation record, not an independently funded investment. Use the canonical account and confirm its original funding history.");
            shortTerm.Issues.Add(longTerm.Issues[^1]);
            shortTerm.AnnualisedPercent = null;
        }
        var original = history[0];
        if (original.LegacyLinkedAccountId.HasValue ||
            original.Transactions.Any(x => !x.IsDeleted && x.TransactionDate == original.InvestmentDate &&
                (x.Description?.Contains("transfer", StringComparison.OrdinalIgnoreCase) == true ||
                 x.Description?.Contains("oorgedra", StringComparison.OrdinalIgnoreCase) == true)))
            longTerm.Issues.Add("An earlier linked account is recorded, but a reviewed transfer chain has not been established.");
        Calculate(longTerm);
        return new(account, history, shortTerm, longTerm) { FundingHistory = fundingHistory };
    }

    private static void MarkInternal(InvestmentReturnResult result, InvestmentReturnCashFlow flow) =>
        result.CashFlows.Add(flow with { IsInternalTransfer = true });

    internal static InvestmentReturnResult AccountPeriod(ClientInvestmentAccount account,
        IReadOnlyList<ClientInvestmentAccount> accounts, IReadOnlyList<ClientFundValuation> valuations,
        IReadOnlyList<ClientInvestmentReconciliationReview> reviews, string currency) => BuildAccount(account, accounts, valuations,
            reviews.GroupBy(x => x.ClientInvestmentAccountId).Select(g => g.OrderByDescending(x => x.ReviewedAtUtc).ThenByDescending(x => x.Id).First()).ToList(),
            currency, DateOnly.FromDateTime(DateTime.Today));

    internal static InvestmentFundingMatch? MatchFundingPayments(InvestmentReturnResult previous,
        InvestmentReturnResult next, DateOnly? inception, DateOnly? surrender, DateOnly? matchedThrough = null)
    {
        if (inception is null || surrender is null) return null;
        var contributions = next.CashFlows.Where(x => !x.IsClosingValue && x.Amount < 0 && (!matchedThrough.HasValue || x.Date <= matchedThrough))
            .GroupBy(x => x.Date).OrderBy(x => x.Key).ToList();
        if (contributions.Count == 0 || contributions[0].Key != inception) return null;
        var initial = contributions[0].ToList();
        var initialPayments = MatchTerminalPayments(previous, initial, surrender);
        // Staged receiving capital must match a terminal payout sequence. Do not search arbitrary subsets.
        var payments = previous.CashFlows.Where(x => !x.IsClosingValue && x.Amount > 0)
            .GroupBy(x => x.Date).OrderByDescending(x => x.Key).ToList();
        var matches = new List<InvestmentFundingMatch>();
        if (initialPayments.Count > 0 && (!matchedThrough.HasValue || initial.Max(x => x.Date) == matchedThrough))
            matches.Add(new(initialPayments, initial));
        var outgoing = new List<InvestmentReturnCashFlow>();
        foreach (var group in payments)
        {
            if (group.Key > surrender || previous.CashFlows.Any(x => x.Amount < 0 && x.Date >= group.Key)) break;
            outgoing.AddRange(group);
            var incoming = new List<InvestmentReturnCashFlow>();
            foreach (var receipt in contributions)
            {
                incoming.AddRange(receipt);
                if (-incoming.Sum(x => x.Amount) > outgoing.Sum(x => x.Amount)) break;
                if (incoming.Count == initial.Count || -incoming.Sum(x => x.Amount) != outgoing.Sum(x => x.Amount)) continue;
                if (matchedThrough.HasValue && receipt.Key != matchedThrough) continue;
                // Every receipt must have already been paid out; money in transit is not a client withdrawal.
                if (incoming.GroupBy(x => x.Date).Any(g => -incoming.Where(x => x.Date <= g.Key).Sum(x => x.Amount) >
                    outgoing.Where(x => x.Date <= g.Key).Sum(x => x.Amount))) continue;
                if (next.CashFlows.Any(x => x.Amount > 0 && !x.IsClosingValue && x.Date <= receipt.Key)) continue;
                matches.Add(new(outgoing.ToList(), incoming.ToList()));
            }
        }
        return matches.Count == 1 ? matches[0] : null;
    }

    private static List<InvestmentReturnCashFlow> MatchTerminalPayments(InvestmentReturnResult previous,
        IReadOnlyList<InvestmentReturnCashFlow> incoming, DateOnly? surrenderDate)
    {
        if (incoming.Count == 0 || surrenderDate is null) return [];
        var target = -incoming.Sum(x => x.Amount);
        var selected = new List<InvestmentReturnCashFlow>();
        // Match only a terminal sequence of payments, never an arbitrary subset of older withdrawals.
        foreach (var group in previous.CashFlows.Where(x => !x.IsClosingValue && x.Amount > 0)
            .GroupBy(x => x.Date).OrderByDescending(x => x.Key))
        {
            if (group.Key > surrenderDate || group.Key > incoming.Min(x => x.Date)) return [];
            if (previous.CashFlows.Any(x => x.Amount < 0 && x.Date >= group.Key)) return [];
            selected.AddRange(group);
            var paid = selected.Sum(x => x.Amount);
            if (paid == target) return selected;
            if (paid > target) return [];
        }
        return [];
    }

    private static InvestmentReturnResult BuildAccount(
        ClientInvestmentAccount account, IReadOnlyList<ClientInvestmentAccount> accounts,
        IReadOnlyList<ClientFundValuation> valuations,
        IReadOnlyList<ClientInvestmentReconciliationReview> latestReviews, string currency, DateOnly today)
    {
        var result = new InvestmentReturnResult { Currency = currency };
        var isZar = currency == "ZAR";
        var matched = ClientInvestmentStatusClassifier.MatchingValuations(account,
            valuations.Where(x => x.ClientId == account.ClientId)).ToList();
        var closingDate = account.SurrenderDate ?? matched.FirstOrDefault()?.ValuationDate;
        if (closingDate is null || closingDate > today)
            result.Issues.Add("A dated closing valuation or surrender is required; future dates are not used.");
        if (account.InvestmentDate is null)
            result.Issues.Add("The investment inception date is missing.");
        if (matched.Any(v => InvestmentSummaryCalculator.MatchingAccounts(v, accounts.Where(x => x.ClientId == account.ClientId))
            .Count(a => !a.SurrenderDate.HasValue || a.SurrenderDate > v.ValuationDate) != 1))
            result.Issues.Add("The closing valuation does not identify this account uniquely.");
        if (account.SurrenderDate.HasValue && matched.Count > 0)
            result.Issues.Add("A surrendered account still has current valuation lines.");
        if (!account.SurrenderDate.HasValue)
        {
            if (matched.Count == 0 || matched.Any(v => v.ValuationDate != closingDate ||
                (isZar ? v.AmountZar : v.AmountForeign) is null))
                result.Issues.Add("All closing fund lines must have amounts in this currency and the same valuation date.");
            else if (closingDate.HasValue)
            {
                var value = matched.Sum(v => (isZar ? v.AmountZar : v.AmountForeign)!.Value);
                if (value < 0) result.Issues.Add("The closing investment value cannot be negative.");
                result.CashFlows.Add(new(closingDate.Value, value, account.Id, null, "Closing valuation", IsClosingValue: true));
            }
        }
        foreach (var tx in account.Transactions.Where(x => !x.IsDeleted))
        {
            var contribution = isZar ? tx.InvestmentAmountZar : tx.InvestmentAmountForeign;
            var withdrawal = isZar ? tx.WithdrawalAmountZar : tx.WithdrawalAmountForeign;
            if (tx.TransactionDate > closingDate)
            {
                if (account.SurrenderDate.HasValue && ((contribution ?? 0) != 0 || (withdrawal ?? 0) != 0))
                    result.Issues.Add($"Transaction {tx.Id} records a movement after surrender; confirm the settlement history before calculating.");
                continue;
            }
            var otherAmount = isZar
                ? (tx.InvestmentAmountForeign ?? 0) != 0 || (tx.WithdrawalAmountForeign ?? 0) != 0
                : (tx.InvestmentAmountZar ?? 0) != 0 || (tx.WithdrawalAmountZar ?? 0) != 0;
            if ((contribution ?? 0) == 0 && (withdrawal ?? 0) == 0)
            {
                if (otherAmount) result.Issues.Add($"Transaction {tx.Id} is missing its {currency} amount; historical exchange rates are not assumed.");
                continue;
            }
            if (!tx.IsFinal || tx.TransactionDate is null)
            {
                result.Issues.Add($"Transaction {tx.Id} requires a final, dated cash-flow record.");
                continue;
            }
            if (!IsOneOff(tx.InvestmentFrequency))
            {
                result.Issues.Add($"Transaction {tx.Id}: {tx.InvestmentFrequency ?? "unspecified"} frequency needs actual payment dates and amounts; recurring instructions are not expanded.");
                continue;
            }
            if (contribution < 0 || withdrawal < 0)
            {
                result.Issues.Add($"Transaction {tx.Id} has a negative movement; confirm whether this is a reversal before calculating.");
                continue;
            }
            if (tx.TransactionDate < account.InvestmentDate)
                result.Issues.Add($"Transaction {tx.Id} precedes the recorded investment inception.");
            if (contribution > 0)
                result.CashFlows.Add(new(tx.TransactionDate.Value, -contribution.Value, account.Id, tx.Id, tx.Description ?? "Investment / top-up"));
            if (withdrawal > 0)
                result.CashFlows.Add(new(tx.TransactionDate.Value, withdrawal.Value, account.Id, tx.Id, tx.Description ?? "Withdrawal"));
        }
        if (!result.CashFlows.Any(x => x.Amount < 0 && x.Date == account.InvestmentDate))
            result.Issues.Add("The dated original investment amount is missing; a later balance is not treated as original capital.");
        if (account.SurrenderDate.HasValue)
        {
            var payouts = result.CashFlows.Where(x => x.Amount > 0 && !x.IsClosingValue).ToList();
            var review = latestReviews.SingleOrDefault(x => x.ClientInvestmentAccountId == account.Id);
            // Administrative closure can follow staged payments. Require reviewed closure, not a made-up payment on that date.
            var reviewedClosure = review is not null &&
                (review.Outcome is ClientInvestmentReconciliationOutcomes.HistoricalSurrendered or ClientInvestmentReconciliationOutcomes.Transferred) &&
                !string.IsNullOrWhiteSpace(review.EvidenceReference) &&
                review.AppliedSurrenderDate == account.SurrenderDate &&
                review.SnapshotSha256 == InvestmentReconciliationService.CalculateSnapshot(account, matched);
            if (payouts.Count == 0)
                result.Issues.Add("The final payout amount is missing; a surrender date alone cannot establish the return.");
            else if (!payouts.Any(x => x.Date == account.SurrenderDate) && !reviewedClosure)
                result.Issues.Add("Earlier withdrawals need a current evidenced surrender/transfer review confirming complete closure; they are not assumed to be the final payout.");
            if (payouts.Count > 0) closingDate = payouts.Max(x => x.Date);
        }
        result.StartDate = account.InvestmentDate;
        result.EndDate = closingDate;
        return result;
    }

    private static bool IsOneOff(string? frequency) =>
        frequency?.Trim().Equals("Once Off", StringComparison.OrdinalIgnoreCase) == true ||
        frequency?.Trim().Equals("Once-off", StringComparison.OrdinalIgnoreCase) == true ||
        frequency?.Trim().Equals("One-off", StringComparison.OrdinalIgnoreCase) == true;

    public static void Calculate(InvestmentReturnResult result)
    {
        result.AnnualisedPercent = null;
        result.Gain = null;
        var flows = result.CashFlows.Where(x => !x.IsInternalTransfer).GroupBy(x => x.Date)
            .Select(g => (Date: g.Key, Amount: g.Sum(x => x.Amount))).Where(x => x.Amount != 0)
            .OrderBy(x => x.Date).ToList();
        result.StartDate ??= flows.Count > 0 ? flows[0].Date : null;
        result.EndDate ??= result.CashFlows.Count > 0 ? result.CashFlows.Max(x => x.Date) : null;
        if (result.Issues.Count > 0) return;
        if (flows.Count > 0 && flows.All(x => x.Amount < 0) &&
            result.CashFlows.Any(x => x.IsClosingValue && x.Amount == 0 && x.Date > flows[^1].Date))
        {
            result.AnnualisedPercent = -100;
            result.Gain = flows.Sum(x => x.Amount);
            return;
        }
        if (flows.Count < 2 || flows[0].Amount >= 0 || !flows.Any(x => x.Amount > 0) ||
            result.EndDate <= result.StartDate)
        {
            result.Issues.Add("A return requires initial capital and subsequent proceeds/value on different dates.");
            return;
        }
        var values = flows.Select(x => (double)x.Amount).ToArray();
        var dates = flows.Select(x => x.Date.ToDateTime(TimeOnly.MinValue)).ToArray();
        // Reject ambiguous solutions rather than silently using whichever root a starting guess finds.
        var roots = new List<double>();
        foreach (var guess in new[] { -0.99, -0.9, -0.5, -0.1, 0.0, 0.1, 0.5, 1.0, 5.0, 10.0 })
        {
            try
            {
                var rate = Financial.XIrr(values, dates, guess);
                if (!double.IsFinite(rate) || rate <= -1 || rate > (double)decimal.MaxValue / 100) continue;
                var npv = values.Select((value, i) => value / Math.Pow(1 + rate,
                    (dates[i] - dates[0]).TotalDays / 365.0)).Sum();
                if (!double.IsFinite(npv) || Math.Abs(npv) > Math.Max(0.01, values.Sum(Math.Abs) * 1e-8)) continue;
                if (roots.All(x => Math.Abs(x - rate) > 1e-6 * Math.Max(1, Math.Abs(rate)))) roots.Add(rate);
            }
            // This library uses F# failwith (a plain System.Exception) for unsolved roots.
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException ||
                ex.GetType() == typeof(Exception)) { }
        }
        if (roots.Count != 1)
        {
            result.Issues.Add(roots.Count == 0 ? "No reliable annualised return could be solved for these cash flows." :
                "These cash flows produce multiple possible annualised returns; a single CAR would be misleading.");
            return;
        }
        result.AnnualisedPercent = (decimal)(roots[0] * 100);
        result.Gain = flows.Sum(x => x.Amount);
    }
}
