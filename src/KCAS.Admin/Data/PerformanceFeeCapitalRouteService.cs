namespace KCAS.Admin.Data;

public sealed partial class PerformanceFeeService
{
    internal static void ApplyCapitalRoutes(FeeClientWorkspace workspace, Dictionary<string, FeePreparation> inputs,
        string[] confirmed, string reason, ApplicationUser actor, bool mandateRequiresCrossWrapContinuity = false)
    {
        foreach (var route in workspace.CapitalRouting.Routes.OrderBy(x => x.Sources.Min(s => s.Date)).ThenBy(x => x.Key))
        {
            if (!route.Targets.Any(x => inputs.ContainsKey(x.Account))) continue;
            // Kanaan's applicable-fund basis does not move one fund's hurdle into
            // another fund. A bespoke continuity mandate is a separate explicit basis.
            if (!mandateRequiresCrossWrapContinuity && route.Targets.Any(t => route.Sources.Any(s => s.Wrap != t.Wrap)))
            {
                foreach (var leg in route.Targets)
                    if (inputs.TryGetValue(leg.Account, out var root))
                        root.CapitalTraces.Add(new(leg.Account, leg.Wrap, route.Sources.Min(x => x.Date), leg.Date, leg.Amount,
                            "Different-wrap allocation: use actual receiving capital under the evidenced fund-specific mandate; do not inherit another fund's HWM. Unclassified receipts still require their allocation evidence.", route.Reference));
                continue;
            }
            var affected = route.Sources.Concat(route.Targets).Select(x => x.Account).Distinct().ToList();
            void Gap(string detail)
            {
                foreach (var target in route.Targets)
                    if (inputs.TryGetValue(target.Account, out var root))
                    {
                        root.Wraps.Single(x => x.Code == target.Wrap).Input.EvidenceIssues.Add(detail);
                        root.CapitalTraces.Add(new(target.Account, target.Wrap, route.Sources.Min(x => x.Date), target.Date,
                            target.Amount, detail, route.Reference));
                    }
            }
            if (!confirmed.Contains(route.Key))
            { Gap("Staged transfer is balanced in the supplied rows; record its documentary mandate-continuity reference before carrying benchmark/HWM."); continue; }
            if (affected.Any(x => !inputs.ContainsKey(x)))
            { Gap("Staged transfer source is outside this draft batch (possibly an approved claim). Its frozen calculation has not been changed; source protection must be explicitly available before carrying it."); continue; }
            // Work on copies. An incomplete split/merge must not consume just some source capital.
            var copies = affected.ToDictionary(x => x, x => FeeJson.Read<FeePreparation>(FeeJson.Write(inputs[x])));
            var sources = new List<(FeeCapitalLeg Leg, FeePreparation Input, decimal Benchmark, decimal Hwm, List<string> Requirements)>();
            string? issue = null;
            foreach (var leg in route.Sources)
            {
                var root = copies[leg.Account];
                var child = root.Wraps.SingleOrDefault(x => x.Code == leg.Wrap)?.Input;
                if (child is null || leg.Date <= child.From || leg.Date > child.To || leg.ValueBefore is null or <= 0 ||
                    leg.Amount > leg.ValueBefore || child.Movements.Any(x => x.Date == leg.Date) || root.Transfers.Count > 0)
                { issue = $"{leg.Account}/{leg.Wrap}: source-scope valuation or same-day ordering is not established; no transfer protection was reset."; break; }
                var ledger = workspace.Accounts.Single(x => x.Number == leg.Account).Ledger;
                if (ledger.Wraps.Any(x => x.Code == "ATXF" && x.Issues.Count > 0))
                { issue = $"{leg.Account}: tax/cash staging units do not reconcile; resolve the original statement discrepancy before carrying protection."; break; }
                var sourceRows = ledger.Rows.Where(x => leg.Keys.Contains(x.Key) && x.Wrap == leg.Wrap).ToList();
                if (ledger.Rows.Any(x => x.Wrap == leg.Wrap && x.Date == leg.Date && x.Kind == "InternalSwitch" && !leg.Keys.Contains(x.Key)))
                { issue = $"{leg.Account}/{leg.Wrap}: another same-day switch makes the staged exit ambiguous."; break; }
                var switchIssue = $"{leg.Date:yyyy-MM-dd}: unmatched switch crosses this wrap or settlement date. Verify transferred benchmark/HWM continuity; do not treat it as a fresh contribution.";
                var check = FeeJson.Read<FeePreparation>(FeeJson.Write(child));
                check.To = leg.Date; check.ClosingValue = leg.ValueBefore; check.ClosingReference = leg.Reference;
                check.Movements.RemoveAll(x => x.Date >= leg.Date);
                check.Benchmarks = check.Benchmarks.Where(x => x.From < leg.Date).Select(x => new FeeBenchmarkPeriod
                    { From = x.From, To = x.To < leg.Date ? x.To : leg.Date, CpiPercent = x.CpiPercent, Reference = x.Reference }).ToList();
                check.EvidenceIssues.RemoveAll(x => x == switchIssue || sourceRows.Any(r => x == TransferIssue(r)) ||
                    DateOnly.TryParseExact(x.Length >= 10 ? x[..10] : "", "yyyy-MM-dd", out var date) && date > leg.Date);
                check.CashFlowsComplete = check.EvidenceIssues.Count == 0 && check.Movements.All(x => x.Kind != "Withdrawal" || x.ValueBeforeWithdrawal != null);
                var validation = PerformanceFeeCalculator.Calculate(check, Today);
                if (!validation.HasCalculation || check.OpeningBenchmark is null || check.OpeningHighWaterMark is null)
                { issue = $"{leg.Account}/{leg.Wrap}: transfer rows are supplied, but source benchmark/HWM is unavailable. Supply earlier AIMS/Glacier history or a verified fee checkpoint; pre-AIMS history is not requested."; break; }
                var protection = PerformanceFeeCalculator.ProtectionAt(check, leg.Date);
                var ratio = leg.Amount / leg.ValueBefore.Value;
                sources.Add((leg, child, protection.Benchmark * ratio, protection.Hwm * ratio,
                    validation.Issues.Where(x => !x.Contains("first investment anniversary", StringComparison.Ordinal)).ToList()));
                child.EvidenceIssues.RemoveAll(x => x == switchIssue || sourceRows.Any(r => x == TransferIssue(r)));
                child.Movements.Add(new() { Date = leg.Date, Kind = "Withdrawal", Amount = leg.Amount,
                    ValueBeforeWithdrawal = leg.ValueBefore, Reference = "Staged protected transfer: " + route.Reference + "; " + reason });
            }
            var total = route.Sources.Sum(x => x.SettledAmount ?? x.Amount);
            foreach (var targetLeg in route.Targets)
            {
                if (issue is not null) break;
                var root = copies[targetLeg.Account];
                var target = root.Wraps.SingleOrDefault(x => x.Code == targetLeg.Wrap)?.Input;
                if (target is null || targetLeg.Date < target.From || targetLeg.Date > target.To || target.Movements.Any(x => x.Date == targetLeg.Date))
                { issue = $"{targetLeg.Account}/{targetLeg.Wrap}: target period or same-day cash-flow ordering requires evidence."; break; }
                foreach (var source in sources)
                {
                    if (source.Input.Method != target.Method || source.Input.HurdleSpreadPercent != target.HurdleSpreadPercent ||
                        target.FeePercent != 0 && (source.Input.FeePercent != target.FeePercent || source.Input.VatPercent != target.VatPercent))
                    { issue = "A staged route changes fee terms; record the applicable mandate treatment before carrying protection."; break; }
                    var ratio = targetLeg.Amount / total;
                    var reference = $"Staged {source.Leg.Account}/{source.Leg.Wrap} to {targetLeg.Account}/{targetLeg.Wrap}; {route.Reference}; continuity: {reason}";
                    var growth = Growth(source.Input, source.Leg.Date, targetLeg.Date);
                    if (growth is null) { issue = "CPI coverage is missing during cash staging; the elapsed benchmark must not be silently stopped."; break; }
                    var b = source.Benchmark * ratio * growth.Value; var h = source.Hwm * ratio;
                    var capital = (source.Leg.SettledAmount ?? source.Leg.Amount) * ratio;
                    if (targetLeg.Date == target.From)
                    {
                        target.OpeningTransfers.Add(new(source.Leg.Account, source.Leg.Wrap, targetLeg.Date, capital, b, h, reference));
                        target.OpeningValue = (target.OpeningValue ?? 0) + capital;
                        target.OpeningBenchmark = (target.OpeningBenchmark ?? 0) + b;
                        target.OpeningHighWaterMark = (target.OpeningHighWaterMark ?? 0) + h;
                        target.OpeningReference += "; " + reference;
                        target.PriorFeeReviewReference = "Source protection carried through reconciled administrator cash staging; " + reference;
                    }
                    else target.Movements.Add(new() { Date = targetLeg.Date, Kind = "TransferIn", Amount = capital,
                        TransferredBenchmark = b, TransferredHighWaterMark = h, Reference = reference });
                    if (copies[source.Leg.Account].Inception is { } inception && (root.Inception is null || inception < root.Inception))
                    { root.Inception = inception; foreach (var wrap in root.Wraps) wrap.Input.Inception = inception; }
                    target.ReviewIssues.AddRange(source.Requirements.Select(x => $"Carried protection from {source.Leg.Account}/{source.Leg.Wrap}: {x}"));
                    target.ReviewIssues.AddRange(copies[source.Leg.Account].ReviewPoints.Where(x => !x.Resolved).Select(x => $"Carried protection from {source.Leg.Account}/{source.Leg.Wrap}: {x.Detail}"));
                }
                var rows = workspace.Accounts.Single(x => x.Number == targetLeg.Account).Ledger.Rows.Where(x => targetLeg.Keys.Contains(x.Key));
                target.EvidenceIssues.RemoveAll(x => rows.Any(r => x == TransferIssue(r)));
            }
            if (issue is not null) { Gap(issue); continue; }
            foreach (var (account, copy) in copies)
            {
                copy.AccountTransferDecisions.Add(new(route.Key, reason, ComplianceWorkflowAccess.Label(actor), DateTime.UtcNow));
                copy.CapitalTraces.AddRange(route.Targets.Select(x => new FeeCapitalTrace(account, x.Wrap,
                    route.Sources.Min(s => s.Date), x.Date, x.Amount, "Protected staged route applied; source benchmark/HWM carried, not reset", route.Reference)));
                foreach (var wrap in copy.Wraps)
                    wrap.Input.CashFlowsComplete = wrap.Input.EvidenceIssues.Count == 0 && wrap.Input.Movements.All(x => x.Kind != "Withdrawal" || x.ValueBeforeWithdrawal != null);
                inputs[account] = copy;
            }
        }
    }

    private static decimal? Growth(FeePreparation input, DateOnly from, DateOnly to)
    {
        decimal growth = 1; var cursor = from;
        foreach (var p in input.Benchmarks.OrderBy(x => x.From))
        {
            if (p.To <= cursor || p.From >= to) continue;
            if (p.From > cursor || input.HurdleSpreadPercent is null) return null;
            var end = p.To < to ? p.To : to;
            growth *= (decimal)Math.Pow(1 + (double)(p.CpiPercent + input.HurdleSpreadPercent.Value) / 100, (end.DayNumber - cursor.DayNumber) / 365d);
            cursor = end;
        }
        return cursor == to ? growth : null;
    }
}
