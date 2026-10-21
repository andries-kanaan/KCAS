namespace KCAS.Admin.Data;

public static class PerformanceFeeWrapCalculator
{
    public static FeeCalculationResult Calculate(FeePreparation input, DateOnly today)
    {
        var allZeroRates = input.Wraps.Count > 0 && input.Wraps.All(x => x.Input.FeePercent == 0 && x.Input.ZeroRateConfirmed);
        var result = new FeeCalculationResult { Issues = allZeroRates ? [] : [.. input.EvidenceIssues] };
        if (input.Wraps.Count > 20 || input.Wraps.Any(x => string.IsNullOrWhiteSpace(x.Code) || x.Input.Wraps.Count > 0 || x.Input.Transfers.Count > 0) ||
            input.Wraps.Select(x => x.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != input.Wraps.Count)
        { result.Issues.Add("Use unique wrap codes and one calculation level, with no nested fee scopes."); return result; }
        var wraps = input.Wraps.ToDictionary(x => x.Code, x => FeeJson.Read<FeePreparation>(FeeJson.Write(x.Input)), StringComparer.OrdinalIgnoreCase);
        foreach (var (code, child) in wraps)
        {
            if (child.From != input.From || child.To != input.To || child.Inception != input.Inception)
                result.Issues.Add($"{code}: all wraps must use the account's preserved anniversary and common checkpoint/cutoff.");
        }
        if (input.Transfers.Count > 10000) { result.Issues.Add("Too many scope transfers."); return result; }
        foreach (var t in input.Transfers.Where(_ => !allZeroRates).OrderBy(x => x.Date))
        {
            if (!t.ContinuityConfirmed || string.IsNullOrWhiteSpace(t.Reference) || t.Amount <= 0 || t.ValueBeforeTransfer < t.Amount ||
                t.ValueBeforeTransfer is null or <= 0 || t.Date <= input.From || t.Date > input.To ||
                !wraps.TryGetValue(t.FromWrap, out var source) || !wraps.TryGetValue(t.ToWrap, out var target) || t.FromWrap.Equals(t.ToWrap, StringComparison.OrdinalIgnoreCase))
            { result.Issues.Add($"{t.Date:yyyy-MM-dd}: verify transfer pairing, pre-transfer valuation and mandate continuity for {t.FromWrap} to {t.ToWrap}."); continue; }
            if (source.Movements.Any(x => x.Date == t.Date && x.Kind != "TransferIn"))
            { result.Issues.Add($"{t.FromWrap}: separate the ordering of same-day external flows and transfers before calculating."); continue; }
            source.Movements.Add(new() { Date = t.Date, Kind = "Withdrawal", Amount = t.Amount, ValueBeforeWithdrawal = t.ValueBeforeTransfer, Reference = t.Reference });
            target.Movements.Add(new() { Date = t.Date, Kind = "ScopeFunding", Amount = t.Amount,
                Reference = "Different-wrap allocation: actual receiving capital starts this fund's protection; " + t.Reference });
        }
        var scopeIssues = result.Issues.ToList();
        foreach (var (code, child) in wraps)
        {
            var calculation = PerformanceFeeCalculator.Calculate(child, today);
            result.Wraps.Add(new(code, child.WrapFund, calculation));
            result.Issues.AddRange(calculation.Issues.Select(x => code + ": " + x));
            result.CalculationIssues.AddRange(calculation.CalculationIssues.Select(x => code + ": " + x));
            result.Benchmark += calculation.Benchmark; result.HighWaterMark += calculation.HighWaterMark;
            result.Threshold += calculation.Threshold; result.Outperformance += calculation.Outperformance;
            result.FeeExVat += calculation.FeeExVat; result.Vat += calculation.Vat; result.Total += calculation.Total;
        }
        result.CalculationIssues.AddRange(scopeIssues);
        result.HasCalculation = scopeIssues.Count == 0 && result.Wraps.All(x => x.Result.CanShowCalculation);
        result.HasVatCalculation = result.HasCalculation && result.Wraps.All(x => x.Result.IsZeroRateReview || x.Result.HasVatCalculation || x.Result.IsReady);
        if (result.Issues.Count == 0 && input.To is { } to)
        {
            var protectedWraps = result.Wraps.Where(x => x.Result.NextCheckpoint != null).ToList();
            result.IsZeroRateReview = protectedWraps.Count == 0 && result.Wraps.All(x => x.Result.IsZeroRateReview);
            if (protectedWraps.Count > 0)
                result.NextCheckpoint = new(to, protectedWraps.Sum(x => x.Result.NextCheckpoint!.Value),
                    protectedWraps.Sum(x => x.Result.NextCheckpoint!.Benchmark), protectedWraps.Sum(x => x.Result.NextCheckpoint!.HighWaterMark), "Per-wrap protected checkpoints; 0% reviews do not create financial baselines")
                { Wraps = protectedWraps.Select(x => new FeeWrapCheckpoint(x.Code, x.Result.NextCheckpoint!)).ToList() };
        }
        return result;
    }
}
