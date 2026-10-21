using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class PerformanceFeeCalculatorTests
{
    internal static readonly DateOnly Start = new(2021, 1, 1);
    internal static FeePreparation Input() => new()
    {
        MethodMatchesMandate = true, IsSouthAfricanWrap = true,
        MandateReference = "Synthetic signed mandate", MandateEffectiveFrom = Start,
        MethodRationale = "Synthetic administrator confirms ACT/365 capital benchmark, proportional withdrawals and after-fee reset.",
        WrapFund = "Synthetic SA wrap", Administrator = "Synthetic administrator", ScopeRationale = "One exclusive client wrap scope",
        Inception = Start, From = Start, To = Start.AddDays(365),
        OpeningValue = 100000, OpeningBenchmark = 100000, OpeningHighWaterMark = 100000,
        OpeningReference = "Synthetic subscription statement", NoPriorFeeConfirmed = true, PriorFeeReviewReference = "Synthetic complete inception history",
        ClosingValue = 120000, ClosingReference = "Synthetic closing statement", FeePercent = 15, HurdleSpreadPercent = 3,
        VatPercent = 15, VatReference = "Synthetic VAT treatment", CashFlowsComplete = true, CashFlowReference = "Synthetic reconciled cash ledger",
        Benchmarks = [new() { From = Start, To = Start.AddDays(365), CpiPercent = 4, Reference = "Synthetic CPI convention" }]
    };

    [Fact]
    public void Charges_only_excess_and_rounds_exclusive_fee_then_VAT()
    {
        var result = PerformanceFeeCalculator.Calculate(Input(), Start.AddYears(5));
        Assert.Empty(result.Issues);
        Assert.Equal(107000m, result.Threshold); Assert.Equal(13000m, result.Outperformance);
        Assert.Equal(1950m, result.FeeExVat); Assert.Equal(292.5m, result.Vat); Assert.Equal(2242.5m, result.Total);
        Assert.Equal(117757.5m, result.NextCheckpoint!.Value);
        Assert.Equal(result.NextCheckpoint.Value, result.NextCheckpoint.Benchmark);
        Assert.Equal(result.NextCheckpoint.Value, result.NextCheckpoint.HighWaterMark);
    }

    [Fact]
    public void Catchup_uses_entire_multi_year_hurdle_not_one_year_or_times_current_balance()
    {
        var input = Input(); input.To = Start.AddDays(1095); input.Benchmarks[0].To = input.To.Value;
        input.ClosingValue = 150000;
        var result = PerformanceFeeCalculator.Calculate(input, Start.AddYears(5));
        Assert.Empty(result.Issues); Assert.Equal(122504.30m, result.Threshold); Assert.Equal(4124.36m, result.FeeExVat);
    }

    [Fact]
    public void Topup_increases_protected_capital_without_resetting_anniversary()
    {
        var input = Input(); input.Movements.Add(new() { Date = input.To!.Value, Amount = 50000, Reference = "Synthetic contribution" });
        input.ClosingValue = 170000;
        var result = PerformanceFeeCalculator.Calculate(input, Start.AddYears(5));
        Assert.Empty(result.Issues); Assert.Equal(157000m, result.Threshold); Assert.Equal(1950m, result.FeeExVat);
        Assert.Equal(new DateOnly(2023, 1, 1), PerformanceFeeCalculator.NextAnniversary(Start, input.To.Value));
    }

    [Fact]
    public void Withdrawal_scales_benchmark_and_HWM_using_actual_pre_withdrawal_value()
    {
        var input = Input(); input.Movements.Add(new() { Date = input.To!.Value, Kind = "Withdrawal", Amount = 60000, ValueBeforeWithdrawal = 120000, Reference = "Synthetic redemption statement" });
        input.ClosingValue = 60000;
        var result = PerformanceFeeCalculator.Calculate(input, Start.AddYears(5));
        Assert.Empty(result.Issues); Assert.Equal(53500m, result.Threshold); Assert.Equal(50000m, result.HighWaterMark); Assert.Equal(975m, result.FeeExVat);
    }

    [Fact]
    public void Zero_fee_preserves_accumulated_underperformance_through_subsequent_review()
    {
        var input = Input(); input.ClosingValue = 80000;
        var first = PerformanceFeeCalculator.Calculate(input, Start.AddYears(5));
        Assert.Empty(first.Issues); Assert.Equal(0m, first.Total); Assert.Equal(107000m, first.NextCheckpoint!.Benchmark); Assert.Equal(100000m, first.NextCheckpoint.HighWaterMark);
        input.From = first.NextCheckpoint.Date; input.To = input.From.Value.AddDays(365);
        input.OpeningValue = first.NextCheckpoint.Value; input.OpeningBenchmark = first.NextCheckpoint.Benchmark; input.OpeningHighWaterMark = first.NextCheckpoint.HighWaterMark;
        input.ClosingValue = 105000; input.NoPriorFeeConfirmed = false;
        input.Benchmarks = [new() { From = input.From.Value, To = input.To.Value, CpiPercent = 4, Reference = "Synthetic second year CPI" }];
        var second = PerformanceFeeCalculator.Calculate(input, Start.AddYears(5));
        Assert.Empty(second.Issues); Assert.Equal(114490m, second.Threshold); Assert.Equal(0m, second.Total);
    }

    [Fact]
    public void Different_benchmark_segments_compound_only_their_actual_days()
    {
        var input = Input(); input.To = Start.AddDays(730); input.ClosingValue = 140000;
        input.Benchmarks.Add(new() { From = Start.AddDays(365), To = input.To.Value, CpiPercent = 6, Reference = "Synthetic changed CPI" });
        var result = PerformanceFeeCalculator.Calculate(input, Start.AddYears(5));
        Assert.Empty(result.Issues); Assert.Equal(116630m, result.Threshold);
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("overlap")]
    [InlineData("unknown method")]
    [InlineData("missing withdrawal value")]
    [InlineData("prior fee unknown")]
    [InlineData("under one year")]
    [InlineData("offshore")]
    [InlineData("future")]
    [InlineData("duplicate cashflow")]
    public void Unsupported_or_incomplete_inputs_are_not_claimable(string problem)
    {
        var input = Input();
        switch (problem)
        {
            case "gap": input.Benchmarks[0].From = Start.AddDays(1); break;
            case "overlap": input.Benchmarks.Add(input.Benchmarks[0]); break;
            case "unknown method": input.MethodMatchesMandate = false; break;
            case "missing withdrawal value": input.Movements.Add(new() { Date = Start.AddDays(100), Kind = "Withdrawal", Amount = 1, Reference = "Synthetic" }); break;
            case "prior fee unknown": input.NoPriorFeeConfirmed = false; break;
            case "under one year": input.To = Start.AddDays(100); input.Benchmarks[0].To = input.To.Value; break;
            case "offshore": input.IsSouthAfricanWrap = false; break;
            case "future": input.To = Start.AddYears(10); input.Benchmarks[0].To = input.To.Value; break;
            case "duplicate cashflow": var movement = new FeeMovement { Date = Start.AddDays(100), Amount = 1, Reference = "Synthetic" }; input.Movements = [movement, movement]; break;
        }
        var result = PerformanceFeeCalculator.Calculate(input, Start.AddYears(5));
        Assert.False(result.IsReady); Assert.NotEmpty(result.Issues); Assert.Null(result.NextCheckpoint);
    }

    [Fact]
    public void Catchup_returns_to_original_anniversary_and_leap_dates_do_not_drift()
    {
        Assert.Equal(new DateOnly(2027, 4, 10), PerformanceFeeCalculator.NextAnniversary(new(2010, 4, 10), new(2026, 10, 20)));
        Assert.Equal(new DateOnly(2028, 2, 29), PerformanceFeeCalculator.NextAnniversary(new(2020, 2, 29), new(2027, 2, 28)));
    }
}
