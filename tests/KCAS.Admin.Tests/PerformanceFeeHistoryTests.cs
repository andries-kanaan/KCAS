using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class PerformanceFeeHistoryTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 14)]
    public void Account_summaries_prove_the_covered_beginning_and_actual_funding_not_the_statement_start(bool af, bool shared, int shift)
    {
        var doc = PerformanceFeeStatementReader.Read(SummaryWorkbook(af, shared, shift, zero: true), "source.xlsx");
        var summary = Assert.Single(doc.Periods);
        Assert.Equal(0, summary.OpeningValue); Assert.Equal(120000m, summary.ClosingValue);
        Assert.Equal(new DateOnly(2021, 1, 1), summary.From);
        var ledger = FeeStatementLedger.Merge([doc], "SYNTHETIC-IA");
        Assert.Empty(ledger.History.Issues);
        Assert.True(ledger.History.BeginningCovered);
        Assert.Equal(new DateOnly(2021, 7, 1), ledger.History.FirstFundingDate);
        var existing = PerformanceFeeCalculatorTests.Input(); existing.Inception = new(2000, 1, 1);
        var input = ledger.Prepare(existing, null);
        Assert.Equal(ledger.History.FirstFundingDate, input.Inception);
        Assert.Equal(input.Inception, input.From);
        Assert.Equal(100000m, input.Wraps.Single().Input.OpeningValue);
        Assert.Equal(100000m, input.Wraps.Single().Input.OpeningHighWaterMark);
        Assert.True(input.Wraps.Single().Input.NoPriorFeeConfirmed);
    }

    [Fact]
    public void Positive_account_opening_requests_earlier_AIMS_history_not_an_original_KCAS_date_or_zero_underlying()
    {
        var doc = PerformanceFeeStatementReader.Read(SummaryWorkbook(), "later.xlsx");
        var ledger = FeeStatementLedger.Merge([doc], "SYNTHETIC-IA");
        Assert.False(ledger.History.BeginningCovered);
        Assert.Contains(ledger.History.Issues, x => x.Contains("before 2021-01-01") && x.Contains("AIMS/Glacier"));
        var old = PerformanceFeeCalculatorTests.Input(); old.Inception = new(2010, 1, 1);
        var input = ledger.Prepare(old, null);
        Assert.Null(input.Inception);
        Assert.Contains(input.EvidenceIssues, x => x.Contains("Earlier AIMS/Glacier"));
        Assert.False(PerformanceFeeCalculator.Calculate(input, new(2026, 1, 1)).CanShowCalculation);
    }

    [Fact]
    public void Summary_amount_conflicts_and_missing_months_remain_visible_even_with_continuous_units()
    {
        var first = PerformanceFeeStatementReader.Read(SummaryWorkbook(), "first.xlsx");
        var second = FeeJson.Read<FeeStatementDocument>(FeeJson.Write(first)); second.FileName = "second.xlsx"; second.Sha256 = "second";
        var p = second.Periods.Single(); p.From = new(2022, 2, 1); p.To = new(2023, 1, 1);
        foreach (var r in second.Rows) { r.Date = r.Kind == "Opening" ? p.From : p.To.Value; r.Key += "second"; }
        var merged = FeeStatementLedger.Merge([first, second], "SYNTHETIC-IA");
        Assert.Equal("Missing statement period", merged.History.Status);
        Assert.Contains(merged.Issues, x => x.Contains("2022-01-02 to 2022-01-31"));
        first.Periods[0].ClosingValue += 100;
        var conflicting = FeeStatementLedger.Merge([first], "SYNTHETIC-IA");
        Assert.Contains(conflicting.History.Issues, x => x.Contains("summary closing"));
        Assert.Contains(conflicting.History.Issues, x => x.Contains("signed movements"));
    }

    [Fact]
    public void Unique_cash_transfers_match_different_underlyings_but_ambiguous_amounts_do_not()
    {
        var date = new DateOnly(2024, 1, 1);
        var rows = new List<FeeStatementRow>
        {
            new() { Key = "out", Account = "A", Wrap = "KE1", Holding = "Old underlying (KE1)", Date = date, Kind = "Transfer", Detail = "Transfer Rands - Out", Amount = -100, Units = -10 },
            new() { Key = "in", Account = "B", Wrap = "KE1", Holding = "New underlying (KE1)", Date = date, Kind = "Transfer", Detail = "Transfer Rands - In", Amount = 100, Units = 20 }
        };
        Assert.Single(FeeAccountTransferMatcher.Match(rows));
        var duplicate = FeeJson.Read<FeeStatementRow>(FeeJson.Write(rows[1])); duplicate.Key = "other"; duplicate.Account = "C"; rows.Add(duplicate);
        Assert.Empty(FeeAccountTransferMatcher.Match(rows));
    }

    [Fact]
    public void Receiving_account_initial_transfer_inherits_HWM_and_administrator_anniversary()
    {
        var source = PerformanceFeeCalculatorTests.Input(); source.ClosingValue = 60000;
        var target = PerformanceFeeCalculatorTests.Input(); target.Inception = target.From = source.To; target.To = target.From!.Value.AddYears(1);
        target.OpeningValue = target.OpeningBenchmark = target.OpeningHighWaterMark = 0; target.ClosingValue = 60000;
        var outRow = new FeeStatementRow { Key = "out", Account = "A", Wrap = "KE1", Date = source.To!.Value, Kind = "Transfer", Amount = -60000, Source = "source.xlsx row 1" };
        var inRow = FeeJson.Read<FeeStatementRow>(FeeJson.Write(outRow)); inRow.Key = "in"; inRow.Account = "B"; inRow.Amount = 60000;
        source.EvidenceIssues.Add($"{outRow.Date:yyyy-MM-dd} Transfer: review {outRow.Detail} ({outRow.Amount:N2}) at {outRow.Source}.");
        target.EvidenceIssues.Add($"{inRow.Date:yyyy-MM-dd} Transfer: review {inRow.Detail} ({inRow.Amount:N2}) at {inRow.Source}.");
        var inputs = new Dictionary<string, FeePreparation>
        { ["A"] = new() { Inception = source.Inception, Wraps = [new() { Code = "KE1", Input = source }] }, ["B"] = new() { Inception = target.Inception, Wraps = [new() { Code = "KE1", Input = target }] } };
        var transfer = new FeeAccountTransfer("pair", "A", "B", "KE1", "KE1", outRow.Date, 60000, 120000, "source.xlsx paired rows", ["out"], ["in"]);
        var workspace = new FeeClientWorkspace(new(), "version", [], [new("A", null, null, new() { Rows = [outRow] }, null), new("B", null, null, new() { Rows = [inRow] }, null)], [transfer]);
        PerformanceFeeService.ApplyAccountTransfers(workspace, inputs, ["pair"], "Synthetic continuing mandate", new());
        Assert.Empty(target.Movements);
        Assert.Single(target.OpeningTransfers);
        Assert.Equal(60000m, target.OpeningValue); Assert.Equal(53500m, target.OpeningBenchmark); Assert.Equal(50000m, target.OpeningHighWaterMark);
        Assert.Equal(source.Inception, inputs["B"].Inception); Assert.Equal(source.Inception, target.Inception);
        target.Benchmarks = [new() { From = target.From!.Value, To = target.To!.Value, CpiPercent = 4, Reference = "Synthetic sourced annual CPI" }];
        Assert.True(PerformanceFeeCalculator.Calculate(target, target.To!.Value).IsReady);
        target.OpeningBenchmark = 60000;
        Assert.Contains(PerformanceFeeCalculator.Calculate(target, target.To!.Value).Issues, x => x.Contains("not a market-value reset"));
    }

    [Fact]
    public void Approved_evidence_appendix_contains_sources_but_not_private_paths_or_internal_deliberations()
    {
        var input = PerformanceFeeCalculatorTests.Input();
        input.MandateReference = @"C:\private-marker\signed-mandate.pdf, pp. 3-5";
        input.OpeningReference = @"C:\private-marker\opening-statement.xlsx";
        input.MethodRationale = "INTERNAL-DELIBERATION-MARKER";
        input.Benchmarks[0].Reference = @"C:\private-marker\CPI-source.pdf; https://www.statssa.gov.za/P0141August2026.pdf";
        input.StatementHistory = new() { BeginningCovered = true, Status = "Beginning covered", FirstFundingDate = input.Inception,
            Sources = [new("original.xlsx", new string('A', 64))], Periods = [new() { Account = "SYNTHETIC", From = input.From!.Value,
                To = input.To, OpeningValue = 100000, ClosingValue = 120000, Entries = [new("Market movement", 20000, "original.xlsx row 1")] }] };
        var profile = new PerformanceFeeProfile { Account = new() { AccountNumber = "SYNTHETIC", Client = new() { DisplayName = "Synthetic client" } } };
        var claim = new PerformanceFeeClaim { InputJson = FeeJson.Write(input), ResultJson = FeeJson.Write(PerformanceFeeCalculator.Calculate(input, input.To!.Value)), From = input.From.Value, To = input.To.Value };
        var text = Encoding.Latin1.GetString(PerformanceFeeExports.CalculationPdf(profile, claim, false));
        Assert.Contains("Evidence and reconciliation appendix", text);
        Assert.Contains("signed-mandate.pdf", text); Assert.Contains("CPI-source.pdf", text); Assert.Contains("SHA256", text);
        Assert.Contains("https://www.statssa.gov.za/P0141August2026.pdf", text);
        Assert.DoesNotContain("private-marker", text); Assert.DoesNotContain("INTERNAL-DELIBERATION-MARKER", text);
    }

    [Fact]
    public void A_closed_zero_unit_holding_need_not_reappear_in_every_later_statement()
    {
        var doc = PerformanceFeeStatementReader.Read(SummaryWorkbook(), "source.xlsx");
        doc.Rows.Add(new() { Key = "closed", Account = "SYNTHETIC-IA", Wrap = "KE1", Holding = "Old closed fund (KE1)", Kind = "Closing", Date = new(2021, 6, 30), UnitBalance = 0, Amount = 0 });
        var ledger = FeeStatementLedger.Merge([doc], "SYNTHETIC-IA");
        Assert.DoesNotContain(ledger.Wraps.Single().Issues, x => x.Contains("common statement cutoff"));
        Assert.Equal(new DateOnly(2022, 1, 1), ledger.Wraps.Single().ClosingDate);
    }

    internal static byte[] SummaryWorkbook(bool af = false, bool shared = false, int shift = 0, bool zero = false, string account = "SYNTHETIC-IA")
    {
        var template = PerformanceFeeStatementTests.Workbook(af, shared, shift: shift, account: account);
        using var source = new ZipArchive(new MemoryStream(template));
        var doc = XDocument.Load(source.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var data = doc.Root!.Element(ns + "sheetData")!;
        foreach (var row in data.Elements())
        {
            var number = (int)row.Attribute("r")! + 20; row.SetAttributeValue("r", number);
            foreach (var cell in row.Elements()) cell.SetAttributeValue("r", new string(cell.Attribute("r")!.Value.TakeWhile(char.IsLetter).ToArray()) + number);
        }
        XElement Cell(string col, int row, string value) => new(ns + "c", new XAttribute("r", col + row), new XAttribute("t", "inlineStr"), new XElement(ns + "is", new XElement(ns + "t", value)));
        XElement Row(int number, string label, string? amount = null) => new(ns + "row", new XAttribute("r", number), Cell("A", number, label), amount is null ? null : Cell("G", number, amount));
        data.AddFirst(new[] {
            Row(1, (af ? "Polisno : " : "Policy No : ") + account), Row(2, af ? "Transaksie Opsomming" : "Transaction Summary"),
            Row(3, (af ? "Beginmarkwaarde soos op " : "Opening Market Value as at ") + "2021/01/01", zero ? "R 0,00" : "R 100 000,00"),
            Row(4, af ? "Invloei" : "Inflows"), Row(5, zero ? "Lump Sum Investment" : "Reinvestment", zero ? "R 100 000,00" : "R 1 000,00"),
            Row(6, af ? "Markbeweging" : "Market Movement", zero ? "R 20 000,00" : "R 20 200,00"),
            Row(7, (af ? "Eindmarkwaarde soos op " : "Closing Market Value as at ") + "2022/01/01", zero ? "R 120 000,00" : "R 121 200,00") });
        if (zero)
        {
            void Set(int rowNo, int cellIndex, string value, bool numeric = false)
            {
                var cell = data.Elements().Single(x => (int)x.Attribute("r")! == rowNo).Elements().ElementAt(cellIndex);
                cell.RemoveNodes(); cell.SetAttributeValue("t", numeric ? null : "inlineStr");
                cell.Add(numeric ? new XElement(ns + "v", value) : new XElement(ns + "is", new XElement(ns + "t", value)));
            }
            Set(25, 4, "0,0000"); Set(25, 5, "0,00");
            Set(26, 1, af ? "Enkelbedragbelegging" : "Lump Sum Investment"); Set(26, 3, "100 000,0000"); Set(26, 4, "100 000,0000"); Set(26, 5, "100 000,00");
            Set(27, 4, "100 000,0000"); Set(27, 5, "120 000,00");
        }
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var entry in source.Entries)
            { using var stream = zip.CreateEntry(entry.FullName).Open(); if (entry.FullName.EndsWith("sheet1.xml")) doc.Save(stream); else { using var original = entry.Open(); original.CopyTo(stream); } }
        return output.ToArray();
    }
}
