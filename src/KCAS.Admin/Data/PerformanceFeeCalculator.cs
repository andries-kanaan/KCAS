namespace KCAS.Admin.Data;

public static class PerformanceFeeCalculator
{
    public static DateOnly NextAnniversary(DateOnly inception, DateOnly after)
    {
        var year = Math.Max(inception.Year + 1, after.Year);
        var candidate = inception.AddYears(year - inception.Year);
        return candidate > after ? candidate : inception.AddYears(year + 1 - inception.Year);
    }

    public static FeeCalculationResult Calculate(FeePreparation input, DateOnly today)
    {
        try
        {
            var result = input.Wraps.Count > 0 ? PerformanceFeeWrapCalculator.Calculate(input, today) : input.FeePercent == 0 ? ReviewZeroRate(input, today) : CalculateCore(input, today);
            foreach (var point in input.ReviewPoints.Where(x => !x.Resolved))
                result.Issues.Add("Fee disclosure review: " + point.Detail);
            if (input.ReviewPoints.Any(x => !x.Resolved)) result.NextCheckpoint = null;
            if (input.RequiresRebuild)
            {
                const string issue = "Source history or classification changed. Rebuild the draft before approval.";
                result.Issues.Add(issue); result.CalculationIssues.Add(issue); result.NextCheckpoint = null; result.HasCalculation = result.HasVatCalculation = false;
                foreach (var wrap in result.Wraps) { wrap.Result.HasCalculation = wrap.Result.HasVatCalculation = false; wrap.Result.NextCheckpoint = null; wrap.Result.Issues.Add(issue); wrap.Result.CalculationIssues.Add(issue); }
            }
            return result;
        }
        catch (OverflowException) { return new() { Issues = ["The inputs exceed supported calculation precision; verify amounts, benchmark rates and dates."] }; }
    }

    private static FeeCalculationResult ReviewZeroRate(FeePreparation input, DateOnly today)
    {
        var result = new FeeCalculationResult { IsZeroRateReview = true };
        if (!input.ZeroRateConfirmed || string.IsNullOrWhiteSpace(input.MandateReference) || string.IsNullOrWhiteSpace(input.ScopeRationale) ||
            !input.IsSouthAfricanWrap || string.IsNullOrWhiteSpace(input.WrapFund) || string.IsNullOrWhiteSpace(input.Administrator))
            result.Issues.Add("Verify and link the signed 0% PDF mandate for this exact SA wrap and fee scope; zero balance or missing terms is not a 0% mandate.");
        if (input.From is not { } from || input.To is not { } to || to <= from || to > today || input.MandateEffectiveFrom is not { } effective || effective > from || input.Inception is null || from < input.Inception)
            result.Issues.Add("Verify the zero-rate mandate covers the actual review period and original inception.");
        if (input.ClosingValue is null or < 0 || string.IsNullOrWhiteSpace(input.ClosingReference))
            result.Issues.Add("Record the actual closing statement value and reference for the zero-rate review.");
        // No fee is levied: no artificial CPI/VAT inputs, prior-fee assurance or financial HWM reset.
        // Cash-flow gaps remain in the saved input/ledger for transfers; this result verifies only the 0% fee.
        return result;
    }

