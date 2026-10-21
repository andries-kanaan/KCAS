using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class PerformanceFeeFundAllocationTests
{
    [Fact]
    public void An_outgoing_row_cannot_disappear_by_being_classified_as_a_new_fund_allocation()
    {
        var doc = PerformanceFeeStatementReader.Read(PerformanceFeeStatementTests.Workbook(), "synthetic.xlsx");
        doc.Rows[1].Kind = "FundAllocation"; doc.Rows[1].Amount = -10;
        var input = FeeStatementLedger.Merge([doc], "SYNTHETIC-IA").Prepare(PerformanceFeeCalculatorTests.Input(), null);
        Assert.Contains(input.Wraps[0].Input.EvidenceIssues, x => x.Contains("positive evidenced receipt"));
        Assert.False(PerformanceFeeCalculator.Calculate(input, input.To!.Value).HasCalculation);
    }

    [Fact]
    public void Different_wrap_allocation_starts_at_receiving_capital_not_the_old_funds_threshold()
    {
        var source = PerformanceFeeCalculatorTests.Input();
        source.Inception = source.Inception!.Value.AddYears(-1);
        source.OpeningBenchmark = 140000; source.OpeningHighWaterMark = 130000;
        source.ClosingValue = 0;
        var target = PerformanceFeeCalculatorTests.Input();
        target.OpeningValue = target.OpeningBenchmark = target.OpeningHighWaterMark = 0;
        target.Inception = source.Inception;
        target.ClosingValue = 100000;
        var input = new FeePreparation { Inception = source.Inception, From = source.From, To = source.To,
            Wraps = [new() { Code = "KA9", Input = source }, new() { Code = "KE1", Input = target }],
            Transfers = [new() { FromWrap = "KA9", ToWrap = "KE1", Date = source.To!.Value, Amount = 100000,
                ValueBeforeTransfer = 100000, ContinuityConfirmed = true, Reference = "Synthetic evidenced change of fund" }] };
        var result = PerformanceFeeWrapCalculator.Calculate(input, input.To!.Value);
        Assert.True(result.HasCalculation, string.Join("; ", result.CalculationIssues));
        Assert.Equal(100000, result.Wraps.Single(x => x.Code == "KE1").Result.Threshold);
        Assert.Equal(0, result.Wraps.Single(x => x.Code == "KA9").Result.Threshold);
        Assert.Equal(0, result.FeeExVat);
    }

    [Fact]
    public void Tax_stage_is_a_verified_fund_exit_without_needing_the_later_recipient()
    {
        var date = new DateOnly(2024, 1, 1);
        var rows = new List<FeeStatementRow> {
            new() { Wrap = "KA9", Date = date, Amount = -100, Kind = "InternalSwitch", Detail = "Apply for Tax Switch Out" },
            new() { Wrap = "ATXF", Date = date.AddDays(2), Amount = 100, Kind = "InternalSwitch", Detail = "Apply for Tax Switch In" } };
        FeeStatementAutomation.Normalise(rows);
        Assert.Equal("ScopeWithdrawal", rows[0].Kind);
        rows[0].Kind = "InternalSwitch"; rows[1].Amount = 99;
        FeeStatementAutomation.Normalise(rows);
        Assert.Equal("InternalSwitch", rows[0].Kind);
    }

    [Fact]
    public void Same_wrap_cash_rebalance_preserves_returned_capital_and_records_only_the_residue()
    {
        var date = new DateOnly(2024, 1, 1);
        FeeStatementRow Row(string wrap, int days, decimal amount, string detail) => new() {
            Wrap = wrap, Date = date.AddDays(days), Amount = amount, Kind = "InternalSwitch", Detail = detail };
        var rows = new List<FeeStatementRow> { Row("KA6", 0, -100, "Dr Wrap Rebalance Out"),
            Row("CASH", 0, 100, "Cr Wrap Rebalance Out"), Row("KA6", 2, 99, "Cr Wrap Rebalance In"),
            Row("CASH", 2, -99, "Dr Wrap Rebalance In") };
        FeeStatementAutomation.Normalise(rows);
        Assert.All(rows.Where(x => x.Wrap == "KA6"), x => Assert.Equal("InternalRebalance", x.Kind));
        Assert.Equal(1, Assert.Single(FeeStatementAutomation.CashRebalances(rows)).Residual);
        rows[3].Amount = -98; rows[0].Kind = rows[2].Kind = "InternalSwitch";
        FeeStatementAutomation.Normalise(rows);
        Assert.Empty(FeeStatementAutomation.CashRebalances(rows));
        Assert.All(rows.Where(x => x.Wrap == "KA6"), x => Assert.Equal("InternalSwitch", x.Kind));
    }

    [Fact]
    public void Missing_rebalance_price_retains_all_protection_instead_of_inventing_a_valuation()
    {
        var input = PerformanceFeeCalculatorTests.Input(); var start = input.From!.Value;
        FeeStatementRow Row(string holding, int day, string kind, decimal amount, decimal units, decimal balance, string detail, decimal price = 1) => new() {
            Holding = holding, Wrap = holding == "cash" ? "CASH" : "KA6", Date = start.AddDays(day), Kind = kind,
            Amount = amount, Units = units, UnitBalance = balance, Detail = detail, Price = price, Source = "Synthetic " + holding + day, SourceRow = day };
        var rows = new List<FeeStatementRow> { Row("A", 0, "Opening", 100, 0, 100, "Opening"), Row("B", 0, "Opening", 100, 0, 100, "Opening"),
            Row("A", 10, "InternalSwitch", -100, -100, 0, "Dr Wrap Rebalance Out"), Row("cash", 10, "InternalSwitch", 100, 100, 100, "Cr Wrap Rebalance Out"),
            Row("A", 12, "InternalSwitch", 99, 100, 100, "Cr Wrap Rebalance In", .99m), Row("cash", 12, "InternalSwitch", -99, -99, 1, "Dr Wrap Rebalance In") };
        rows.Add(Row("A", 365, "Closing", 120, 0, 100, "Closing", 1.2m));
        rows.Add(Row("B", 365, "Closing", 120, 0, 100, "Closing", 1.2m));
        FeeStatementAutomation.Normalise(rows);
        var ledger = new FeeStatementLedger { Rows = rows, History = new() { BeginningCovered = true, FirstFundingDate = start },
            Wraps = [new() { Code = "KA6", Rows = rows.Where(x => x.Wrap == "KA6").ToList(), ClosingDate = input.To, ClosingValue = 240 }] };
        var conservative = ledger.Prepare(input, null);
        var child = Assert.Single(conservative.Wraps).Input;
        Assert.Empty(child.Movements);
        Assert.Contains(child.AutomationNotes, x => x.Contains("conservatively retain all"));
        var result = PerformanceFeeCalculator.Calculate(conservative, input.To!.Value);
        Assert.True(result.HasCalculation, string.Join("; ", result.CalculationIssues));
        Assert.Equal(214, result.Threshold);
        var priceRow = Row("B", 10, "Expense", 0, 0, 100, "Synthetic same-date price");
        rows.Add(priceRow); ledger.Wraps[0].Rows.Add(priceRow);
        var precise = ledger.Prepare(input, null);
        Assert.Equal(200, precise.Wraps[0].Input.Movements.Single().ValueBeforeWithdrawal);
        Assert.True(result.FeeExVat <= PerformanceFeeCalculator.Calculate(precise, input.To.Value).FeeExVat);
    }
}
