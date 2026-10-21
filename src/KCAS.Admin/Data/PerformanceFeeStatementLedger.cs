namespace KCAS.Admin.Data;

public sealed record FeeStatementReview(string Kind, string Reason, string ReviewedBy, DateTime ReviewedAtUtc);
public sealed class FeeStatementLedger
{
    public List<FeeStatementRow> Rows { get; set; } = [];
    public List<string> Issues { get; set; } = [];
    public int DuplicateRows { get; set; }
    public List<FeeStatementWrap> Wraps { get; set; } = [];
    public FeeStatementHistory History { get; set; } = new();

    public static FeeStatementLedger Merge(IEnumerable<FeeStatementDocument> documents, string account, Dictionary<string, FeeStatementReview>? reviews = null)
    {
        var ledger = new FeeStatementLedger(); var docs = documents.ToList();
        var ranges = docs.SelectMany(d =>
        {
            var summaries = d.Periods.Where(x => x.Account.Equals(account, StringComparison.OrdinalIgnoreCase) && x.To != null).ToList();
            var rows = d.Rows.Where(x => x.Account.Equals(account, StringComparison.OrdinalIgnoreCase)).ToList();
            return summaries.Count > 0 ? summaries.Select(x => (From: x.From, To: x.To!.Value)) :
                rows.Count > 0 ? [(From: rows.Min(r => r.Date), To: rows.Max(r => r.Date))] : Enumerable.Empty<(DateOnly From, DateOnly To)>();
        }).OrderBy(x => x.From).ToList();
        if (ranges.Count > 0)
        {
            var covered = ranges[0].To;
            foreach (var range in ranges.Skip(1))
            {
                if (range.From.DayNumber - covered.DayNumber > 1)
                    ledger.Issues.Add($"Statement coverage gap: {covered.AddDays(1):yyyy-MM-dd} to {range.From.AddDays(-1):yyyy-MM-dd}. Obtain the missing period; unchanged units alone do not prove no cash flows.");
                if (range.To > covered) covered = range.To;
            }
        }
        var all = docs.SelectMany(x => x.Rows).Where(x => x.Account.Equals(account, StringComparison.OrdinalIgnoreCase)).ToList();
        ledger.DuplicateRows = all.Count - all.Select(x => x.Key).Distinct().Count();
        ledger.Rows = all.DistinctBy(x => x.Key).Select(x => FeeJson.Read<FeeStatementRow>(FeeJson.Write(x)))
            .OrderBy(x => x.Date).ThenBy(x => x.Holding, StringComparer.Ordinal).ThenBy(x => x.SourceRow).ToList();
        foreach (var row in ledger.Rows)
            if (reviews?.TryGetValue(row.Key, out var review) == true) row.Kind = review.Kind;
            else if (row.Kind == "Unknown") row.Kind = FeeStatementKinds.Classify(row.Detail);
        FeeStatementAutomation.Normalise(ledger.Rows);
        ledger.Issues.AddRange(docs.Where(x => x.Rows.Any(r => r.Account.Equals(account, StringComparison.OrdinalIgnoreCase))).SelectMany(x => x.Issues).Distinct());
        foreach (var conflict in ledger.Rows.GroupBy(x => new { x.Holding, x.Date, x.Detail, x.Units, x.UnitBalance }).Where(g => g.Select(x => (x.Amount, x.Price)).Distinct().Count() > 1))
            ledger.Issues.Add($"Conflicting source amounts/prices: {conflict.First().Holding}, {conflict.First().Date:yyyy-MM-dd}.");
        foreach (var group in ledger.Rows.GroupBy(x => x.Wrap))
        {
            var wrap = new FeeStatementWrap { Code = group.Key, Rows = group.ToList() };
            foreach (var holding in group.GroupBy(x => x.Holding))
            {
                decimal? units = null;
                foreach (var row in holding.OrderBy(x => x.Date).ThenBy(x => x.Kind == "Opening" ? 0 : x.Kind == "Closing" ? 2 : 1).ThenBy(x => x.SourceRow))
                {
                    if (row.Kind == "Opening")
                    {
                        if (units is { } previous && Math.Abs(previous - row.UnitBalance) > 0.0003m)
                            wrap.Issues.Add($"Unit continuity gap for {holding.Key} at {row.Date:yyyy-MM-dd}.");
                        units = row.UnitBalance;
                    }
                    else if (row.Kind == "Closing")
                    {
                        if (units is { } previous && Math.Abs(previous - row.UnitBalance) > 0.0003m)
                            wrap.Issues.Add($"Closing units do not reconcile for {holding.Key} at {row.Date:yyyy-MM-dd}.");
                    }
                    else
                    {
                        units ??= row.UnitBalance - row.Units;
                        if (Math.Abs(units.Value + row.Units - row.UnitBalance) > 0.0003m)
                            wrap.Issues.Add($"Transaction units do not reconcile at {row.Source}.");
                        units = row.UnitBalance;
                    }
                }
                var closing = holding.Where(x => x.Kind == "Closing").OrderByDescending(x => x.Date).FirstOrDefault();
                if (closing is null) wrap.Issues.Add($"Missing closing snapshot for {holding.Key}.");
                else
                {
                    wrap.ClosingValue += closing.Amount;
                    var cutoff = ranges.Count > 0 ? ranges.Max(x => x.To) : ledger.Rows.Max(x => x.Date);
                    // A settled zero-unit holding may disappear from a later statement; it is not an undated live position.
                    if (closing.Amount != 0 || closing.UnitBalance != 0 || closing.Date == cutoff) wrap.ClosingDates.Add(closing.Date);
                    if (closing.Amount != 0 && closing.PriceDate is { } priced && priced != closing.Date)
                        wrap.Issues.Add($"{holding.Key}: nonzero closing holding uses price {priced:yyyy-MM-dd}, not cutoff {closing.Date:yyyy-MM-dd}.");
                }
            }
            wrap.Issues = wrap.Issues.Distinct().ToList();
            if (wrap.ClosingDates.Distinct().Count() > 1) wrap.Issues.Add("Closing holdings do not share a common statement cutoff.");
            wrap.ClosingDate = wrap.ClosingDates.Count > 0 ? wrap.ClosingDates.Max() :
                wrap.Rows.All(x => x.Kind != "Closing" || x.Amount == 0 && x.UnitBalance == 0) && ranges.Count > 0 ? ranges.Max(x => x.To) : null;
            ledger.Wraps.Add(wrap);
        }
        ledger.History = FeeStatementHistory.Build(docs, account, ledger);
        return ledger;
    }

