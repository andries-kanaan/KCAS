using System.Globalization;

namespace KCAS.Admin.Data;

public sealed record AumReportTable(string Title, string[] Headings, List<object?[]> Rows);
internal sealed record AumPercent(double Value);
internal sealed record AumDecimal(double Value);

internal static class AumProjectionTables
{
    public const string Business = "Business forecast", Scenario = "Scenario comparison", Inputs = "Inputs",
        Monthly = "Monthly forecast", Funds = "Fund monthly", Support = "Client support", Sleeves = "EO and IBKR",
        Offshore = "PDF schedule", Local = "SA PDF estimates", Mortality = "Mortality";
    public static readonly string[] SheetNames = [Business, Scenario, Inputs, Monthly, Funds, Support, Sleeves, Offshore, Local, Mortality];

    public static AumReportTable[] For(AumSavedProjection r, string sheet, int? month = null) => sheet switch
    {
        Business => BusinessTables(r),
        Scenario => ScenarioTables(r),
        Inputs => [Rates(r.DefaultRates, r.Overrides), Assumptions(r.Assumptions)],
        Monthly => MonthlyTables(r),
        Funds => FundTables(r, month),
        Support => SupportTables(r, month),
        Sleeves => SleeveTables(r),
        Offshore => OffshoreTables(r, month),
        Local => [LocalTable(r)],
        Mortality => MortalityTables(r),
        _ => []
    };

