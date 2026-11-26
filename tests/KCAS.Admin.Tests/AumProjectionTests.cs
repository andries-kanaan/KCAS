using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Xml.Linq;
using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class AumProjectionTests
{
    [Fact]
    public void BrokerIsAnIndependentReturnPathNotAdditionalCapital()
    {
        var snapshot = Fixture();
        var baseline = AumProjectionEngine.Run(snapshot);
        var scenario = AumProjectionEngine.Run(snapshot, new Dictionary<string, double> { [AumProjectionEngine.Broker] = .2 });
        Assert.Equal(100_000, scenario.Months[0].Opening, 6);
        Assert.Equal(25_000, scenario.Months[0].Funds.Single(x => x.Fund == AumProjectionEngine.Broker).Opening, 6);
        Assert.Equal(105_000, scenario.Years[0].Closing, 6);
        Assert.Equal(75_000 + 25_000 * Math.Pow(1.2, 5), scenario.Years[4].Closing, 6);
        Assert.Equal(100_000, baseline.Years[4].Closing, 6);
        Assert.Equal(baseline.Years[4].Closing, AumProjectionEngine.Run(snapshot).Years[4].Closing, 6);
        Assert.All(scenario.Months, m => Assert.Equal(m.Closing, m.Funds.Sum(f => f.Closing), 6));
    }

    [Fact]
    public void ThresholdSupportEntersAumAndStopsAtTheNextAnnualCheck()
    {
        var snapshot = Fixture() with
        {
            Holdings = [new(0, 0, 0, 95_000, 1)],
            Supports = [new(false, 0, [0], [0], 10_000)],
            Periods = Fixture().Periods.Select(p => p with { SupportSurvival = [1] }).ToArray()
        };
        var run = AumProjectionEngine.Run(snapshot);
        Assert.Equal(10_000, run.Months[0].DepositedSupport, 6);
        Assert.Equal(105_000, run.Years[0].Closing, 6);
        Assert.Equal(10_000, run.Years[0].Support, 6);
        Assert.All(run.Years.Skip(1), y => Assert.Equal(0, y.Support, 6));
    }

    [Fact]
    public void PriorCpiAndHighWaterMarkSuppressOffshoreFeesUntilCleared()
    {
        var snapshot = Fixture() with
        {
            OffshorePdfEnabled = 1, PdfIncomeShare = 1, UsCpi2025 = .03, FutureUsCpi = .03,
            FeeTerms = [new(100, 100, 100, .15, 0), new(100, 110, 100, .15, 0), new(100, 100, 100, 0, 0)]
        };
        var positive = AumProjectionEngine.Run(snapshot, new Dictionary<string, double> { [AumProjectionEngine.Equity] = .25 });
        var flat = AumProjectionEngine.Run(snapshot);
        Assert.Equal(0, flat.Years.Sum(y => y.OffshorePdf), 6);
        Assert.Equal(0, positive.Months[2].OffshorePdf, 6);
        Assert.True(positive.Years[0].OffshorePdf > 0);
    }

    [Fact]
    public void RejectsUnsupportedOverridesAndDuplicatedBrokerOpening()
    {
        var snapshot = Fixture();
        Assert.Throws<ValidationException>(() => AumProjectionEngine.Run(snapshot, new Dictionary<string, double> { ["Unknown"] = .1 }));
        Assert.Throws<ValidationException>(() => AumProjectionEngine.Run(snapshot, new Dictionary<string, double> { [AumProjectionEngine.Equity] = double.NaN }));
        Assert.Throws<ValidationException>(() => AumProjectionEngine.Run(snapshot with { Holdings = [new(1, 0, 0, 100_000, 1)] }));
    }

    [Fact]
    public void FrozenRunRoundTripsAndExportsItsComparison()
    {
        var snapshot = Fixture();
        var baseline = AumProjectionEngine.Run(snapshot);
        Dictionary<string, double> changes = new() { [AumProjectionEngine.Broker] = .2 };
        var run = new AumSavedProjection(1, 100, snapshot.SourceHash, snapshot.AsOf, "Broker scenario", snapshot.Source,
            snapshot.Funds, changes, baseline, AumProjectionEngine.Run(snapshot, changes));
        var restored = FundModelJson.Read<AumSavedProjection>(FundModelJson.Write(run));
        Assert.Equal(run.Active.Years[4].Closing, restored.Active.Years[4].Closing);
        Assert.Equal(run.Overrides[AumProjectionEngine.Broker], restored.Overrides[AumProjectionEngine.Broker]);
        using var zip = new ZipArchive(new MemoryStream(AumProjectionReports.Excel(restored)));
        using var sheet = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var html = sheet.ReadToEnd();
        Assert.Contains("Broker scenario", html);
        Assert.Contains("Baseline annualised", html);
        Assert.Contains("Year 5", html);
        Assert.Contains("Result after support per month", html);
        using var workbook = zip.GetEntry("xl/workbook.xml")!.Open();
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var names = XDocument.Load(workbook).Descendants(ns + "sheet").Select(s => (string?)s.Attribute("name")).ToArray();
        Assert.Equal(AumProjectionTables.SheetNames, names);
        Assert.NotNull(zip.GetEntry("xl/styles.xml"));
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(AumProjectionReports.Pdf(restored), 0, 5));
    }

    [Fact]
    public void BusinessForecastUsesYearEndAumForMonthlyRecurringRunRate()
    {
        var snapshot = Fixture() with
        {
            BusinessBaseline = new(new(2026, 8, 26), 80, 30, .04, new(2027, 10, 22), 50, 10, 120)
        };
        Dictionary<string, double> changes = new() { [AumProjectionEngine.Broker] = .2 };
        var baseline = AumProjectionEngine.Run(snapshot);
        var active = AumProjectionEngine.Run(snapshot, changes);
        var run = new AumSavedProjection(2, 100, snapshot.SourceHash, snapshot.AsOf, "Test", snapshot.Source,
            snapshot.Funds, changes, baseline, active) { Assumptions = AumRunAssumptions.From(snapshot) };
        var tables = AumProjectionTables.For(run, AumProjectionTables.Business);
        Assert.Equal(100, Assert.IsType<double>(tables[1].Rows[0][1]), 6);
        Assert.Equal(105, Assert.IsType<double>(tables[1].Rows[0][2]), 6);
        Assert.Equal(100_000, Assert.IsType<double>(tables[2].Rows[^1][1]), 6);
        var legacy = AumProjectionTables.For(run with { Assumptions = null }, AumProjectionTables.Business);
        Assert.Null(legacy[0].Rows[0][1]);
        Assert.Equal(105, Assert.IsType<double>(legacy[1].Rows[0][2]), 6);
    }

    [Fact]
    public void CapturedOffshoreReceiptAndSleeveTraceReconcileToMonthlyTotals()
    {
        var snapshot = Fixture() with
        {
            OffshorePdfEnabled = 1, PdfIncomeShare = .9, FutureUsCpi = .03, UsCpi2025 = .026,
            FeeTerms = [new(100, 100, 100, .15, 0), new(100, 100, 100, .15, 0), new(100, 100, 100, 0, 0)]
        };
        var run = AumProjectionEngine.Run(snapshot, new Dictionary<string, double> { [AumProjectionEngine.Equity] = .25 });
        var december = run.Months[2].OffshoreFees.Single(f => f.Fund == AumProjectionEngine.Equity);
        Assert.Equal(new DateOnly(2026, 12, 31), december.Checkpoint.QuarterEnd);
        Assert.True(december.Receipt > 0);
        Assert.True(december.Checkpoint.QuarterNav > december.Checkpoint.Hurdle);
        Assert.All(run.Months, m =>
        {
            Assert.Equal(m.OffshorePdf, m.OffshoreFees.Sum(f => f.Receipt), 8);
            Assert.NotNull(m.Sleeves);
        });
    }

    internal static AumSnapshot Fixture() => new(
        Version: 1, Source: "Test workbook.xlsx", SourceHash: new string('a', 64), AsOf: new(2026, 10, 8),
        BrokerShare: .25, BrokerWrapperCost: 0, MonthlyFeeIncome: 100, FeeBaseAum: 100_000,
        PdfIncomeShare: 0, OffshorePdfEnabled: 0, RetirementRate: .07, SupportRate: .08, SupportThreshold: 100_000,
        UsCpi2025: 0, FutureUsCpi: 0, SaHurdle: .07, SaPdfRate: 0, RecoveryMonth: 60, RecoveryShare: 0, RecoveryCar: 0,
        Funds: [new(AumProjectionEngine.Equity, 0, "Net"), new(AumProjectionEngine.Broker, 0, "Gross"),
            new(AumProjectionEngine.Moriah, 0, "Net"), new(AumProjectionEngine.Stable, 0, "Net")],
        Holdings: [new(0, 0, 0, 100_000, 1)], Accounts: [new(0, 0, 0, 1, false)], RetirementMonths: [0],
        Supports: [], FeeTerms: [new(100, 100, 100, 0, 0), new(100, 100, 100, 0, 0), new(100, 100, 100, 0, 0)],
        Periods: Enumerable.Range(0, 60).Select(_ => new AumPeriod(0, [0], [1], [0], [])).ToArray());
}