    public FeePreparation Prepare(FeePreparation existing, FeeCheckpoint? checkpoint)
    {
        var input = FeeJson.Read<FeePreparation>(FeeJson.Write(existing));
        input.RequiresRebuild = false;
        var active = Wraps.Where(x => x.Code is "KE1" or "KA6" or "KA9" or "KP1").ToList();
        if (active.Count == 0) throw new System.ComponentModel.DataAnnotations.ValidationException("No supported SA wraps were identified for this account.");
        input.OtherScopes = Wraps.Except(active).Select(x => new FeeScopeNote(x.Code, Name(x.Code), x.ClosingValue,
            x.Code == "CASH" ? "Cash retained for reconciliation; not a separate wrap PDF claim." : "Outside the supported SA wrap calculation; retained for scope review, not assumed to have a 0% mandate.")).ToList();
        input.To = active.All(x => x.ClosingDate is not null) ? active.Min(x => x.ClosingDate) : null;
        var earliest = Rows.Min(x => x.Date);
        var scopeBeginning = !History.BeginningCovered && History.FeeScopeBeginningCovered;
        input.StatementHistory = FeeJson.Read<FeeStatementHistory>(FeeJson.Write(History));
        if (checkpoint is null && History.Periods.Count > 0)
            input.Inception = History.BeginningCovered ? History.FirstFundingDate :
                scopeBeginning ? History.FeeScopeEntries.Min(x => x.Date) : null;
        input.From = checkpoint?.Date ?? (input.Inception >= earliest ? input.Inception : earliest);
        input.WrapFund = "Separately protected SA wraps";
        input.EvidenceIssues = [.. Issues]; input.Transfers.Clear(); input.Wraps = [];
        input.EvidenceIssues.AddRange(History.Issues.Where(x => checkpoint is null || !x.StartsWith("Earlier AIMS/Glacier", StringComparison.Ordinal)));
        foreach (var entry in checkpoint is null && scopeBeginning ? History.FeeScopeEntries.Where(x => x.SourceKeys.Length > 0) : [])
        {
            var key = "fee-scope-entry-" + entry.Wrap + "-" + entry.Date.ToString("yyyyMMdd");
            var detail = $"{entry.Wrap} first allocation {entry.Date:yyyy-MM-dd}, {entry.Amount:N2}, starts from verified zero wrap units and is balanced to a fully exited holding outside the supported SA-wrap scope. Confirm the applicable mandate and the separate fund fee-period basis before claiming; no prior fund's fee/HWM is assumed settled or cancelled. {entry.Reference}";
            var point = input.ReviewPoints.SingleOrDefault(x => x.Key == key);
            if (point is null) input.ReviewPoints.Add(new() { Key = key, Detail = detail });
            else if (point.Detail != detail) { point.Detail = detail; point.ResolvedBy = null; point.ResolvedAtUtc = null; point.ResolutionReference = ""; }
        }
        var paired = new HashSet<(DateOnly Date, string Wrap)>();
        foreach (var dateGroup in Rows.Where(x => x.Kind == "InternalSwitch" && x.Date > input.From && x.Date <= input.To).GroupBy(x => x.Date))
        {
            var nets = dateGroup.GroupBy(x => x.Wrap).Select(x => new { Code = x.Key, Amount = x.Sum(r => r.Amount), Rows = x.ToList() }).Where(x => Math.Abs(x.Amount) > 0.02m).ToList();
            if (nets.Count == 2 && active.Any(x => x.Code == nets[0].Code) && active.Any(x => x.Code == nets[1].Code) && Math.Abs(nets.Sum(x => x.Amount)) <= 0.02m)
            {
                var source = nets.Single(x => x.Amount < 0); var target = nets.Single(x => x.Amount > 0);
                input.Transfers.Add(new() { Date = dateGroup.Key, FromWrap = source.Code, ToWrap = target.Code, Amount = -source.Amount, ContinuityConfirmed = true,
                    ValueBeforeTransfer = ValueBefore(active.Single(x => x.Code == source.Code).Rows, dateGroup.Key),
                    Reference = string.Join("; ", source.Rows.Concat(target.Rows).Select(x => x.Source)) });
                paired.Add((dateGroup.Key, source.Code)); paired.Add((dateGroup.Key, target.Code));
            }
        }
        foreach (var wrap in active)
        {
            var child = existing.Wraps.SingleOrDefault(x => x.Code == wrap.Code)?.Input;
            var existingWrap = child;
            child = FeeJson.Read<FeePreparation>(FeeJson.Write(child ?? existing));
            if (wrap.Code == "KP1" && existingWrap is null && !(active.Count == 1 && existing.FeePercent == 0))
            { child.FeePercent = null; child.ZeroRateConfirmed = false; child.MethodMatchesMandate = false; }
            child.Wraps.Clear(); child.Transfers.Clear(); child.Movements.Clear(); child.EvidenceIssues = [.. wrap.Issues];
            child.RequiresRebuild = false; child.AutomationNotes.Clear();
            child.StatementHistory = null; child.OpeningTransfers.Clear();
            child.ReviewPoints.Clear();
            child.ReviewIssues.RemoveAll(x => x.StartsWith("Carried protection from ", StringComparison.Ordinal));
            child.ReviewIssues.RemoveAll(x => x.StartsWith("The recorded source mandate no longer covers", StringComparison.Ordinal));
            child.Inception = input.Inception; child.From = input.From; child.To = input.To;
            child.WrapFund = Name(wrap.Code); child.IsSouthAfricanWrap = true;
            child.ClosingValue = wrap.ClosingDate == input.To ? wrap.ClosingValue : null;
            child.ClosingReference = string.Join("; ", wrap.Rows.Where(x => x.Kind == "Closing" && x.Date == input.To).Select(x => x.Source).Distinct());
            child.CashFlowReference = "Preserved administrator transaction rows; internal income and expenses are not external client flows.";
            child.EvidenceIssues.AddRange(input.EvidenceIssues);
            foreach (var invalid in wrap.Rows.Where(x => x.Kind == "FundAllocation" && x.Amount <= 0 && x.Date >= input.From && x.Date <= input.To))
                child.EvidenceIssues.Add($"{invalid.Date:yyyy-MM-dd}: a new fund allocation must be a positive evidenced receipt, not an outgoing or zero row; {invalid.Source}.");
            var cp = checkpoint?.Wraps.SingleOrDefault(x => x.Code == wrap.Code)?.Checkpoint;
            if (cp is not null)
            {
                child.OpeningValue = cp.Value; child.OpeningBenchmark = cp.Benchmark; child.OpeningHighWaterMark = cp.HighWaterMark;
                child.OpeningReference = cp.Reference; child.PriorFeeReviewReference = cp.Reference; child.NoPriorFeeConfirmed = false;
            }
            else
            {
                var snapshots = wrap.Rows.Where(x => x.Kind == "Opening" && x.Date == input.From).ToList();
                var scopeEntry = scopeBeginning ? History.FeeScopeEntries.SingleOrDefault(x => x.Wrap == wrap.Code) : null;
                var starts = wrap.Rows.Where(x => x.Date == input.From && (x.Kind is "Contribution" or "FundAllocation" || scopeEntry?.IncomingKeys.Contains(x.Key) == true)).ToList();
                child.OpeningValue = snapshots.Count > 0 ? snapshots.Sum(x => x.Amount) + starts.Sum(x => x.Amount) : starts.Count > 0 ? starts.Sum(x => x.Amount) :
                    (History.BeginningCovered && input.From == History.FirstFundingDate || scopeBeginning && scopeEntry?.Date >= input.From) ? 0 : null;
                child.OpeningReference = string.Join("; ", snapshots.Concat(starts).Select(x => x.Source));
                if (child.From == child.Inception && child.OpeningValue is { } value)
                { child.OpeningBenchmark = value; child.OpeningHighWaterMark = value; }
                else if (existing.Wraps.All(x => x.Code != wrap.Code)) { child.OpeningBenchmark = null; child.OpeningHighWaterMark = null; }
                foreach (var transfer in wrap.Rows.Where(x => x.Date == input.From && x.Kind == "Transfer"))
                    child.EvidenceIssues.Add($"{transfer.Date:yyyy-MM-dd} Transfer: review {transfer.Detail} ({transfer.Amount:N2}) at {transfer.Source}.");
            }
            foreach (var day in wrap.Rows.Where(x => x.Date > input.From && x.Date <= input.To && x.Kind is not ("Opening" or "Closing")).GroupBy(x => x.Date))
            {
                var scopeEntry = scopeBeginning ? History.FeeScopeEntries.SingleOrDefault(x => x.Wrap == wrap.Code && x.Date == day.Key) : null;
                if (scopeEntry is not null)
                    child.Movements.Add(new() { Date = day.Key, Kind = "ScopeFunding", Amount = scopeEntry.Amount, Reference = "First verified wrap allocation, not a new external client payment: " + scopeEntry.Reference });
                foreach (var bad in day.Where(x => x.Kind is "Unknown" or "Transfer" or "PerformanceFee"))
                    child.EvidenceIssues.Add($"{bad.Date:yyyy-MM-dd} {bad.Kind}: review {bad.Detail} ({bad.Amount:N2}) at {bad.Source}.");
                var cash = day.Where(x => x.Kind is "ScopeFunding" or "FundAllocation" && x.Amount > 0).ToList();
                if (cash.Count > 0)
                {
                    child.Movements.Add(new() { Date = day.Key, Kind = "ScopeFunding", Amount = cash.Sum(x => x.Amount), Reference = "Evidenced fund allocation at actual receiving value; not necessarily an external client contribution: " + string.Join("; ", cash.Select(x => x.Source)) });
                    child.AutomationNotes.Add($"{day.Key:yyyy-MM-dd}: R{cash.Sum(x => x.Amount):N2} cash allocation protected at its actual market value. Cash-scope earnings are not charged as this wrap's performance.");
                }
                if (day.Any(x => x.Kind == "InternalRebalance")) child.AutomationNotes.Add($"{day.Key:yyyy-MM-dd}: uniquely matched same-wrap rebalance across settlement dates; no new client capital or HWM reset.");
                var switches = day.Where(x => x.Kind == "InternalSwitch" && scopeEntry?.IncomingKeys.Contains(x.Key) != true).ToList();
                if (Math.Abs(switches.Sum(x => x.Amount)) > 0.02m && !paired.Contains((day.Key, wrap.Code)))
                    child.EvidenceIssues.Add($"{day.Key:yyyy-MM-dd}: unmatched switch crosses this wrap or settlement date. Verify transferred benchmark/HWM continuity; do not treat it as a fresh contribution.");
                foreach (var kind in new[] { "Contribution", "Withdrawal", "ScopeWithdrawal" })
                {
                    var moves = day.Where(x => x.Kind == kind && scopeEntry?.IncomingKeys.Contains(x.Key) != true).ToList();
                    if (moves.Count == 0) continue;
                    if (moves.Any(x => kind == "Contribution" ? x.Amount <= 0 : x.Amount >= 0))
                    { child.EvidenceIssues.Add($"{day.Key:yyyy-MM-dd}: {kind} includes a reversal or unexpected sign requiring review."); continue; }
                    var amount = Math.Abs(moves.Sum(x => x.Amount));
                    var withdrawal = kind != "Contribution";
                    var before = withdrawal ? ValueBefore(wrap.Rows, day.Key) : null;
                    if (withdrawal && day.Any(x => x.Kind is not ("Withdrawal" or "ScopeWithdrawal" or "InternalSwitch"))) before = null;
                    if (withdrawal && FullyExited(wrap.Rows, day.Key) && day.All(x => x.Amount <= 0)) before = amount;
                    child.Movements.Add(new() { Date = day.Key, Kind = withdrawal ? "Withdrawal" : kind, Amount = amount, ValueBeforeWithdrawal = before,
                        Reference = string.Join("; ", moves.Select(x => x.Source)) });
                }
            }
            foreach (var rebalance in FeeStatementAutomation.CashRebalances(Rows).Where(x => x.Rows[0].Wrap == wrap.Code && x.Date > child.From && x.Date <= child.To && x.Residual > 0))
            {
                var before = ValueBefore(wrap.Rows, rebalance.Date);
                if (FullyExited(wrap.Rows, rebalance.Date)) before = rebalance.Outgoing;
                if (before is null)
                    child.AutomationNotes.Add($"{rebalance.Date:yyyy-MM-dd}: same-wrap rebalance leaves R{rebalance.Residual:N2} outside the wrap. Exact full-wrap pre-exit prices are unavailable: conservatively retain all benchmark/HWM protection rather than invent a valuation or increase the fee. Actual closing value excludes that cash; this can only reduce the fee. Sources: {string.Join("; ", rebalance.Rows.Select(x => x.Source))}");
                else
                {
                    child.Movements.Add(new() { Date = rebalance.Date, Kind = "Withdrawal", Amount = rebalance.Residual, ValueBeforeWithdrawal = before,
                        Reference = "Same-wrap cash rebalance: only the amount not returned leaves this fee scope. " + string.Join("; ", rebalance.Rows.Select(x => x.Source)) });
                    child.AutomationNotes.Add($"{rebalance.Date:yyyy-MM-dd}: same-wrap rebalance preserves benchmark/HWM on returned capital; R{rebalance.Residual:N2} retained outside the wrap reduces both proportionally.");
                }
            }
            // A historical boundary is not an inception or a verified HWM checkpoint.
            if (checkpoint is null && child.From != child.Inception && child.OpeningBenchmark is null)
                child.EvidenceIssues.Add("History starts after inception or AIMS/Glacier entry is not established: supply earlier AIMS/Glacier statements or an evidenced benchmark/HWM checkpoint, not an opening market-value reset. No earlier-administrator history is requested.");
            child.CashFlowsComplete = child.EvidenceIssues.Count == 0 && child.Movements.All(x => x.Kind != "Withdrawal" || x.ValueBeforeWithdrawal is not null);
            FeeStatementAutomation.RecordPriorFeeReview(this, wrap, child);
            input.Wraps.Add(new() { Code = wrap.Code, Input = child });
        }
        return input;
    }