    private static AumReportTable[] BusinessTables(AumSavedProjection r)
    {
        var years = r.Active.Years;
        var months = r.Active.Months;
        var a = r.Assumptions;
        var baseline = a?.BusinessBaseline;
        var monthlyFee = a?.MonthlyFeeIncome;
        // Older saved runs still contain the fee yield in their average-AUM fee calculation.
        var first = months.FirstOrDefault(x => x.Opening + x.Closing > 0);
        var monthlyYield = a is not null ? a.MonthlyFeeIncome / a.FeeBaseAum
            : first is null ? (double?)null : first.Recurring / ((first.Opening + first.Closing) / 2);
        var baseIncome = monthlyFee * 12;
        var baseCosts = baseline?.MonthlyOperatingCosts * 12;
        var baseIncomeSupport = baseline?.MonthlyIncomeSupport * 12;
        var baseFeeSupport = baseline?.AnnualFeeSupport;
        var baseAfter = baseIncome - baseCosts - baseIncomeSupport - baseFeeSupport;
        var yearEnd = months.Where((_, i) => (i + 1) % 12 == 0).ToArray();
        var groups = months.Chunk(12).ToArray();

        static object?[] Row(string label, object? baseline, IEnumerable<object?> values)
            => [label, baseline, .. values];
        var heading = new[] { "Annual results (ZAR)", "Baseline annualised" }
            .Concat(years.Select(y => $"Year {y.Year}")).ToArray();
        var annual = new AumReportTable("", heading, [
            Row("Period ending", baseline?.Date, years.Select(y => (object)y.End)),
            Row("Opening AUM", a?.FeeBaseAum, groups.Select(y => (object)y[0].Opening)),
            Row("Closing AUM", a?.FeeBaseAum, years.Select(y => (object)y.Closing)),
            Row("Average monthly AUM", a?.FeeBaseAum, groups.Select(y => (object)y.Average(m => (m.Opening + m.Closing) / 2))),
            Row("Implied annual fee yield", P(monthlyYield * 12), years.Select(_ => P(monthlyYield * 12))),
            Row("Income incl. SA and offshore PDFs", baseIncome, years.Select(y => (object)(y.Recurring + y.OffshorePdf + y.SaPdf))),
            Row("Operating costs", baseCosts, years.Select(y => (object)y.Costs)),
            Row("Operating profit / (loss)", baseIncome - baseCosts, years.Select(y => (object)Operating(y))),
            Row("Client-income support", baseIncomeSupport, groups.Select(y =>
                y.All(m => m.IncomeSupport.HasValue) ? (object)y.Sum(m => m.IncomeSupport!.Value) : null)),
            Row("Fee support", baseFeeSupport, groups.Select(y =>
                y.All(m => m.IncomeSupport.HasValue) ? (object)y.Sum(m => m.Support - m.IncomeSupport!.Value) : null)),
            Row("Total support", baseIncomeSupport + baseFeeSupport, years.Select(y => (object)y.Support)),
            Row("Result after support", baseAfter, years.Select(y => (object)y.Result)),
            Row("Operating result improvement vs prior year", null, years.Select((y, i) =>
                (object?)(Operating(y) - (i == 0 ? baseIncome - baseCosts : Operating(years[i - 1]))))),
            Row("After-support improvement vs prior year", null, years.Select((y, i) =>
                (object?)(y.Result - (i == 0 ? baseAfter : years[i - 1].Result)))),
            Row("Annual operating profit flag", null, years.Select(y => (object)(Operating(y) > 0 ? "Profit" : "No profit")))
        ]);
        var runRate = new AumReportTable("Recurring year-end monthly run rate (excludes PDFs)",
            ["Monthly results (ZAR)", "Baseline", .. years.Select(y => $"Year {y.Year}")], [
                Row("Fee income per month", monthlyFee, yearEnd.Select(m => (object?)(m.Closing * monthlyYield))),
                Row("Operating costs per month", baseline?.MonthlyOperatingCosts, yearEnd.Select(m => (object)m.Costs)),
                Row("Operating result per month", (baseIncome - baseCosts) / 12, yearEnd.Select(m => (object?)(m.Closing * monthlyYield - m.Costs))),
                Row("Support payments per month", (baseIncomeSupport + baseFeeSupport) / 12, yearEnd.Select(m => (object)m.Support)),
                Row("Result after support per month", baseAfter / 12, yearEnd.Select(m => (object?)(m.Closing * monthlyYield - m.Costs - m.Support)))
            ]);
        var operatingYear = years.FirstOrDefault(y => Operating(y) > 0);
        var supportYear = years.FirstOrDefault(y => y.Result > 0);
        var breakEven = new AumReportTable("Break-even and profitability", ["Measure", "Value"], [
            ["First positive operating month incl. PDFs", (object?)months.FirstOrDefault(m => Operating(m) > 0)?.End ?? "Not within five years"],
            ["First positive month after support incl. PDFs", (object?)months.FirstOrDefault(m => m.Result > 0)?.End ?? "Not within five years"],
            ["First profitable full projection year (operating)", operatingYear is null ? "Not within five years" : $"Year {operatingYear.Year}"],
            ["First profitable full projection year (after support)", supportYear is null ? "Not within five years" : $"Year {supportYear.Year}"],
            ["Operating break-even AUM without PDFs", monthlyYield > 0 ? baseline?.MonthlyOperatingCosts / monthlyYield : null],
            ["After-support break-even AUM without PDFs", monthlyYield > 0 ? (baseCosts + baseIncomeSupport + baseFeeSupport) / 12 / monthlyYield : null]
        ]);
        return [annual, runRate, breakEven];
    }

    private static AumReportTable[] ScenarioTables(AumSavedProjection r)
        => [new("Defaults vs active CARs",
            ["Year", "Period ending", "Default AUM", "Active AUM", "AUM change", "Default result after support", "Active result after support", "Result change"],
            r.Active.Years.Zip(r.Defaults.Years, (a, b) => new object?[] {
                a.Year, a.End, b.Closing, a.Closing, a.Closing - b.Closing, b.Result, a.Result, a.Result - b.Result }).ToList()),
            Rates(r.DefaultRates, r.Overrides)];

    public static AumReportTable Rates(AumFund[] funds, IReadOnlyDictionary<string, double> overrides)
        => new("Fund CARs", ["Fund", "Basis", "Default CAR", "Active CAR"],
            funds.Select(f => new object?[] { f.Name, f.Basis, P(f.Car),
                P(overrides.GetValueOrDefault(f.Name == AumProjectionEngine.MoriahUsd ? AumProjectionEngine.Moriah : f.Name, f.Car)) }).ToList());

