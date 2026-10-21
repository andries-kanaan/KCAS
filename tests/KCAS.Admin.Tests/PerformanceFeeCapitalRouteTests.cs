using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class PerformanceFeeCapitalRouteTests
{
    private static readonly DateOnly Start = new(2021, 1, 1);

    [Fact]
    public void Delayed_tax_staging_merges_sources_and_retains_every_intermediate_row()
    {
        var rows = Exit("A", 60).Concat(Exit("B", 40)).Concat(Entry("C", 100, 10)).ToList();
        var routing = FeeCapitalRoutes.Build(rows);
        var route = Assert.Single(routing.Routes);
        Assert.Equal(2, route.Sources.Count); Assert.Equal(100, route.Targets.Sum(x => x.Amount));
        Assert.All(route.Sources, x => Assert.Equal(3, x.Keys.Length));
        Assert.All(route.Sources, x => Assert.Equal(Start.AddDays(2), x.ReleasedOn));
        Assert.Contains("A-tax", route.Reference); Assert.Contains("B-cash", route.Reference);
        Assert.Equal(route.Key, Assert.Single(FeeCapitalRoutes.Build(rows.Concat(rows)).Routes).Key);
    }

    [Fact]
    public void Delayed_split_is_unique_and_ambiguous_or_unbalanced_routes_are_not_guessed()
    {
        var rows = Exit("A", 100).Concat(Entry("B", 60, 10)).Concat(Entry("C", 40, 12)).ToList();
        var route = Assert.Single(FeeCapitalRoutes.Build(rows).Routes);
        Assert.Single(route.Sources); Assert.Equal(2, route.Targets.Count);
        Assert.Empty(FeeCapitalRoutes.Build(rows.Concat(Entry("D", 100, 10))).Routes);
        rows[^1].Amount = 39;
        var unbalanced = FeeCapitalRoutes.Build(rows);
        Assert.Empty(unbalanced.Routes);
        Assert.Contains(unbalanced.Traces, x => x.Account == "B" && x.Status.Contains("source/settlement"));
        Assert.Empty(FeeCapitalRoutes.Build(Exit("A", 100).Concat(Entry("B", 100, 1))).Routes);
    }

    [Fact]
    public void Cash_adjustments_are_not_ignored_to_force_a_staged_match()
    {
        var rows = Exit("A", 100).Concat(Entry("B", 100, 10)).ToList();
        rows.Add(Row("A", "ATXF", 1, -1, "expense", "Expense", "Administration fee"));
        Assert.Empty(FeeCapitalRoutes.Build(rows).Routes);
        Assert.Contains(FeeCapitalRoutes.Build(rows).Traces, x => x.Status.Contains("requires reconciliation"));
    }

    [Fact]
    public void Evidenced_staging_fee_reduces_cash_but_does_not_reset_transferred_protection()
    {
        var rows = Exit("A", 100).Concat(Entry("B", 98, 10)).ToList();
        var exit = rows.Single(x => x.Key == "A-cash"); exit.Amount = exit.Units = -98;
        var fee = Row("A", "ATXF", 1, -2, "actual-fee-row", "Expense", "Administration fee");
        fee.UnitBalance = 98; rows.Add(fee);
        var route = Assert.Single(FeeCapitalRoutes.Build(rows).Routes);
        Assert.Equal(100, route.Sources[0].Amount); Assert.Equal(98, route.Sources[0].SettledAmount);
        Assert.Contains("actual-fee-row", route.Sources[0].Keys);
        var (workspace, inputs, original) = Prepared();
        var adjusted = original with { Sources = [original.Sources[0] with { SettledAmount = 98000 }],
            Targets = original.Targets.Select(x => x with { Amount = x.Amount * 0.98m }).ToList() };
        workspace = workspace with { CapitalRouting = new([adjusted], []) };
        PerformanceFeeService.ApplyCapitalRoutes(workspace, inputs, [adjusted.Key], "Synthetic bespoke cross-fund continuity mandate", new(), mandateRequiresCrossWrapContinuity: true);
        Assert.Equal(98000, inputs["B"].Wraps.Sum(x => x.Input.OpeningValue));
        Assert.Equal(100000, inputs["B"].Wraps.Sum(x => x.Input.OpeningHighWaterMark));
    }

    [Fact]
    public void Partial_same_wrap_rebalance_reports_cash_residue_without_clearing_switches()
    {
        var rows = new List<FeeStatementRow>
        {
            Row("A", "KA6", 1, -100, "sell", "InternalSwitch", "Dr Wrap Rebalance Out"),
            Row("A", "CASH", 1, 100, "credit", "InternalSwitch", "Cr Wrap Rebalance Out"),
            Row("A", "CASH", 3, -99, "debit", "InternalSwitch", "Dr Wrap Rebalance In"),
            Row("A", "KA6", 3, 99, "buy", "InternalSwitch", "Cr Wrap Rebalance In")
        };
        var trace = Assert.Single(FeeCapitalRoutes.Build(rows).Traces);
        Assert.Contains("1.00 remains outside", trace.Status);
        Assert.All(rows, x => Assert.Equal("InternalSwitch", x.Kind));
        Assert.Empty(FeeCapitalRoutes.Build(rows).Routes);
    }

    [Fact]
    public void Protected_split_carries_each_source_threshold_and_grows_it_through_cash_staging()
    {
        var (workspace, inputs, route) = Prepared();
        PerformanceFeeService.ApplyCapitalRoutes(workspace, inputs, [route.Key], "Synthetic bespoke cross-fund continuity mandate", new(), mandateRequiresCrossWrapContinuity: true);
        var source = inputs["A"].Wraps.Single().Input;
        Assert.Single(source.Movements); Assert.Equal(100000, source.Movements[0].Amount);
        var targets = inputs["B"].Wraps.Select(x => x.Input).ToList();
        Assert.Equal(100000, targets.Sum(x => x.OpeningHighWaterMark));
        Assert.Equal(100000, targets.Sum(x => x.OpeningValue));
        Assert.All(targets, x => Assert.Empty(x.EvidenceIssues));
        var expected = 100000m * (decimal)Math.Pow(1.07, 15 / 365d);
        Assert.InRange(targets.Sum(x => x.OpeningBenchmark)!.Value, expected - 0.000001m, expected + 0.000001m);
        Assert.Equal(Start, inputs["B"].Inception);
        Assert.All(targets, x => Assert.Single(x.OpeningTransfers));
        Assert.Single(inputs["A"].AccountTransferDecisions);
        Assert.Single(inputs["B"].AccountTransferDecisions);
    }

    [Fact]
    public void Missing_protection_or_unselected_frozen_source_never_resets_receiving_capital()
    {
        var (workspace, inputs, route) = Prepared();
        inputs["A"].Wraps[0].Input.OpeningBenchmark = null;
        var before = FeeJson.Write(inputs["A"]);
        PerformanceFeeService.ApplyCapitalRoutes(workspace, inputs, [route.Key], "Synthetic bespoke cross-fund continuity mandate", new(), mandateRequiresCrossWrapContinuity: true);
        Assert.Equal(before, FeeJson.Write(inputs["A"]));
        Assert.All(inputs["B"].Wraps, x => { Assert.Equal(0, x.Input.OpeningValue); Assert.Empty(x.Input.OpeningTransfers); Assert.Contains(x.Input.EvidenceIssues, i => i.Contains("earlier AIMS/Glacier")); });
        (workspace, inputs, route) = Prepared(); inputs.Remove("A");
        PerformanceFeeService.ApplyCapitalRoutes(workspace, inputs, [route.Key], "Synthetic bespoke cross-fund continuity mandate", new(), mandateRequiresCrossWrapContinuity: true);
        Assert.All(inputs["B"].Wraps, x => Assert.Contains(x.Input.EvidenceIssues, i => i.Contains("frozen calculation")));
    }

    [Fact]
    public void Source_prior_fee_review_requirements_follow_carried_protection()
    {
        var (workspace, inputs, route) = Prepared();
        inputs["A"].Wraps[0].Input.NoPriorFeeConfirmed = false;
        PerformanceFeeService.ApplyCapitalRoutes(workspace, inputs, [route.Key], "Synthetic bespoke cross-fund continuity mandate", new(), mandateRequiresCrossWrapContinuity: true);
        Assert.All(inputs["B"].Wraps, x =>
        {
            Assert.Single(x.Input.OpeningTransfers);
            Assert.Contains(x.Input.ReviewIssues, i => i.Contains("no earlier performance fee"));
            Assert.False(PerformanceFeeCalculator.Calculate(x.Input, x.Input.To!.Value).IsReady);
        });
    }

    [Fact]
    public void Broken_staging_units_block_the_entire_route_without_consuming_source_capital()
    {
        var (workspace, inputs, route) = Prepared();
        workspace.Accounts[0].Ledger.Wraps.Add(new() { Code = "ATXF", Issues = ["Synthetic unit discontinuity"] });
        var original = FeeJson.Write(inputs["A"]);
        PerformanceFeeService.ApplyCapitalRoutes(workspace, inputs, [route.Key], "Synthetic bespoke cross-fund continuity mandate", new(), mandateRequiresCrossWrapContinuity: true);
        Assert.Equal(original, FeeJson.Write(inputs["A"]));
        Assert.All(inputs["B"].Wraps, x => Assert.Contains(x.Input.EvidenceIssues, i => i.Contains("staging units do not reconcile")));
    }

    private static (FeeClientWorkspace Workspace, Dictionary<string, FeePreparation> Inputs, FeeCapitalRoute Route) Prepared()
    {
        var source = PerformanceFeeCalculatorTests.Input();
        var outDate = Start.AddDays(10); var inDate = Start.AddDays(15);
        source.EvidenceIssues = [$"{outDate:yyyy-MM-dd}: unmatched switch crosses this wrap or settlement date. Verify transferred benchmark/HWM continuity; do not treat it as a fresh contribution."];
        FeePreparation Target()
        {
            var input = PerformanceFeeCalculatorTests.Input(); input.From = inDate; input.Inception = inDate;
            input.OpeningValue = input.OpeningBenchmark = input.OpeningHighWaterMark = 0;
            input.Benchmarks[0].From = inDate;
            return input;
        }
        var sourceRow = Row("A", "KA9", 10, -100000, "origin", "InternalSwitch", "Apply for Tax Switch Out");
        var route = new FeeCapitalRoute("route", [new("A", "KA9", outDate, 100000, 100000, [sourceRow.Key], sourceRow.Source)],
            [new("B", "KE1", inDate, 70000, null, ["target-1"], "Synthetic target row 1"),
             new("B", "KA6", inDate, 30000, null, ["target-2"], "Synthetic target row 2")], "Synthetic statement rows through staging");
        var workspace = new FeeClientWorkspace(new(), "version", [],
            [new("A", new(), null, new() { Rows = [sourceRow] }, null), new("B", new(), null, new(), null)], [])
            { CapitalRouting = new([route], []) };
        var inputs = new Dictionary<string, FeePreparation>
        {
            ["A"] = new() { Inception = Start, Wraps = [new() { Code = "KA9", Input = source }] },
            ["B"] = new() { Inception = inDate, Wraps = [new() { Code = "KE1", Input = Target() }, new() { Code = "KA6", Input = Target() }] }
        };
        return (workspace, inputs, route);
    }
    private static IEnumerable<FeeStatementRow> Exit(string account, decimal amount)
    {
        yield return Row(account, "KA9", -1, amount, account + "-open", "Opening", "Opening balance");
        yield return Row(account, "KA9", 0, -amount, account + "-out", "InternalSwitch", "Apply for Tax Switch Out");
        yield return Row(account, "ATXF", 1, amount, account + "-tax", "InternalSwitch", "Apply for Tax Switch In");
        yield return Row(account, "ATXF", 2, -amount, account + "-cash", "Transfer", "Transfer Rands - Out");
    }
    private static IEnumerable<FeeStatementRow> Entry(string account, decimal amount, int day)
    { yield return Row(account, "KE1", day, amount, account + "-in", "Transfer", "Transfer Rands - In"); }
    private static FeeStatementRow Row(string account, string wrap, int day, decimal amount, string key, string kind, string detail) =>
        new() { Account = account, Wrap = wrap, Date = Start.AddDays(day), Amount = amount, Units = amount,
            Price = 1, UnitBalance = kind == "Opening" || kind == "InternalSwitch" && amount > 0 ? amount : 0,
            Key = key, Source = key, Holding = wrap + " holding", Kind = kind, Detail = detail };
}
