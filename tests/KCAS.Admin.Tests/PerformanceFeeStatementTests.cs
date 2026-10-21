using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class PerformanceFeeStatementTests
{
    [Fact]
    public void Two_periods_preserve_boundaries_without_double_counting_and_show_unit_gaps()
    {
        var first = PerformanceFeeStatementReader.Read(Workbook(), "first.xlsx");
        var second = FeeJson.Read<FeeStatementDocument>(FeeJson.Write(first));
        second.FileName = "second.xlsx"; second.Sha256 = "different";
        second.Rows[0].Date = new(2022, 1, 1); second.Rows[0].UnitBalance = 101000; second.Rows[0].Amount = 121200; second.Rows[0].Key = "new-opening";
        second.Rows[1].Date = new(2022, 7, 1); second.Rows[1].UnitBalance = 102000; second.Rows[1].Key = "new-reinvestment";
        second.Rows[2].Date = new(2023, 1, 1); second.Rows[2].UnitBalance = 102000; second.Rows[2].Amount = 122400; second.Rows[2].Key = "new-closing";
        var ledger = FeeStatementLedger.Merge([first, second], "SYNTHETIC-IA");
        Assert.Empty(ledger.Wraps.Single().Issues); Assert.Equal(122400, ledger.Wraps.Single().ClosingValue);
        second.Rows[0].UnitBalance += 1;
        Assert.Contains(FeeStatementLedger.Merge([first, second], "SYNTHETIC-IA").Wraps.Single().Issues, x => x.Contains("continuity gap"));
        second.Rows[0].UnitBalance -= 1; second.Rows[0].Date = new(2022, 2, 1);
        Assert.Contains(FeeStatementLedger.Merge([first, second], "SYNTHETIC-IA").Issues, x => x.Contains("coverage gap"));
    }

    [Fact]
    public void Account_transfer_match_requires_unique_date_holding_units_and_amounts()
    {
        var date = new DateOnly(2024, 1, 1);
        var rows = new List<FeeStatementRow>
        {
            new() { Key = "out", Account = "A", Holding = "Fund (KE1)", Wrap = "KE1", Date = date, Kind = "Transfer", Units = -10, Amount = -100 },
            new() { Key = "in", Account = "B", Holding = "Fund (KE1)", Wrap = "KE1", Date = date, Kind = "Transfer", Units = 10, Amount = 100 }
        };
        Assert.Single(FeeAccountTransferMatcher.Match(rows));
        rows.Add(new() { Key = "ambiguous", Account = "C", Holding = "Fund (KE1)", Wrap = "KE1", Date = date, Kind = "Transfer", Units = 10, Amount = 100 });
        Assert.Empty(FeeAccountTransferMatcher.Match(rows));
        rows.RemoveAt(2); rows[1].Date = date.AddDays(1); Assert.Empty(FeeAccountTransferMatcher.Match(rows));
    }

    [Fact]
    public void Full_exit_uses_settled_value_within_printed_price_rounding_but_not_a_material_difference()
    {
        var date = new DateOnly(2024, 1, 1);
        var rows = new List<FeeStatementRow>
        {
            new() { Key = "opening", Account = "A", Holding = "Fund (KE1)", Wrap = "KE1", Date = date.AddDays(-1), Kind = "Opening", UnitBalance = 10000, Amount = 10000 },
            new() { Key = "out", Account = "A", Holding = "Fund (KE1)", Wrap = "KE1", Date = date, Kind = "Transfer", Units = -10000, UnitBalance = 0, Price = 1.000001m, Amount = -10000.02m },
            new() { Key = "in", Account = "B", Holding = "Fund (KE1)", Wrap = "KE1", Date = date, Kind = "Transfer", Units = 10000, UnitBalance = 10000, Amount = 10000.02m }
        };
        Assert.Equal(10000.02m, Assert.Single(FeeAccountTransferMatcher.Match(rows)).BeforeValue);
        rows[1].Amount = -11000; rows[2].Amount = 11000;
        Assert.Equal(10000.01m, Assert.Single(FeeAccountTransferMatcher.Match(rows)).BeforeValue);
    }

    [Fact]
    public void Verified_cross_account_pair_carries_protection_instead_of_becoming_a_new_contribution()
    {
        var source = PerformanceFeeCalculatorTests.Input(); source.ClosingValue = 60000;
        var target = PerformanceFeeCalculatorTests.Input(); target.OpeningValue = target.OpeningBenchmark = target.OpeningHighWaterMark = 0; target.ClosingValue = 60000;
        var outgoing = new FeeStatementRow { Key = "out", Account = "A", Holding = "Fund (KE1)", Wrap = "KE1", Date = source.To!.Value, Kind = "Transfer", Detail = "Transfer Units Out Internal", Units = -50000, UnitBalance = 50000, Price = 1.2m, Amount = -60000, Source = "Synthetic statement row 1" };
        var incoming = FeeJson.Read<FeeStatementRow>(FeeJson.Write(outgoing)); incoming.Key = "in"; incoming.Account = "B"; incoming.Units = 50000; incoming.Amount = 60000; incoming.Detail = "Transfer Units In Internal";
        source.EvidenceIssues.Add($"{outgoing.Date:yyyy-MM-dd} Transfer: review {outgoing.Detail} ({outgoing.Amount:N2}) at {outgoing.Source}.");
        target.EvidenceIssues.Add($"{incoming.Date:yyyy-MM-dd} Transfer: review {incoming.Detail} ({incoming.Amount:N2}) at {incoming.Source}.");
        var inputs = new Dictionary<string, FeePreparation> { ["A"] = new() { Wraps = [new() { Code = "KE1", Input = source }] }, ["B"] = new() { Wraps = [new() { Code = "KE1", Input = target }] } };
        var transfer = new FeeAccountTransfer("match", "A", "B", "KE1", "KE1", outgoing.Date, 60000, 120000, "Synthetic matched units", ["out"], ["in"]);
        var workspace = new FeeClientWorkspace(new(), "version", [],
            [new("A", null, null, new() { Rows = [outgoing] }, null), new("B", null, null, new() { Rows = [incoming] }, null)], [transfer]);
        PerformanceFeeService.ApplyAccountTransfers(workspace, inputs, [], "", new());
        Assert.Empty(target.Movements); Assert.NotEmpty(target.EvidenceIssues);
        PerformanceFeeService.ApplyAccountTransfers(workspace, inputs, ["match"], "Synthetic signed continuity", new() { Email = "synthetic@example.test" });
        Assert.Empty(source.EvidenceIssues); Assert.Empty(target.EvidenceIssues);
        var move = Assert.Single(target.Movements); Assert.Equal("TransferIn", move.Kind);
        Assert.Equal(53500m, move.TransferredBenchmark); Assert.Equal(50000m, move.TransferredHighWaterMark);
        Assert.Equal(60000m, source.Movements.Single().Amount);
        Assert.True(PerformanceFeeCalculator.Calculate(source, source.To.Value).IsReady);
        Assert.True(PerformanceFeeCalculator.Calculate(target, target.To!.Value).IsReady);
    }
    [Theory]
    [InlineData("Performance Driven Fee", "PerformanceFee")]
    [InlineData("PDF deduction", "PerformanceFee")]
    [InlineData("Prestasiefooi", "PerformanceFee")]
    [InlineData("Wrap management fee", "Expense")]
    public void Distinguishes_actual_performance_fee_descriptions_from_ordinary_wrap_charges(string description, string expected)
        => Assert.Equal(expected, FeeStatementKinds.Classify(description));

    internal static byte[] Workbook(bool af = false, bool shared = false, string account = "SYNTHETIC-IA", bool formula = false, int shift = 0, string holding = "Synthetic holding (KE1)")
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var strings = new List<string>();
        XElement Cell(int column, int row, string text, bool numeric = false)
        {
            var col = ""; for (var n = column + shift; n > 0; n = (n - 1) / 26) col = (char)('A' + (n - 1) % 26) + col;
            var cell = new XElement(ns + "c", new XAttribute("r", col + row));
            if (numeric) cell.Add(new XElement(ns + "v", text));
            else if (shared) { strings.Add(text); cell.Add(new XAttribute("t", "s"), new XElement(ns + "v", strings.Count - 1)); }
            else cell.Add(new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", text)));
            return cell;
        }
        XElement Row(int row, params string[] values) => new(ns + "row", new XAttribute("r", row), values.Select((x, i) => Cell(i + 1, row, x, i == 0 && decimal.TryParse(x, out _))));
        var data = new XElement(ns + "sheetData",
            Row(1, af ? "Gedetailleerde Transaksiestaat - Absa" : "Detailed Transaction Statement - Absa"),
            Row(2, (af ? "Polisno : " : "Policy No : ") + account),
            Row(3, af ? "Datum" : "Date", af ? "Besonderhede" : "Detail", af ? "Eenheids-prys" : "Unit Price", af ? "Eenhede" : "Units", af ? "Eenheidsbalans" : "Unit Balance", af ? "Markwaarde" : "Market Value"),
            Row(4, "", holding),
            Row(5, "44197", af ? "Openingsbalans" : "Opening Balance", "1,0000", "0,0000", "100 000,0000", "100 000,00"),
            Row(6, "44378", af ? "Herbelegging" : "Reinvestment", "1,0000", "1 000,0000", "101 000,0000", "1 000,00"),
            Row(7, "44562", af ? "Sluitingsbalans" : "Closing Balance", "1,2000", "0,0000", "101 000,0000", "121 200,00"));
        if (formula) data.Elements().ElementAt(5).Elements().Last().Add(new XElement(ns + "f", "1+1"));
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            void Part(string name, string text) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
            Part("xl/workbook.xml", $"<workbook xmlns='{ns}' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Statement' sheetId='1' r:id='r1'/></sheets></workbook>");
            Part("xl/_rels/workbook.xml.rels", "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='r1' Target='worksheets/sheet1.xml'/></Relationships>");
            Part("xl/worksheets/sheet1.xml", new XElement(ns + "worksheet", data).ToString());
            if (shared) Part("xl/sharedStrings.xml", new XElement(ns + "sst", strings.Select(x => new XElement(ns + "si", new XElement(ns + "t", x)))).ToString());
        }
        return output.ToArray();
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 12)]
    [InlineData(false, true, 31)]
    [InlineData(true, false, 5)]
    public void Reads_shifted_bilingual_shared_and_inline_original_values(bool af, bool shared, int shift)
    {
        var document = PerformanceFeeStatementReader.Read(Workbook(af, shared, shift: shift), "original.XLSX");
        Assert.Equal(3, document.Rows.Count);
        Assert.All(document.Rows, x => { Assert.Equal("SYNTHETIC-IA", x.Account); Assert.Equal("KE1", x.Wrap); });
        Assert.Equal("Reinvestment", document.Rows[1].Kind);
        Assert.Equal(121200m, document.Rows[^1].Amount);
        Assert.Equal(new DateOnly(2022, 1, 1), document.Rows[^1].Date);
        var ledger = FeeStatementLedger.Merge([document, document], "SYNTHETIC-IA");
        Assert.Equal(3, ledger.Rows.Count); Assert.Equal(3, ledger.DuplicateRows); Assert.Empty(Assert.Single(ledger.Wraps).Issues);
        var input = PerformanceFeeCalculatorTests.Input();
        input.ClosingValue = null; input.Movements.Clear();
        var prepared = ledger.Prepare(input, null);
        var wrap = Assert.Single(prepared.Wraps);
        Assert.Empty(wrap.Input.Movements); // Retained investment income is not new client funding.
        Assert.Equal(121200m, wrap.Input.ClosingValue);
        Assert.Equal(100000m, wrap.Input.OpeningValue);
    }

    [Fact]
    public void Afrikaans_cash_scope_is_the_same_cash_scope_not_a_chargeable_wrap()
    {
        var doc = PerformanceFeeStatementReader.Read(Workbook(af: true, holding: "Kliëntkontantbeweging (KONTANT)"), "af.xlsx");
        Assert.All(doc.Rows, x => Assert.Equal("CASH", x.Wrap));
    }

    [Fact]
    public void Rejects_formulas_macros_and_invalid_files_without_evaluating_them()
    {
        Assert.Throws<ValidationException>(() => PerformanceFeeStatementReader.Read(Workbook(formula: true), "calculation.xlsx"));
        Assert.Throws<ValidationException>(() => PerformanceFeeStatementReader.Read(Workbook(), "engine.xlsm"));
        Assert.Throws<ValidationException>(() => PerformanceFeeStatementReader.Read([1, 2, 3], "bad.xlsx"));
    }

    [Fact]
    public void Does_not_reset_a_historical_opening_market_value_as_a_new_HWM()
    {
        var doc = PerformanceFeeStatementReader.Read(Workbook(), "original.xlsx");
        var input = PerformanceFeeCalculatorTests.Input(); input.Inception = input.From!.Value.AddYears(-2);
        var prepared = FeeStatementLedger.Merge([doc], "SYNTHETIC-IA").Prepare(input, null);
        Assert.Null(prepared.Wraps[0].Input.OpeningBenchmark);
        Assert.Contains(prepared.Wraps[0].Input.EvidenceIssues, x => x.Contains("after inception"));
    }

    [Fact]
    public void Unit_gaps_conflicting_rows_and_unrecognised_transactions_stay_visible()
    {
        var doc = PerformanceFeeStatementReader.Read(Workbook(), "original.xlsx");
        doc.Rows[1].UnitBalance += 10;
        doc.Rows[1].Kind = "Unknown";
        doc.Rows[1].Detail = "Unrecognised administrator movement";
        var ledger = FeeStatementLedger.Merge([doc], "SYNTHETIC-IA");
        Assert.NotEmpty(ledger.Wraps[0].Issues);
        var input = ledger.Prepare(PerformanceFeeCalculatorTests.Input(), null);
        Assert.False(input.Wraps[0].Input.CashFlowsComplete);
        Assert.Contains(input.Wraps[0].Input.EvidenceIssues, x => x.Contains("Unknown"));
    }

    [Fact]
    public void Matched_cash_allocations_are_separate_scope_funding_and_prior_fees_still_require_a_checkpoint()
    {
        var doc = PerformanceFeeStatementReader.Read(Workbook(), "original.xlsx");
        var date = PerformanceFeeCalculatorTests.Start.AddDays(180);
        doc.Rows.Add(new() { Key = "sweep-source", Account = "SYNTHETIC-IA", Wrap = "CASH", Holding = "Cash (CASH)", Date = date, Kind = "Transfer", Detail = "Dr Cash Sweep", Amount = -100 });
        doc.Rows.Add(new() { Key = "sweep-target", Account = "SYNTHETIC-IA", Wrap = "KE1", Holding = "Synthetic holding (KE1)", Date = date, Kind = "Transfer", Detail = "Cr Cash Sweep", Amount = 100 });
        doc.Rows.Add(new() { Key = "prior-pdf", Account = "SYNTHETIC-IA", Wrap = "KE1", Holding = "Synthetic holding (KE1)", Date = date.AddDays(1), Kind = "PerformanceFee", Detail = "PDF deduction", Amount = -10 });
        var ledger = FeeStatementLedger.Merge([doc], "SYNTHETIC-IA");
        Assert.Single(ledger.Rows, x => x.Kind == "IncomeTransfer");
        Assert.Single(ledger.Rows, x => x.Kind == "ScopeFunding");
        var input = ledger.Prepare(PerformanceFeeCalculatorTests.Input(), null);
        Assert.DoesNotContain(input.Wraps[0].Input.EvidenceIssues, x => x.Contains("pooled-cash"));
        Assert.Contains(input.Wraps[0].Input.EvidenceIssues, x => x.Contains("PerformanceFee"));
        Assert.Equal("ScopeFunding", Assert.Single(input.Wraps[0].Input.Movements).Kind);
    }

    [Fact]
    public void Independent_wraps_do_not_offset_losses_or_reset_zero_fee_protection()
    {
        var root = PerformanceFeeCalculatorTests.Input();
        var gain = PerformanceFeeCalculatorTests.Input(); var loss = PerformanceFeeCalculatorTests.Input(); loss.ClosingValue = 80000;
        root.Wraps = [new() { Code = "KE1", Input = gain }, new() { Code = "KA6", Input = loss }];
        var result = PerformanceFeeCalculator.Calculate(root, root.To!.Value.AddYears(1));
        Assert.True(result.IsReady); Assert.Equal(1950m, result.FeeExVat);
        Assert.Equal(107000m, result.NextCheckpoint!.Wraps.Single(x => x.Code == "KA6").Checkpoint.Benchmark);
        Assert.Equal(80000m, result.NextCheckpoint.Wraps.Single(x => x.Code == "KA6").Checkpoint.Value);
        Assert.Equal(2, result.Wraps.Count);
        root.Wraps[1].Code = "KE1";
        Assert.False(PerformanceFeeCalculator.Calculate(root, root.To.Value.AddYears(1)).IsReady);
    }

    [Fact]
    public void Matched_different_wrap_switch_preserves_source_remainder_but_starts_the_receiving_fund_at_actual_capital()
    {
        var root = PerformanceFeeCalculatorTests.Input();
        var source = PerformanceFeeCalculatorTests.Input(); source.ClosingValue = 60000;
        var target = PerformanceFeeCalculatorTests.Input(); target.OpeningValue = target.OpeningBenchmark = target.OpeningHighWaterMark = 0; target.ClosingValue = 60000;
        root.Wraps = [new() { Code = "KE1", Input = source }, new() { Code = "KA6", Input = target }];
        root.Transfers = [new() { Date = root.To!.Value, FromWrap = "KE1", ToWrap = "KA6", Amount = 60000, ValueBeforeTransfer = 120000, ContinuityConfirmed = true, Reference = "Synthetic matched switch" }];
        var result = PerformanceFeeCalculator.Calculate(root, root.To.Value.AddYears(1));
        Assert.True(result.IsReady, string.Join("; ", result.Issues));
        Assert.Equal(53500m, result.Wraps.Single(x => x.Code == "KE1").Result.Benchmark);
        Assert.Equal(50000m, result.Wraps.Single(x => x.Code == "KE1").Result.HighWaterMark);
        Assert.Equal(60000m, result.Wraps.Single(x => x.Code == "KA6").Result.Benchmark);
        Assert.Equal(60000m, result.Wraps.Single(x => x.Code == "KA6").Result.HighWaterMark);
        Assert.Equal(975m, result.FeeExVat);
        root.Transfers[0].ContinuityConfirmed = false;
        Assert.False(PerformanceFeeCalculator.Calculate(root, root.To.Value.AddYears(1)).IsReady);
    }
}