    public static AumReportTable Assumptions(AumRunAssumptions? a)
    {
        var b = a?.BusinessBaseline;
        var m = a?.MortalityBasis;
        return new("Snapshot assumptions", ["Parameter", "Value", "Basis"], a is null ? [] : [
            ["Document baseline date", b?.Date, "Business source baseline, not a live valuation"],
            ["Document baseline AUM", a.FeeBaseAum, "Basis for the portfolio-wide fee yield"],
            ["Baseline monthly recurring fee income", a.MonthlyFeeIncome, "Excludes performance-driven fees"],
            ["Baseline monthly operating costs", b?.MonthlyOperatingCosts, "Before client support"],
            ["Monthly basic salary", b?.MonthlySalary, "Only basic salary is escalated"],
            ["Annual salary increase", P(b?.SalaryIncrease), "Snapshot payroll schedule"],
            ["First salary increase", b?.FirstSalaryIncrease, "Prorated when a period straddles the increase"],
            ["Other monthly operating costs", b?.OtherMonthlyOperatingCosts, "Not escalated in the source schedule"],
            ["Baseline monthly client-income support", b?.MonthlyIncomeSupport, "Company expense"],
            ["Baseline annual fee support", b?.AnnualFeeSupport, "Company expense"],
            ["Fee-support stopping balance", a.SupportThreshold, "Annual renewal eligibility"],
            ["Income-support stopping rate", P(a.SupportRate), "Annualised planned withdrawals divided by opening balances"],
            ["Retirement draw rate", P(a.RetirementRate), "On the applicable portfolio at retirement and each anniversary"],
            ["Coleman recovery month", a.RecoveryMonth, "Conditional return change; no additional principal"],
            ["Coleman share already in Moriah", P(a.RecoveryShare), "Opening allocation"],
            ["Enko net CAR after recovery", P(a.RecoveryCar), "Scenario rate"],
            ["IBKR share of opening EO", P(a.BrokerShare), "Part of the wrapper, not new AUM"],
            ["Annual IBKR wrapper cost", P(a.BrokerCost), "Applied monthly"],
            ["Offshore PDF income share retained", P(a.PdfIncomeShare * a.OffshorePdfEnabled), "Amount included in business results"],
            ["US CPI for 2026 fee dates", P(a.UsCpi2025), "2025 annual-average CPI used by the model"],
            ["Future prior-year US CPI", P(a.FutureUsCpi), "Planning assumption"],
            ["SA CPI plus hurdle", P(a.SaHurdle), "Projected wrap fee estimate"],
            ["Effective SA estimate rate", P(a.SaEffectiveRate), "Fee rate adjusted by retained prudence factor"],
            ["Estate withdrawal probability", P(m?.WithdrawalProbability), "Eligible voluntary shares only"],
            ["Estate withdrawal lag (months)", m?.DelayMonths, "Fixed in the snapshot's mortality schedule"]
        ]);
    }

    private static AumReportTable[] MonthlyTables(AumSavedProjection r) => [
        new("Monthly income and costs",
            ["Period ending", "Opening AUM", "Closing AUM", "Average AUM", "Recurring fees", "Offshore PDFs", "Estimated SA PDFs", "Total income", "Operating costs",
             "Operating result", "Client-income support", "Fee support", "Total support", "After support", "Cumulative after support"],
            r.Active.Months.Select((m, i) => new object?[] { m.End, m.Opening, m.Closing, (m.Opening + m.Closing) / 2,
                m.Recurring, m.OffshorePdf, m.SaPdf, m.Recurring + m.OffshorePdf + m.SaPdf, m.Costs, Operating(m), m.IncomeSupport,
                m.Support - m.IncomeSupport, m.Support, m.Result, r.Active.Months.Take(i + 1).Sum(p => p.Result) }).ToList()),
        new("Monthly AUM movements", ["Period ending", "Opening AUM", "Net investment growth", "Contributions", "Support deposits", "Income requested",
            "Income paid", "Unfunded income", "Estate withdrawals", "Closing AUM"],
            r.Active.Months.Select(m => new object?[] { m.End, m.Opening, m.Growth, m.Contributions, m.DepositedSupport, m.Requested,
                m.Requested - m.Unfunded, m.Unfunded, m.Estate, m.Closing }).ToList())
    ];

