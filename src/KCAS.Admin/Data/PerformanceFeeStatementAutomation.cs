namespace KCAS.Admin.Data;

internal static class FeeStatementAutomation
{
    internal static void Normalise(List<FeeStatementRow> rows)
    {
        // A verified exit to tax/cash ends this fund scope. Its eventual destination does
        // not determine the old fund's withdrawal or the new fund's capital basis.
        foreach (var exit in rows.Where(x => Active(x.Wrap) && x.Kind == "InternalSwitch" && x.Amount < 0 &&
                     x.Detail.Contains("Tax Switch", StringComparison.OrdinalIgnoreCase)).GroupBy(x => (x.Wrap, x.Date)))
        {
            var matches = rows.Where(x => x.Wrap == "ATXF" && x.Amount > 0 &&
                x.Detail.Contains("Tax Switch", StringComparison.OrdinalIgnoreCase) && x.Date >= exit.Key.Date &&
                x.Date.DayNumber - exit.Key.Date.DayNumber <= 7).GroupBy(x => x.Date)
                .Where(g => Math.Abs(g.Sum(x => x.Amount) + exit.Sum(x => x.Amount)) <= 0.02m).ToList();
            if (matches.Count == 1) foreach (var row in exit) row.Kind = "ScopeWithdrawal";
        }
        foreach (var sweep in rows.Where(x => x.Kind == "Transfer" &&
            (x.Detail.Contains("Cash Sweep", StringComparison.OrdinalIgnoreCase) || x.Detail.Contains("Kontantaansuiwering", StringComparison.OrdinalIgnoreCase))).GroupBy(x => x.Date))
        {
            // Cash is outside the wrap fee scope. Its matched market-value allocation is protected
            // capital in the receiving wrap, not new client money or fee-bearing wrap performance.
            if (!sweep.Any(x => x.Wrap == "CASH" && x.Amount < 0) ||
                sweep.Any(x => x.Wrap == "CASH" ? x.Amount > 0 : x.Amount < 0) || Math.Abs(sweep.Sum(x => x.Amount)) > 0.02m) continue;
            foreach (var row in sweep) row.Kind = row.Wrap == "CASH" ? "IncomeTransfer" : "ScopeFunding";
        }
        var legs = rows.Where(x => x.Kind == "InternalSwitch").GroupBy(x => (x.Wrap, x.Date))
            .Select(g => new { g.Key.Wrap, g.Key.Date, Rows = g.ToList(), Net = g.Sum(x => x.Amount) }).Where(x => Math.Abs(x.Net) > 0.02m).ToList();
        foreach (var outgoing in legs.Where(x => x.Net < 0))
        {
            var incoming = legs.Where(x => x.Wrap == outgoing.Wrap && x.Net > 0 && x.Date >= outgoing.Date && x.Date.DayNumber - outgoing.Date.DayNumber <= 7 && Math.Abs(x.Net + outgoing.Net) <= 0.02m).ToList();
            if (incoming.Count != 1) continue;
            var match = incoming[0];
            if (legs.Count(x => x.Wrap == match.Wrap && x.Net < 0 && x.Date <= match.Date && match.Date.DayNumber - x.Date.DayNumber <= 7 && Math.Abs(x.Net + match.Net) <= 0.02m) != 1) continue;
            foreach (var row in outgoing.Rows.Concat(match.Rows)) row.Kind = "InternalRebalance";
        }
        // A partial cash return is not a fresh contribution. The retained part keeps
        // its protection; only the evidenced amount left outside the wrap is removed.
        foreach (var rebalance in CashRebalances(rows))
            foreach (var row in rebalance.Rows) row.Kind = "InternalRebalance";
    }

    internal sealed record CashRebalance(DateOnly Date, decimal Residual, decimal Outgoing, List<FeeStatementRow> Rows);
    internal static List<CashRebalance> CashRebalances(List<FeeStatementRow> rows)
    {
        var results = new List<CashRebalance>();
        var legs = rows.Where(x => Active(x.Wrap) && x.Kind is "InternalSwitch" or "InternalRebalance")
            .GroupBy(x => (x.Wrap, x.Date)).Select(g => new { g.Key, Rows = g.ToList(), Amount = g.Sum(x => x.Amount) }).ToList();
        foreach (var exit in legs.Where(x => x.Amount < 0 && x.Rows.All(r => r.Detail.Contains("Rebalance Out", StringComparison.OrdinalIgnoreCase))))
        {
            var returns = legs.Where(x => x.Key.Wrap == exit.Key.Wrap && x.Amount > 0 && x.Amount <= -exit.Amount &&
                x.Key.Date > exit.Key.Date && x.Key.Date.DayNumber - exit.Key.Date.DayNumber <= 7 &&
                x.Rows.All(r => r.Detail.Contains("Rebalance In", StringComparison.OrdinalIgnoreCase))).ToList();
            if (returns.Count != 1) continue;
            var incoming = returns[0];
            if (legs.Count(x => x.Key.Wrap == incoming.Key.Wrap && x.Amount < 0 && x.Key.Date < incoming.Key.Date &&
                incoming.Key.Date.DayNumber - x.Key.Date.DayNumber <= 7) != 1) continue;
            bool CashMatches(DateOnly date, string label, decimal amount) => Math.Abs(rows.Where(x => x.Wrap == "CASH" &&
                x.Date == date && x.Detail.Contains(label, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount) - amount) <= 0.02m;
            if (!CashMatches(exit.Key.Date, "Rebalance Out", -exit.Amount) || !CashMatches(incoming.Key.Date, "Rebalance In", -incoming.Amount)) continue;
            results.Add(new(exit.Key.Date, -exit.Amount - incoming.Amount, -exit.Amount, exit.Rows.Concat(incoming.Rows).ToList()));
        }
        return results;
    }

    private static bool Active(string code) => code is "KE1" or "KA6" or "KA9" or "KP1";

    internal static void RecordPriorFeeReview(FeeStatementLedger ledger, FeeStatementWrap wrap, FeePreparation child)
    {
        if (child.NoPriorFeeConfirmed || child.From != child.Inception || child.OpeningValue is null || ledger.Issues.Count > 0 || ledger.History.Issues.Count > 0 || wrap.Issues.Count > 0) return;
        if (wrap.Rows.Any(x => x.Kind is "Unknown" or "PerformanceFee") || wrap.Rows.Any(x => x.Kind == "Opening" && x.Date <= child.From && x.UnitBalance != 0) ||
            wrap.Rows.Any(x => x.Date < child.From && x.Amount != 0) ||
            !(wrap.Rows.Any(x => x.Date == child.From && x.Kind is "Contribution" or "FundAllocation") ||
                ledger.History.FeeScopeBeginningCovered && ledger.History.FeeScopeEntries.Any(x => x.Wrap == wrap.Code && x.Date == child.From))) return;
        child.NoPriorFeeConfirmed = true;
        child.PriorFeeReviewReference = $"KCAS reviewed the preserved detailed ledger from zero units through initial subscription {child.From:yyyy-MM-dd} to {child.To:yyyy-MM-dd}. Unit continuity reconciles; no performance-fee description or unknown transaction was found. Ordinary labelled administration/advice/wrap fees remain expenses. Source: {child.ClosingReference}";
        child.AutomationNotes.Add("Prior-fee review derived from complete reconciled inception history, not absence of documents or recurring instructions.");
    }
}
