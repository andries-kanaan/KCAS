namespace KCAS.Admin.Data;

public sealed record FeeStatementScopeEntry(string Wrap, DateOnly Date, decimal Amount, string[] IncomingKeys,
    string[] SourceKeys, string Reference);

internal static class FeeStatementScopeEntries
{
    internal static List<FeeStatementScopeEntry> Find(FeeStatementLedger ledger, DateOnly statementFrom)
    {
        var found = new List<FeeStatementScopeEntry>();
        foreach (var wrap in ledger.Wraps.Where(x => Active(x.Code)))
        {
            var funding = wrap.Rows.Where(x => x.Amount > 0 && x.Kind is "Contribution" or "InternalSwitch").OrderBy(x => x.Date).ToList();
            if (funding.Count == 0) continue;
            var date = funding[0].Date;
            if (wrap.Issues.Count > 0 || date <= statementFrom || wrap.Rows.Any(x => x.Date < date && (x.Amount != 0 || x.UnitBalance != 0))) continue;
            if (wrap.Rows.GroupBy(x => x.Holding).Any(g => !g.Any(x => x.Kind == "Opening" && x.Date == statementFrom && x.Amount == 0 && x.UnitBalance == 0))) continue;
            var initial = wrap.Rows.Where(x => x.Date == date && x.Kind is not ("Opening" or "Closing")).ToList();
            if (initial.Any(x => x.Amount <= 0 || x.Kind is not ("Contribution" or "InternalSwitch") || Math.Abs(x.UnitBalance - x.Units) > 0.0003m)) continue;
            var sources = new List<FeeStatementRow>();
            if (initial.Any(x => x.Kind == "InternalSwitch"))
            {
                if (initial.Any(x => x.Kind != "InternalSwitch")) continue;
                var candidates = ledger.Rows.Where(x => x.Account == initial[0].Account && x.Kind == "InternalSwitch" && x.Amount < 0 &&
                    x.Date <= date && date.DayNumber - x.Date.DayNumber <= 7).GroupBy(x => x.Date)
                    .Where(g => g.All(x => !Active(x.Wrap) && x.UnitBalance == 0) &&
                        Math.Abs(g.Sum(x => x.Amount) + initial.Sum(x => x.Amount)) <= 0.02m).ToList();
                if (candidates.Count != 1) continue;
                sources = candidates[0].ToList();
                // A single outgoing row set must not fund more than one independent wrap start.
                if (ledger.Rows.Any(x => Active(x.Wrap) && x.Wrap != wrap.Code && x.Kind == "InternalSwitch" &&
                    x.Amount > 0 && x.Date >= sources[0].Date && x.Date <= date)) continue;
            }
            found.Add(new(wrap.Code, date, initial.Sum(x => x.Amount), initial.Select(x => x.Key).ToArray(),
                sources.Select(x => x.Key).ToArray(), string.Join("; ", sources.Concat(initial).Select(x => x.Source).Distinct())));
        }
        return found;
    }

    internal static bool Covers(FeeStatementLedger ledger, List<FeeStatementScopeEntry> entries) =>
        ledger.Wraps.Where(x => Active(x.Code) && x.Rows.Any(r => r.Amount != 0 || r.UnitBalance != 0))
            .All(x => entries.Any(e => e.Wrap == x.Code)) && entries.Count > 0;

    private static bool Active(string code) => code is "KE1" or "KA6" or "KA9" or "KP1";
}