    private static AumReportTable[] FundTables(AumSavedProjection r, int? month)
    {
        var periods = r.Active.Months;
        return [new("Year-end fund balances", ["Fund", "Opening AUM", .. r.Active.Years.Select(y => $"Year {y.Year}")],
                    periods[0].Funds.Select((f, i) => (object?[])[f.Fund, f.Opening,
                        .. periods.Where((_, j) => (j + 1) % 12 == 0).Select(m => (object)m.Funds[i].Closing)]).ToList()),
                new("Monthly fund balances", ["Fund", "Period ending", "Opening AUM", "Closing AUM"],
                    Filter(periods, month).SelectMany(m => m.Funds.Select(f => new object?[] { f.Fund, m.End, f.Opening, f.Closing })).ToList())];
    }

    private static AumReportTable[] SupportTables(AumSavedProjection r, int? month) =>
        [new("Monthly support balances", ["Workbook support row", "Period ending", "Rule", "Opening AUM", "Closing AUM", "Annual withdrawal rate", "Amount deposited", "Status"],
            Filter(r.Active.Months, month).SelectMany(m => m.SupportBalances.Select(s => new object?[] {
                s.Scope + 5, m.End, s.Rule, s.Opening, s.Closing, P(s.DrawRate), s.Deposit, s.Active ? "Continues" : "Stopped" })).ToList())];

    private static AumReportTable[] SleeveTables(AumSavedProjection r)
    {
        static AumFundBalance Balance(AumProjectionMonth m, string name) => m.Funds.Single(f => f.Fund == name);
        var months = r.Active.Months;
        var years = r.Active.Years.Zip(r.Defaults.Years, (y, d) =>
        {
            var m = months[y.Year * 12 - 1];
            var eo = Balance(m, AumProjectionEngine.Equity).Closing;
            var ib = Balance(m, AumProjectionEngine.Broker).Closing;
            return new object?[] { y.Year, eo, ib, eo + ib, y.Closing, d.Closing, y.Closing - d.Closing,
                y.OffshorePdf, d.OffshorePdf, y.Result, d.Result };
        }).ToList();
        return [new("Separate EO and IBKR return paths", ["Year", "EO closing AUM", "IBKR closing AUM", "Combined EO wrapper", "Total active AUM", "Total default AUM",
                    "AUM change", "Active offshore PDFs", "Default offshore PDFs", "Active result after support", "Default result after support"], years),
                new("Monthly EO and IBKR", ["Period ending", "EO opening", "IBKR opening", "EO pre-PDF factor", "IBKR pre-PDF factor",
                    "EO before PDF and flows", "IBKR before PDF and flows", "EO closing", "IBKR closing", "Combined wrapper", "Blended pre-PDF factor"],
                    months.Select(m =>
                    {
                        var eo = Balance(m, AumProjectionEngine.Equity);
                        var ib = Balance(m, AumProjectionEngine.Broker);
                        return new object?[] { m.End, eo.Opening, ib.Opening, D(m.Sleeves?.EquityFactor), D(m.Sleeves?.BrokerFactor),
                            eo.Opening * m.Sleeves?.EquityFactor, ib.Opening * m.Sleeves?.BrokerFactor, eo.Closing, ib.Closing,
                            eo.Closing + ib.Closing, D(m.Sleeves?.WrapperFactor) };
                    }).ToList())];
    }