    private static FeeCalculationResult CalculateCore(FeePreparation input, DateOnly today)
    {
        var result = new FeeCalculationResult();
        result.Issues.AddRange(input.EvidenceIssues);
        result.Issues.AddRange(input.ReviewIssues);
        void Need(bool condition, string message, bool calculation = true) { if (!condition) { result.Issues.Add(message); if (calculation) result.CalculationIssues.Add(message); } }
        Need(input.Method == FeePreparation.SupportedMethod, "Select a supported calculation method before performing arithmetic.");
        Need(input.MethodMatchesMandate,
            input.UseSharedPolicy ? "Shared Kanaan calculation convention requires KI adoption; see Convention and VAT." :
            "Confirm the KCAS calculation convention is appropriate to the mandate before claiming: ACT/365 hurdle, proportional withdrawals, protected transfers and after-fee reset.", false);
        Need(!string.IsNullOrWhiteSpace(input.MandateReference) && !string.IsNullOrWhiteSpace(input.MethodRationale), "Record the signed mandate and the calculation-method rationale.", false);
        Need(!string.IsNullOrWhiteSpace(input.WrapFund) && !string.IsNullOrWhiteSpace(input.Administrator) && !string.IsNullOrWhiteSpace(input.ScopeRationale), "Identify the SA wrap, administrator and exclusive fee scope (no duplicated holdings or internal switches).", false);
        Need(input.IsSouthAfricanWrap, "Confirm this is a South African client wrap mandate, not a fund-level offshore performance fee.");
        Need(input.Inception is { } inception && inception.Year >= 1900 && inception <= today, "Verify the original first-investment date.");
        Need(input.From is { } from && input.To is { } to && from >= input.Inception && to > from && to <= today, "Use an actual closing valuation date after the opening checkpoint, not a future date.");
        Need(input.Inception is { Year: < 9999 } first && input.To >= first.AddYears(1), "The first fee calculation is due only after the first investment anniversary.", false);
        Need(input.MandateEffectiveFrom is { } effective && effective <= input.From, "Verify that the mandate covers the entire fee period; split periods if terms changed.", false);
        Need(input.OpeningValue >= 0 && input.OpeningBenchmark >= 0 && input.OpeningHighWaterMark >= 0 && !string.IsNullOrWhiteSpace(input.OpeningReference), "Record the evidenced opening value, benchmark, high-water mark and checkpoint source.");
        Need(input.ClosingValue >= 0 && !string.IsNullOrWhiteSpace(input.ClosingReference), "Record the actual closing value before this PDF and its dated statement reference.");
        Need(input.FeePercent is >= 0 and <= 100 && input.HurdleSpreadPercent is >= 0 and <= 100, "Confirm the mandate fee and hurdle spread percentages.");
        Need(input.VatPercent is >= 0 and <= 100 && !string.IsNullOrWhiteSpace(input.VatReference), "Confirm whether VAT applies and record its basis; use an exclusive-of-VAT fee rate.", false);
        Need(input.CashFlowsComplete && !string.IsNullOrWhiteSpace(input.CashFlowReference), "Reconcile all actual external cash flows for the fee scope; recurring instructions alone are not payment evidence.");
        Need(!string.IsNullOrWhiteSpace(input.PriorFeeReviewReference), "Research previous fee deductions and record the checkpoint/no-prior-fee evidence.", false);
        if (input.From == input.Inception)
        {
            Need(input.NoPriorFeeConfirmed, "Confirm from actual history that no earlier performance fee was crystallised before starting at inception.", false);
            if (input.OpeningTransfers.Count == 0)
                Need(input.OpeningValue == input.OpeningBenchmark && input.OpeningValue == input.OpeningHighWaterMark, "An inception calculation starts with invested capital as both benchmark and high-water mark.");
        }
        if (input.OpeningTransfers.Count > 0)
        {
            Need(input.OpeningTransfers.All(x => x.Date == input.From && x.Amount > 0 && x.Benchmark >= 0 && x.HighWaterMark >= 0 && !string.IsNullOrWhiteSpace(x.Account) && !string.IsNullOrWhiteSpace(x.Reference)),
                "Opening transfers require an evidenced source, date, amount and carried benchmark/HWM.");
            var transferred = input.OpeningTransfers.Sum(x => x.Amount);
            Need(input.OpeningValue >= transferred && input.OpeningBenchmark == input.OpeningValue - transferred + input.OpeningTransfers.Sum(x => x.Benchmark) &&
                input.OpeningHighWaterMark == input.OpeningValue - transferred + input.OpeningTransfers.Sum(x => x.HighWaterMark),
                "Opening protected capital must include the source transfers' actual benchmark/HWM, not a market-value reset.");
        }
        var periods = input.Benchmarks.OrderBy(x => x.From).ToList();
        var cursor = input.From;
        foreach (var period in periods)
        {
            Need(period.From == cursor && period.To > period.From && period.To <= input.To,
                "CPI benchmark segments must cover the full opening-to-closing period without gaps, overlaps or extra days.");
            Need(period.CpiPercent is > -100 and <= 100 && !string.IsNullOrWhiteSpace(period.Reference), "Supply the actual evidenced CPI basis for every benchmark segment, not an assumed projection rate.");
            cursor = period.To;
        }
        Need(periods.Count > 0 && cursor == input.To, "Supply benchmark coverage for the entire elapsed period, including catch-up years.");
        foreach (var move in input.Movements)
        {
            Need(move.Date > input.From && move.Date <= input.To && move.Amount > 0 && !string.IsNullOrWhiteSpace(move.Reference), "Each cash flow needs an actual date within the period, positive amount and evidence reference.");
            Need(move.Kind is "Contribution" or "Withdrawal" or "TransferIn" or "ScopeFunding", "Classify actual capital movements; internal rebalancing is not an external cash flow.");
            if (move.Kind == "TransferIn") Need(move.TransferredBenchmark >= 0 && move.TransferredHighWaterMark >= 0,
                "An incoming scope transfer requires evidenced carried benchmark and HWM, not a fresh capital reset.");
            if (move.Kind == "Withdrawal") Need(move.ValueBeforeWithdrawal > 0 && move.Amount <= move.ValueBeforeWithdrawal,
                "Every withdrawal requires its verified immediately-before-withdrawal portfolio value, covering the same fee scope.");
        }
        Need(!input.Movements.GroupBy(x => new { x.Date, x.Kind, x.Amount, x.Reference }).Any(x => x.Count() > 1), "Resolve duplicate cash-flow rows.");
        result.CalculationIssues.InsertRange(0, input.EvidenceIssues);
        if (result.CalculationIssues.Count != 0) return result;

        decimal benchmark = input.OpeningBenchmark!.Value, hwm = input.OpeningHighWaterMark!.Value;
        cursor = input.From!.Value;
        void GrowTo(DateOnly date)
        {
            foreach (var period in periods)
            {
                var start = cursor.Value > period.From ? cursor.Value : period.From;
                var end = date < period.To ? date : period.To;
                if (end <= start) continue;
                var annual = 1 + (double)(period.CpiPercent + input.HurdleSpreadPercent!.Value) / 100;
                benchmark *= (decimal)Math.Pow(annual, (end.DayNumber - start.DayNumber) / 365d);
            }
            cursor = date;
        }
        result.Steps.Add(new(cursor.Value, "Opening checkpoint", Money(benchmark), Money(hwm)));
        // Preserve caller order for movements on the same day: each pre-withdrawal value refers to that order.
        foreach (var move in input.Movements.OrderBy(x => x.Date))
        {
            GrowTo(move.Date);
            if (move.Kind is "Contribution" or "ScopeFunding") { benchmark += move.Amount; hwm += move.Amount; }
            else if (move.Kind == "TransferIn") { benchmark += move.TransferredBenchmark!.Value; hwm += move.TransferredHighWaterMark!.Value; }
            else
            {
                var ratio = 1 - move.Amount / move.ValueBeforeWithdrawal!.Value;
                benchmark *= ratio; hwm *= ratio;
            }
            result.Steps.Add(new(move.Date, $"{move.Kind}: ZAR {move.Amount:N2}", Money(benchmark), Money(hwm)));
        }
        GrowTo(input.To!.Value);
        result.Benchmark = Money(benchmark); result.HighWaterMark = Money(hwm);
        var threshold = Math.Max(benchmark, hwm);
        result.Threshold = Money(threshold);
        var excess = Math.Max(0, input.ClosingValue!.Value - threshold);
        result.Outperformance = Money(excess);
        result.FeeExVat = Money(excess * input.FeePercent!.Value / 100);
        result.HasCalculation = true;
        result.HasVatCalculation = input.VatPercent is >= 0 and <= 100 && !string.IsNullOrWhiteSpace(input.VatReference);
        result.Vat = result.HasVatCalculation ? Money(result.FeeExVat * input.VatPercent!.Value / 100) : 0;
        result.Total = result.FeeExVat + result.Vat;
        if (result.Total > input.ClosingValue)
        {
            result.Issues.Add("The fee including VAT exceeds closing capital; verify the mandate rate and VAT treatment.");
            return result;
        }
        var net = input.ClosingValue.Value - result.Total;
        // A zero-fee review must never wipe out accumulated underperformance or its hurdle.
        if (result.Issues.Count == 0) result.NextCheckpoint = new(input.To.Value, net,
            result.Total > 0 ? net : benchmark, result.Total > 0 ? net : hwm, "Pending verified fee deduction / approved zero-fee review");
        result.Steps.Add(new(input.To.Value, "Closing threshold before this PDF", result.Benchmark, result.HighWaterMark));
        return result;
    }