    internal static decimal? ValueBefore(List<FeeStatementRow> rows, DateOnly date)
    {
        decimal total = 0;
        foreach (var holding in rows.GroupBy(x => x.Holding))
        {
            var prior = holding.Where(x => x.Date < date).OrderBy(x => x.Date).ThenBy(x => x.SourceRow).LastOrDefault();
            var units = prior?.UnitBalance ?? 0;
            if (units == 0) continue;
            var prices = holding.Where(x => x.Date == date && x.Price > 0 && (x.PriceDate is null || x.PriceDate == date)).Select(x => x.Price).Distinct().ToList();
            if (prices.Count != 1) return null;
            total += units * prices[0];
        }
        return total > 0 ? decimal.Round(total, 2, MidpointRounding.AwayFromZero) : null;
    }

    internal static bool FullyExited(List<FeeStatementRow> rows, DateOnly date) => rows.Where(x => x.Date <= date)
        .GroupBy(x => x.Holding).All(g => g.OrderBy(x => x.Date).ThenBy(x => x.SourceRow).Last().UnitBalance == 0);

    public static string Name(string code) => code switch { "KE1" => "Kanaan Equity SA Wrap", "KA6" => "Kanaan Global SA Wrap", "KA9" => "Kanaan Compulsory SA Wrap", "KP1" => "Kanaan Stable SA Wrap", _ => code };
}

public sealed class FeeStatementWrap
{
    public string Code { get; set; } = "";
    public List<FeeStatementRow> Rows { get; set; } = [];
    public decimal ClosingValue { get; set; }
    public DateOnly? ClosingDate { get; set; }
    public List<DateOnly> ClosingDates { get; set; } = [];
    public List<string> Issues { get; set; } = [];
}