    private static AumReportTable[] OffshoreTables(AumSavedProjection r, int? month)
    {
        string[] names = [AumProjectionEngine.Moriah, AumProjectionEngine.Equity, AumProjectionEngine.Stable];
        var terms = r.Assumptions?.FeeTerms ?? [];
        return [new("Offshore PDF terms", ["Fund", "Opening NAV", "Opening HWM", "Opening hurdle base", "Fee rate", "Hurdle"],
                    terms.Select((t, i) => new object?[] { names[i], D(t.Nav), D(t.HighWater), D(t.HurdleBase), P(t.Rate),
                        i == 2 ? P(t.Hurdle) : "Prior-year US CPI" }).ToList()),
                new("Offshore PDF calculation schedule", ["Fund", "Period ending", "Opening AUM", "Closing AUM", "Net NAV return", "Opening NAV", "Closing NAV",
                    "Opening HWM", "Opening hurdle base", "Quarter end", "Quarter fraction", "Prior-year US CPI", "Quarter NAV before fee",
                    "Quarter hurdle", "Fee benchmark", "PDF per unit", "Quarter AUM", "Projected PDF income", "Closing HWM", "Carried hurdle base"],
                    Filter(r.Active.Months, month).SelectMany(m => m.OffshoreFees.Select(f =>
                    {
                        var c = f.Checkpoint;
                        return new object?[] { f.Fund, m.End, f.OpeningAum, f.ClosingAum, P(c.ClosingNav / c.OpeningNav - 1), D(c.OpeningNav),
                            D(c.ClosingNav), D(c.OpeningHwm), D(c.OpeningBase), c.QuarterEnd, c.QuarterEnd is null ? null : P(c.Fraction), P(f.Cpi),
                            D(c.QuarterNav), D(c.Hurdle), D(c.Hurdle is double h ? Math.Max(c.OpeningHwm, h) : null), D(c.FeePerUnit),
                            c.QuarterEnd is null ? null : f.OpeningAum + (f.ClosingAum - f.OpeningAum) * c.Fraction, f.Receipt,
                            D(c.ClosingHwm), D(c.ClosingBase) };
                    })).ToList())];
    }

    private static AumReportTable LocalTable(AumSavedProjection r) =>
        new("SA performance-driven fee estimates", ["Fund", "Period ending", "Average AUM", "Net CAR", "CPI plus hurdle", "Positive excess",
            "Effective estimate rate", "Estimated SA PDF"],
            r.Active.Months.SelectMany(m => m.SaFees.Select(f => new object?[] { f.Fund, m.End, f.AverageAum,
                P(f.Car), P(f.Hurdle), P(Math.Max(0, f.Car - f.Hurdle)), P(f.EffectiveRate), f.Receipt })).ToList());

    private static AumReportTable[] MortalityTables(AumSavedProjection r)
    {
        var basis = r.Assumptions?.MortalityBasis;
        return [new("Coverage of opening AUM", ["Measure", "Value"], [
            ["Total opening AUM", r.Active.Months[0].Opening],
            ["Eligible voluntary AUM modelled", basis?.EligibleVoluntary],
            ["Unproven joint ownership (voluntary subset)", basis?.UnprovenJoint],
            ["Voluntary AUM not modelled (incl. entities)", basis?.TotalVoluntary - basis?.EligibleVoluntary],
            ["Total classified voluntary opening AUM", basis?.TotalVoluntary],
            ["Unclassified product AUM (also excluded)", basis?.Unclassified],
            ["Withdrawal probability", P(basis?.WithdrawalProbability)],
            ["Withdrawal delay (months)", basis?.DelayMonths]
        ]),
        new("Expected estate withdrawals (not individual predictions)", ["Period ending", "Opening AUM", "Estate withdrawals", "Cumulative estate withdrawals", "Closing AUM"],
            r.Active.Months.Select((m, i) => new object?[] { m.End, m.Opening, m.Estate,
                r.Active.Months.Take(i + 1).Sum(p => p.Estate), m.Closing }).ToList())];
    }

    private static IEnumerable<AumProjectionMonth> Filter(AumProjectionMonth[] rows, int? month)
        => month is int i ? rows.Skip(i).Take(1) : rows;
    private static double Operating(AumProjectionYear y) => y.Recurring + y.OffshorePdf + y.SaPdf - y.Costs;
    private static double Operating(AumProjectionMonth m) => m.Recurring + m.OffshorePdf + m.SaPdf - m.Costs;
    private static AumPercent? P(double? value) => value.HasValue ? new(value.Value) : null;
    private static AumDecimal? D(double? value) => value.HasValue ? new(value.Value) : null;

    public static string Display(object? value) => value switch
    {
        null => "-",
        DateOnly date => date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
        AumPercent p => p.Value.ToString("0.00%", CultureInfo.InvariantCulture),
        AumDecimal d => d.Value.ToString("0.000000", CultureInfo.InvariantCulture),
        double d => d.ToString("N0", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };
}
