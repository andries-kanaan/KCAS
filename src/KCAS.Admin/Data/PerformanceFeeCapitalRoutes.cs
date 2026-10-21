using System.Security.Cryptography;
using System.Text;

namespace KCAS.Admin.Data;

public sealed record FeeCapitalLeg(string Account, string Wrap, DateOnly Date, decimal Amount,
    decimal? ValueBefore, string[] Keys, string Reference)
{
    public DateOnly? ReleasedOn { get; init; }
    public decimal? SettledAmount { get; init; }
}
public sealed record FeeCapitalRoute(string Key, List<FeeCapitalLeg> Sources, List<FeeCapitalLeg> Targets,
    string Reference);
public sealed record FeeCapitalTrace(string Account, string Wrap, DateOnly From, DateOnly To,
    decimal Amount, string Status, string Reference);
public sealed record FeeCapitalRouting(List<FeeCapitalRoute> Routes, List<FeeCapitalTrace> Traces);

// A route is a uniquely balanced statement path, not a guess based on family membership.
public static class FeeCapitalRoutes
{
    public static FeeCapitalRouting Build(IEnumerable<FeeStatementRow> source)
    {
        var rows = source.DistinctBy(x => x.Key).ToList();
        var traces = new List<FeeCapitalTrace>();
        var exits = new List<FeeCapitalLeg>();
        foreach (var account in rows.GroupBy(x => x.Account))
        {
            foreach (var outgoing in account.Where(x => Active(x.Wrap) && x.Kind == "InternalSwitch" && x.Amount < 0 &&
                x.Detail.Contains("Rebalance Out", StringComparison.OrdinalIgnoreCase)).GroupBy(x => (x.Wrap, x.Date)))
            {
                var amount = -outgoing.Sum(x => x.Amount);
                var credit = account.Where(x => x.Wrap == "CASH" && x.Date == outgoing.Key.Date && x.Amount > 0 &&
                    x.Detail.Contains("Rebalance Out", StringComparison.OrdinalIgnoreCase)).ToList();
                var returns = account.Where(x => x.Wrap == outgoing.Key.Wrap && x.Kind == "InternalSwitch" && x.Amount > 0 &&
                    Between(outgoing.Key.Date, x.Date, 7) && x.Detail.Contains("Rebalance In", StringComparison.OrdinalIgnoreCase)).GroupBy(x => x.Date).ToList();
                if (!Equal(credit.Sum(x => x.Amount), amount) || returns.Count != 1) continue;
                var incoming = returns[0];
                var debits = account.Where(x => x.Wrap == "CASH" && x.Date == incoming.Key && x.Amount < 0 &&
                    x.Detail.Contains("Rebalance In", StringComparison.OrdinalIgnoreCase)).ToList();
                var returned = incoming.Sum(x => x.Amount);
                if (!Equal(-debits.Sum(x => x.Amount), returned) || returned >= amount) continue;
                traces.Add(new(account.Key, outgoing.Key.Wrap, outgoing.Key.Date, incoming.Key, amount,
                    $"Same-wrap rebalance via cash: {returned:N2} reinvested; {amount - returned:N2} remains outside the wrap. Retained cash protection requires attribution; no balancing adjustment or HWM reset applied",
                    References(outgoing.Concat(credit).Concat(debits).Concat(incoming))));
            }
            var taxOut = account.Where(x => x.Amount < 0 && TaxSwitch(x.Detail) && Active(x.Wrap))
                .GroupBy(x => (x.Wrap, x.Date)).ToList();
            var staging = account.Where(x => x.Wrap == "ATXF" && x.Amount > 0 && TaxSwitch(x.Detail))
                .GroupBy(x => x.Date).ToList();
            foreach (var batch in taxOut)
            {
                var amount = -batch.Sum(x => x.Amount);
                var candidates = staging.Where(g => Between(batch.Key.Date, g.Key, 7) && Equal(g.Sum(x => x.Amount), amount)).ToList();
                if (candidates.Count != 1 || taxOut.Count(g => Between(g.Key.Date, candidates[0].Key, 7) && Equal(-g.Sum(x => x.Amount), amount)) != 1)
                {
                    traces.Add(new(account.Key, batch.Key.Wrap, batch.Key.Date, batch.Key.Date, amount,
                        "Tax/cash staging unresolved; no benchmark/HWM reset", References(batch)));
                    continue;
                }
                var deposit = candidates[0];
                var outflows = account.Where(x => x.Wrap == "ATXF" && x.Amount < 0 && Between(deposit.Key, x.Date, 7) && CashTransfer(x.Detail)).ToList();
                var intervening = outflows.Count == 1 ? account.Where(x => x.Wrap == "ATXF" &&
                    x.Date >= deposit.Key && x.Date <= outflows[0].Date && x.Key != outflows[0].Key &&
                    !deposit.Any(d => d.Key == x.Key) && x.Kind is not ("Opening" or "Closing") && x.Amount != 0).ToList() : [];
                var refs = References(batch.Concat(deposit));
                // Only explicit ordinary expenses/reinvested interest in an isolated staging lot.
                // Tax, performance fees and unexplained differences are not balancing adjustments.
                var isolated = deposit.Count() == 1 && Math.Abs(deposit.First().UnitBalance - deposit.First().Units) <= 0.0001m &&
                    outflows.Count == 1 && Math.Abs(outflows[0].UnitBalance) <= 0.0001m;
                var adjustmentsSupported = intervening.All(x => x.Kind == "Expense" && x.Amount < 0 ||
                    x.Kind == "Reinvestment" && x.Amount > 0 && x.Detail.Contains("interest", StringComparison.OrdinalIgnoreCase));
                if (outflows.Count != 1 || !isolated || !adjustmentsSupported ||
                    !Equal(amount + intervening.Sum(x => x.Amount), -outflows[0].Amount))
                {
                    traces.Add(new(account.Key, batch.Key.Wrap, batch.Key.Date, deposit.Key, amount,
                        "Wrap-to-tax/cash stage traced; subsequent tax, redemption or transfer requires reconciliation", refs));
                    continue;
                }
                var exit = outflows[0];
                exits.Add(new(account.Key, batch.Key.Wrap, batch.Key.Date, amount,
                    FeeStatementLedger.ValueBefore(account.Where(x => x.Wrap == batch.Key.Wrap).ToList(), batch.Key.Date),
                    batch.Concat(deposit).Concat(intervening).Append(exit).Select(x => x.Key).ToArray(),
                    References(batch.Concat(deposit).Concat(intervening).Append(exit))) { ReleasedOn = exit.Date, SettledAmount = -exit.Amount });
                traces.Add(new(account.Key, batch.Key.Wrap, batch.Key.Date, exit.Date, amount,
                    $"Wrap-to-tax/cash-to-account exit traced; settled {-exit.Amount:N2} after evidenced staging adjustments {intervening.Sum(x => x.Amount):N2}; receiving allocation not yet uniquely balanced",
                    References(batch.Concat(deposit).Concat(intervening).Append(exit))));
            }
        }
        var entries = rows.Where(x => x.Amount > 0 && x.Kind == "Transfer" && Active(x.Wrap) && CashTransfer(x.Detail))
            .GroupBy(x => (x.Account, x.Date)).Select(g => g.GroupBy(x => x.Wrap).Select(w =>
                new FeeCapitalLeg(g.Key.Account, w.Key, g.Key.Date, w.Sum(x => x.Amount), null,
                    w.Select(x => x.Key).ToArray(), References(w))).ToList()).ToList();
        var proposals = new List<FeeCapitalRoute>();
        // Bound subset work. Larger/ambiguous batches remain evidence gaps, not greedy allocations.
        foreach (var entry in entries)
        {
            var eligible = exits.Where(x => x.Account != entry[0].Account && Between(x.ReleasedOn ?? x.Date, entry[0].Date, 31)).ToList();
            if (eligible.Count > 12) continue;
            foreach (var subset in Subsets(eligible, 4))
                if (Equal(subset.Sum(x => x.SettledAmount ?? x.Amount), entry.Sum(x => x.Amount))) proposals.Add(Route(subset, entry));
        }
        foreach (var exit in exits)
        {
            var eligible = entries.Where(g => g[0].Account != exit.Account && Between(exit.ReleasedOn ?? exit.Date, g[0].Date, 31)).ToList();
            if (eligible.Count > 12) continue;
            foreach (var subset in Subsets(eligible, 4).Where(x => x.Count > 1))
                if (Equal(exit.SettledAmount ?? exit.Amount, subset.Sum(g => g.Sum(x => x.Amount)))) proposals.Add(Route([exit], subset.SelectMany(x => x).ToList()));
        }
        proposals = proposals.DistinctBy(x => x.Key).ToList();
        var routes = proposals.Where(p => !proposals.Any(q => q.Key != p.Key &&
            Keys(q).Intersect(Keys(p)).Any())).ToList();
        foreach (var route in routes)
            foreach (var leg in route.Sources)
            {
                var index = traces.FindIndex(x => x.Account == leg.Account && x.Wrap == leg.Wrap && x.From == leg.Date && x.Amount == leg.Amount);
                if (index >= 0) traces[index] = traces[index] with { Status = "Uniquely balanced staged route; source protection and mandate continuity still validated before calculation" };
            }
        foreach (var entry in entries.Where(g => !routes.Any(r => r.Targets.Any(t => t.Keys.Intersect(g.SelectMany(x => x.Keys)).Any()))))
            traces.Add(new(entry[0].Account, string.Join("/", entry.Select(x => x.Wrap)), entry[0].Date, entry[0].Date,
                entry.Sum(x => x.Amount), "Receiving allocation supplied; source/settlement reconciliation required (not proof of missing statement rows)",
                string.Join("; ", entry.Select(x => x.Reference))));
        return new(routes, traces);
    }

