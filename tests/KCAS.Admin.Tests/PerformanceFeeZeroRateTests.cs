using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class PerformanceFeeZeroRateTests
{
    internal static FeePreparation ZeroInput()
    {
        var input = PerformanceFeeCalculatorTests.Input();
        input.FeePercent = 0; input.ZeroRateConfirmed = true;
        input.WrapFund = "Synthetic Stable SA wrap";
        input.MethodMatchesMandate = false; input.MethodRationale = "";
        input.OpeningValue = input.OpeningBenchmark = input.OpeningHighWaterMark = null;
        input.Benchmarks.Clear(); input.HurdleSpreadPercent = null; input.VatPercent = null; input.VatReference = "";
        input.NoPriorFeeConfirmed = false; input.PriorFeeReviewReference = ""; input.CashFlowsComplete = false;
        return input;
    }

    [Fact]
    public void Verified_zero_rate_needs_no_invented_CPI_VAT_or_financial_checkpoint()
    {
        var input = ZeroInput(); input.EvidenceIssues.Add("Historical cash-flow ambiguity retained for transfers");
        var result = PerformanceFeeCalculator.Calculate(input, input.To!.Value);
        Assert.True(result.IsReady); Assert.True(result.IsZeroRateReview);
        Assert.Equal(0, result.Total); Assert.Null(result.NextCheckpoint);
        Assert.Contains("Verified 0%", FeeResultDescription.Describe(result));
        Assert.False(input.CashFlowsComplete); Assert.NotEmpty(input.EvidenceIssues);
        input.RequiresRebuild = true;
        Assert.False(PerformanceFeeCalculator.Calculate(input, input.To.Value).IsReady);
        input.RequiresRebuild = false;
        input.ZeroRateConfirmed = false;
        Assert.False(PerformanceFeeCalculator.Calculate(input, input.To.Value).IsReady);
        input.ZeroRateConfirmed = true; input.MandateEffectiveFrom = input.From!.Value.AddDays(1);
        Assert.False(PerformanceFeeCalculator.Calculate(input, input.To.Value).IsReady);
    }

    [Fact]
    public void Missing_rate_zero_balance_and_no_outperformance_are_not_zero_rate_mandates()
    {
        var input = PerformanceFeeCalculatorTests.Input(); input.FeePercent = null; input.ClosingValue = 0;
        var unknown = PerformanceFeeCalculator.Calculate(input, input.To!.Value);
        Assert.False(unknown.IsReady); Assert.Contains("Calculation incomplete", FeeResultDescription.Describe(unknown));
        input.FeePercent = 15;
        var loss = PerformanceFeeCalculator.Calculate(input, input.To.Value);
        Assert.True(loss.IsReady); Assert.Contains("No chargeable outperformance", FeeResultDescription.Describe(loss));
        Assert.Equal(107000, loss.NextCheckpoint!.Benchmark);
    }

    [Fact]
    public void Stable_is_reported_without_copying_the_active_wrap_rate_and_losing_wrap_protection_survives()
    {
        var document = PerformanceFeeStatementReader.Read(PerformanceFeeStatementTests.Workbook(holding: "Synthetic Stable (KP1)"), "stable.xlsx");
        var ledger = FeeStatementLedger.Merge([document], "SYNTHETIC-IA");
        var projection = ledger.Prepare(PerformanceFeeCalculatorTests.Input(), null);
        Assert.Null(Assert.Single(projection.Wraps).Input.FeePercent);
        Assert.False(PerformanceFeeCalculator.Calculate(projection, projection.To!.Value).IsReady);
        projection.Wraps[0].Input.FeePercent = 0; projection.Wraps[0].Input.ZeroRateConfirmed = true;
        Assert.True(PerformanceFeeCalculator.Calculate(projection, projection.To.Value).IsZeroRateReview);

        var gain = PerformanceFeeCalculatorTests.Input();
        var loss = PerformanceFeeCalculatorTests.Input(); loss.ClosingValue = 80000;
        var root = PerformanceFeeCalculatorTests.Input();
        root.Wraps = [new() { Code = "KE1", Input = gain }, new() { Code = "KA6", Input = loss }, new() { Code = "KP1", Input = ZeroInput() }];
        var result = PerformanceFeeCalculator.Calculate(root, root.To!.Value);
        Assert.True(result.IsReady); Assert.Equal(3, result.Wraps.Count); Assert.Equal(2242.5m, result.Total);
        Assert.Equal(107000, result.NextCheckpoint!.Wraps.Single(x => x.Code == "KA6").Checkpoint.Benchmark);
        Assert.DoesNotContain(result.NextCheckpoint.Wraps, x => x.Code == "KP1");
    }
}
