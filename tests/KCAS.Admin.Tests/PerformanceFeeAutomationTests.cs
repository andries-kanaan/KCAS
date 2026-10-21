using System.ComponentModel.DataAnnotations;
using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class PerformanceFeeAutomationTests
{
    [Fact]
    public void Approval_requirements_do_not_hide_supported_arithmetic_or_invent_VAT()
    {
        var input = PerformanceFeeCalculatorTests.Input(); input.MethodMatchesMandate = false; input.VatPercent = null; input.VatReference = "";
        var result = PerformanceFeeCalculator.Calculate(input, input.To!.Value);
        Assert.True(result.HasCalculation); Assert.Empty(result.CalculationIssues); Assert.False(result.HasVatCalculation);
        Assert.False(result.IsReady); Assert.Null(result.NextCheckpoint); Assert.Equal(1950, result.FeeExVat);
        Assert.Contains("Calculated draft", FeeResultDescription.Describe(result));
        input.Benchmarks.Clear();
        result = PerformanceFeeCalculator.Calculate(input, input.To.Value);
        Assert.False(result.HasCalculation); Assert.NotEmpty(result.CalculationIssues);
    }

    [Fact]
    public void Source_invalidation_hides_every_stale_wrap_amount_even_if_old_inputs_still_compute()
    {
        var input = PerformanceFeeCalculatorTests.Input(); input.Wraps = [new() { Code = "KE1", Input = PerformanceFeeCalculatorTests.Input() }];
        input.RequiresRebuild = true;
        var result = PerformanceFeeCalculator.Calculate(input, input.To!.Value);
        Assert.False(result.CanShowCalculation); Assert.False(result.Wraps[0].Result.CanShowCalculation);
        Assert.False(result.Wraps[0].Result.HasVatCalculation); Assert.Null(result.NextCheckpoint);
    }

    [Fact]
    public void Published_CPI_changes_on_publication_not_observation_month_and_never_looks_forward()
    {
        var rates = new[] { new FeeCpiObservation(new(2023, 12, 1), new(2024, 1, 24), 5.1m, "Synthetic release A"),
            new FeeCpiObservation(new(2024, 1, 1), new(2024, 2, 21), 5.3m, "Synthetic release B") };
        var periods = FeePublishedCpi.Periods(rates, new(2024, 2, 1), new(2024, 3, 1));
        Assert.Equal(2, periods.Count); Assert.Equal(5.1m, periods[0].CpiPercent); Assert.Equal(new DateOnly(2024, 2, 21), periods[0].To);
        Assert.Equal(5.3m, periods[1].CpiPercent); Assert.Equal(periods[0].To, periods[1].From);
        Assert.Throws<ValidationException>(() => FeePublishedCpi.Periods(rates, new(2024, 1, 1), new(2024, 3, 1)));
        Assert.Throws<ValidationException>(() => FeePublishedCpi.Periods(rates, new(2024, 2, 1), new(2024, 6, 1)));
        Assert.Throws<ValidationException>(() => FeePublishedCpi.Periods([rates[0], rates[1] with { Month = new(2024, 2, 1), PublishedOn = new(2024, 3, 1) }], new(2024, 2, 1), new(2024, 3, 2)));
        Assert.Throws<ValidationException>(() => FeePublishedCpi.Periods(rates.Append(rates[1] with { AnnualPercent = 99 }), new(2024, 2, 1), new(2024, 3, 1)));
        var csv = "Month,PublishedOn,AnnualPercent,Reference\n2023-12-01,2024-01-24,5.1,\"Synthetic, cited release\"";
        Assert.Equal("Synthetic, cited release", Assert.Single(FeePublishedCpi.ReadCsv(csv)).Reference);
        Assert.Throws<ValidationException>(() => FeePublishedCpi.ReadCsv(csv + "\n2023-12-01,2024-01-24,5.1,duplicate"));
    }

    [Fact]
    public void Covered_mandate_reuses_terms_only_for_exact_client_account_and_wrap_without_copying_history()
    {
        var source = new FeePreparation { MandateSourceAccountId = 77, MandateAccountIds = [42], MandateCoverageReference = "Synthetic signed schedule covering account 42",
            Wraps = [new() { Code = "KE1", Input = PerformanceFeeCalculatorTests.Input() }] };
        var child = new FeePreparation { From = PerformanceFeeCalculatorTests.Start, OpeningValue = 321, Inception = new(2019, 1, 1) };
        var target = new FeePreparation { Wraps = [new() { Code = "KE1", Input = child }, new() { Code = "KP1", Input = new() }] };
        PerformanceFeeService.ApplyCoveredMandate(target, 99, [source]); Assert.Null(child.FeePercent);
        PerformanceFeeService.ApplyCoveredMandate(target, 42, [source]);
        Assert.Equal(15, child.FeePercent); Assert.Equal(321, child.OpeningValue); Assert.Equal(new DateOnly(2019, 1, 1), child.Inception);
        Assert.False(child.NoPriorFeeConfirmed); Assert.Null(target.Wraps[1].Input.FeePercent);
        Assert.Equal(77, child.MandateSourceAccountId);
        source.Wraps[0].Input.VatPercent = 0;
        PerformanceFeeService.ApplyCoveredMandate(target, 42, [source]); Assert.Equal(0, child.VatPercent);
        child.FeePercent = 13;
        PerformanceFeeService.ApplyCoveredMandate(target, 42, [source]); Assert.Equal(13, child.FeePercent); Assert.Contains(child.EvidenceIssues, x => x.Contains("differ"));
    }

    [Fact]
    public void Same_wrap_two_day_switch_is_neutral_only_when_amounts_match_uniquely()
    {
        var doc = PerformanceFeeStatementReader.Read(PerformanceFeeStatementTests.Workbook(), "source.xlsx");
        var date = PerformanceFeeCalculatorTests.Start.AddDays(90);
        doc.Rows.AddRange([
            new() { Key = "out", Account = "SYNTHETIC-IA", Wrap = "KE1", Holding = "Synthetic holding (KE1)", Date = date, Kind = "InternalSwitch", Detail = "Switch Out", Amount = -100, Units = -100, UnitBalance = 99900 },
            new() { Key = "in", Account = "SYNTHETIC-IA", Wrap = "KE1", Holding = "Synthetic holding (KE1)", Date = date.AddDays(2), Kind = "InternalSwitch", Detail = "Switch In", Amount = 100, Units = 100, UnitBalance = 100000 }
        ]);
        var ledger = FeeStatementLedger.Merge([doc], "SYNTHETIC-IA");
        Assert.Equal(2, ledger.Rows.Count(x => x.Kind == "InternalRebalance"));
        var input = ledger.Prepare(PerformanceFeeCalculatorTests.Input(), null);
        Assert.DoesNotContain(input.Wraps[0].Input.EvidenceIssues, x => x.Contains("unmatched switch"));
        doc.Rows[^1].Amount = 110;
        Assert.DoesNotContain(FeeStatementLedger.Merge([doc], "SYNTHETIC-IA").Rows, x => x.Kind == "InternalRebalance");
    }

    [Fact]
    public void Cash_scope_allocation_is_protected_capital_not_performance_or_new_client_funding()
    {
        var input = PerformanceFeeCalculatorTests.Input(); input.Movements.Add(new() { Date = input.To!.Value, Kind = "ScopeFunding", Amount = 1000, Reference = "Synthetic matched cash allocation" });
        input.ClosingValue += 1000;
        var result = PerformanceFeeCalculator.Calculate(input, input.To.Value);
        Assert.True(result.IsReady); Assert.Equal(1950, result.FeeExVat); Assert.Equal(108000, result.Threshold);
    }
}