    private static decimal Money(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    internal static (decimal Benchmark, decimal Hwm) ProtectionAt(FeePreparation input, DateOnly through)
    {
        var b = input.OpeningBenchmark!.Value; var h = input.OpeningHighWaterMark!.Value;
        var cursor = input.From!.Value;
        void Grow(DateOnly date)
        {
            foreach (var p in input.Benchmarks.OrderBy(x => x.From))
            {
                var start = cursor > p.From ? cursor : p.From; var end = date < p.To ? date : p.To;
                if (end > start) b *= (decimal)Math.Pow(1 + (double)(p.CpiPercent + input.HurdleSpreadPercent!.Value) / 100, (end.DayNumber - start.DayNumber) / 365d);
            }
            cursor = date;
        }
        foreach (var m in input.Movements.Where(x => x.Date <= through).OrderBy(x => x.Date))
        {
            Grow(m.Date);
            if (m.Kind is "Contribution" or "ScopeFunding") { b += m.Amount; h += m.Amount; }
            else if (m.Kind == "TransferIn") { b += m.TransferredBenchmark!.Value; h += m.TransferredHighWaterMark!.Value; }
            else { var ratio = 1 - m.Amount / m.ValueBeforeWithdrawal!.Value; b *= ratio; h *= ratio; }
        }
        Grow(through); return (b, h);
    }
}