    private static IEnumerable<List<T>> Subsets<T>(List<T> values, int limit)
    {
        for (var mask = 1; mask < (1 << values.Count); mask++)
        {
            if (System.Numerics.BitOperations.PopCount((uint)mask) > limit) continue;
            yield return values.Where((_, i) => (mask & (1 << i)) != 0).ToList();
        }
    }
    private static FeeCapitalRoute Route(List<FeeCapitalLeg> sources, List<FeeCapitalLeg> targets)
    {
        var reference = string.Join("; ", sources.Concat(targets).Select(x => x.Reference));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", sources.Concat(targets).SelectMany(x => x.Keys).Order()))));
        return new(key, sources, targets, reference);
    }
    private static IEnumerable<string> Keys(FeeCapitalRoute route) => route.Sources.Concat(route.Targets).SelectMany(x => x.Keys);
    private static bool Between(DateOnly start, DateOnly end, int days) => end >= start && end.DayNumber - start.DayNumber <= days;
    private static bool Equal(decimal a, decimal b) => Math.Abs(a - b) <= 0.02m;
    private static string References(IEnumerable<FeeStatementRow> rows) => string.Join("; ", rows.Select(x => x.Source).Distinct());
    private static bool TaxSwitch(string text) => text.Contains("Tax Switch", StringComparison.OrdinalIgnoreCase);
    private static bool CashTransfer(string text) => text.Contains("Transfer Rands", StringComparison.OrdinalIgnoreCase) || text.Contains("Oorplasing Rand", StringComparison.OrdinalIgnoreCase);
    private static bool Active(string code) => code is "KE1" or "KA6" or "KA9" or "KP1";
}
