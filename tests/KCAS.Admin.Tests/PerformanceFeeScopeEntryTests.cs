using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class PerformanceFeeScopeEntryTests
{
    private static readonly DateOnly Start = new(2020, 1, 1);
    private static readonly DateOnly Exit = new(2022, 2, 17);
    private static readonly DateOnly Entry = new(2022, 2, 21);
    private static readonly DateOnly End = new(2023, 12, 31);

    [Fact]
    public void Existing_legacy_holding_does_not_hide_the_supplied_zero_unit_fee_wrap_beginning()
    {
        var ledger = FeeStatementLedger.Merge([Document()], "SYNTHETIC");
        Assert.Empty(ledger.Issues); Assert.Empty(ledger.History.Issues);
        Assert.False(ledger.History.BeginningCovered);
        Assert.True(ledger.History.FeeScopeBeginningCovered);
        var entry = Assert.Single(ledger.History.FeeScopeEntries);
        Assert.Equal(Entry, entry.Date); Assert.Equal(100000, entry.Amount);
        Assert.Equal("KA9", entry.Wrap); Assert.Equal(["legacy-out"], entry.SourceKeys);
        Assert.Null(FeeEarlierHistoryNotice.From("SYNTHETIC", ledger));
        var old = PerformanceFeeCalculatorTests.Input(); old.Inception = new(2010, 1, 1); old.NoPriorFeeConfirmed = false;
        var input = ledger.Prepare(old, null);
        Assert.Equal(Entry, input.Inception); Assert.Equal(Entry, input.From);
        var wrap = Assert.Single(input.Wraps).Input;
        Assert.Equal(100000, wrap.OpeningValue); Assert.Equal(100000, wrap.OpeningBenchmark);
        Assert.Empty(wrap.Movements); Assert.Empty(wrap.EvidenceIssues);
        Assert.True(wrap.NoPriorFeeConfirmed);
        Assert.False(Assert.Single(input.ReviewPoints).Resolved);
        wrap.Benchmarks = [new() { From = Entry, To = End, CpiPercent = 4, Reference = "Synthetic CPI" }];
        var result = PerformanceFeeCalculator.Calculate(input, End);
        Assert.True(result.HasCalculation); Assert.False(result.IsReady);
        Assert.Contains(result.Issues, x => x.Contains("separate fund fee-period"));
    }

    [Theory]
    [InlineData("positive-wrap-opening")]
    [InlineData("unmatched-amount")]
    [InlineData("fee-bearing-source")]
    [InlineData("ambiguous-source")]
    [InlineData("missing-zero-opening")]
    public void An_unproven_or_existing_fee_scope_does_not_gain_a_new_market_value_baseline(string failure)
    {
        var doc = Document();
        switch (failure)
        {
            case "positive-wrap-opening": doc.Rows.Single(x => x.Key == "wrap-open").Amount = 1; break;
            case "unmatched-amount": doc.Rows.Single(x => x.Key == "legacy-out").Amount = -99999; break;
            case "fee-bearing-source": foreach (var row in doc.Rows.Where(x => x.Wrap == "IND")) row.Wrap = "KA6"; break;
            case "ambiguous-source":
                var second = FeeJson.Read<FeeStatementRow>(FeeJson.Write(doc.Rows.Single(x => x.Key == "legacy-out")));
                second.Key = "second-out"; second.Date = Exit.AddDays(1); doc.Rows.Add(second); break;
            case "missing-zero-opening": doc.Rows.RemoveAll(x => x.Key == "wrap-open"); break;
        }
        var ledger = FeeStatementLedger.Merge([doc], "SYNTHETIC");
        Assert.False(ledger.History.FeeScopeBeginningCovered);
        Assert.NotNull(FeeEarlierHistoryNotice.From("SYNTHETIC", ledger));
        Assert.Null(ledger.Prepare(PerformanceFeeCalculatorTests.Input(), null).Inception);
    }

    [Fact]
    public void A_saved_KI_scope_reconciliation_survives_an_unchanged_statement_rebuild()
    {
        var ledger = FeeStatementLedger.Merge([Document()], "SYNTHETIC");
        var input = ledger.Prepare(PerformanceFeeCalculatorTests.Input(), null);
        var point = Assert.Single(input.ReviewPoints);
        point.ResolvedBy = "Synthetic KI"; point.ResolvedAtUtc = DateTime.UtcNow; point.ResolutionReference = "Synthetic signed scope decision";
        var rebuilt = ledger.Prepare(input, null);
        Assert.True(Assert.Single(rebuilt.ReviewPoints).Resolved);
    }

    internal static FeeStatementDocument Document()
    {
        FeeStatementRow Row(string key, string wrap, string kind, DateOnly date, decimal amount, decimal units, decimal balance) => new()
        {
            Key = key, Account = "SYNTHETIC", Wrap = wrap, Kind = kind, Date = date, Amount = amount,
            Units = units, UnitBalance = balance, Price = 1, Holding = wrap + " holding", Source = "Synthetic statement " + key
        };
        return new()
        {
            FileName = "synthetic-complete-wrap-entry.xlsx", Sha256 = "synthetic",
            Periods = [new() { Account = "SYNTHETIC", From = Start, To = End, OpeningValue = 100000,
                ClosingValue = 120000, Entries = [new("Market movement", 20000, "Synthetic statement summary")] }],
            Rows = [Row("legacy-open", "IND", "Opening", Start, 100000, 0, 100000),
                Row("wrap-open", "KA9", "Opening", Start, 0, 0, 0),
                Row("legacy-out", "IND", "InternalSwitch", Exit, -100000, -100000, 0),
                Row("wrap-in", "KA9", "InternalSwitch", Entry, 100000, 100000, 100000),
                Row("legacy-close", "IND", "Closing", End, 0, 0, 0),
                Row("wrap-close", "KA9", "Closing", End, 120000, 0, 100000)]
        };
    }
}
