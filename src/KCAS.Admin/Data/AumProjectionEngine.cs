using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed record AumSnapshot(
    int Version, string Source, string SourceHash, DateOnly AsOf, double BrokerShare, double BrokerWrapperCost,
    double MonthlyFeeIncome, double FeeBaseAum, double PdfIncomeShare, double OffshorePdfEnabled,
    double RetirementRate, double SupportRate, double SupportThreshold,
    double UsCpi2025, double FutureUsCpi, double SaHurdle, double SaPdfRate,
    int RecoveryMonth, double RecoveryShare, double RecoveryCar,
    AumFund[] Funds, AumHolding[] Holdings, AumAccount[] Accounts, int[] RetirementMonths,
    AumSupport[] Supports, AumFeeTerms[] FeeTerms, AumPeriod[] Periods)
{
    public AumBusinessBaseline? BusinessBaseline { get; init; }
    public AumMortalityBasis? MortalityBasis { get; init; }
}

public sealed record AumBusinessBaseline(DateOnly Date, double MonthlyOperatingCosts, double MonthlySalary,
    double SalaryIncrease, DateOnly FirstSalaryIncrease, double OtherMonthlyOperatingCosts, double MonthlyIncomeSupport, double AnnualFeeSupport);
public sealed record AumMortalityBasis(double WithdrawalProbability, int DelayMonths, double EligibleVoluntary, double UnprovenJoint,
    double TotalVoluntary, double Unclassified);

public sealed record AumFund(string Name, double Car, string Basis, bool SaPdf = false);
public sealed record AumHolding(int Fund, int Account, int Pool, double Opening, double IncomeWeight);
public sealed record AumAccount(int Pool, double FixedAnnualWithdrawal, double WithdrawalRate, int Interval, bool Advance);
public sealed record AumSupport(bool Income, int Pool, int[] Scope, int[] Targets, double AnnualAmount);
public sealed record AumFeeTerms(double Nav, double HighWater, double HurdleBase, double Rate, double Hurdle);
public sealed record AumPeriod(double Costs, double[] Contributions, double[] WithdrawalWeights, double[] EstateWeights, double[] SupportSurvival);
public sealed record AumFundBalance(string Fund, double Opening, double Closing);
public sealed record AumSupportBalance(int Scope, string Rule, double Opening, double Closing, double? DrawRate, double Deposit, bool Active);
public sealed record AumSleeveMonth(double EquityFactor, double BrokerFactor, double WrapperFactor);
public sealed record AumFeeCheckpoint(double OpeningNav, double ClosingNav, double OpeningHwm, double OpeningBase,
    DateOnly? QuarterEnd, double Fraction, double? QuarterNav, double? Hurdle, double? FeePerUnit, double ClosingHwm, double ClosingBase);
public sealed record AumOffshoreFeeMonth(string Fund, double OpeningAum, double ClosingAum, double Receipt, AumFeeCheckpoint Checkpoint, double? Cpi);
public sealed record AumSaFeeYear(string Fund, double AverageAum, double Car, double Hurdle, double EffectiveRate, double Receipt);
public sealed record AumProjectionMonth(DateOnly End, double Opening, double Closing, double Growth, double Contributions,
    double DepositedSupport, double Requested, double Unfunded, double Estate, double Recurring, double OffshorePdf,
    double SaPdf, double Costs, double Support, AumFundBalance[] Funds)
{
    public double Result => Recurring + OffshorePdf + SaPdf - Costs - Support;
    public AumSupportBalance[] SupportBalances { get; init; } = [];
    public double? IncomeSupport { get; init; }
    public AumSleeveMonth? Sleeves { get; init; }
    public AumOffshoreFeeMonth[] OffshoreFees { get; init; } = [];
    public AumSaFeeYear[] SaFees { get; init; } = [];
}
public sealed record AumProjectionYear(int Year, DateOnly End, double Closing, double Recurring, double OffshorePdf,
    double SaPdf, double Costs, double Support, double Result);
public sealed record AumProjectionResult(AumProjectionMonth[] Months, AumProjectionYear[] Years);

public static class AumProjectionEngine
{
    public const string Equity = "Equity Offshore";
    public const string Broker = "EO IBKR Brokers";
    public const string Moriah = "Moriah Global";
    public const string MoriahUsd = "Moriah Global FoF (USD)";
    public const string Stable = "Stable Offshore";

    private static double Factor(double car) => Math.Pow(1 + car, 1d / 12);
    private static void Require(bool ok, string message)
    {
        if (!ok) throw new ValidationException(message);
    }

    private static bool Within(double x, double lo, double hi) => double.IsFinite(x) && x >= lo && x <= hi;

    public static void Validate(AumSnapshot s)
    {
        Require(s.Version == 1 && s.Source is { Length: > 0 and <= 200 } && s.SourceHash is { Length: 64 } &&
            s.SourceHash.All(Uri.IsHexDigit) && s.AsOf.Year is >= 2000 and <= 2100, "Unsupported snapshot header.");
        Require(s.Funds is { Length: >= 4 and <= 200 } && s.Holdings is { Length: > 0 and <= 5000 } &&
            s.Accounts is { Length: > 0 and <= 5000 } && s.RetirementMonths is { Length: > 0 and <= 5000 } &&
            s.Supports is { Length: <= 200 } && s.FeeTerms is { Length: 3 } && s.Periods is { Length: 60 },
            "Unexpected snapshot dimensions.");
        Require(s.Funds.All(f => f is not null && f.Name is { Length: > 0 and <= 150 } && f.Basis is { Length: > 0 and <= 150 } &&
            Within(f.Car, -.8, 2)) && s.Funds.Select(f => f.Name).Distinct(StringComparer.Ordinal).Count() == s.Funds.Length &&
            new[] { Equity, Broker, Moriah, Stable }.All(n => s.Funds.Any(f => f.Name == n)), "Invalid fund rates or missing required fund.");
        Require(s.Holdings.All(h => h is not null && h.Fund >= 0 && h.Fund < s.Funds.Length && s.Funds[h.Fund].Name != Broker &&
            h.Account >= 0 && h.Account < s.Accounts.Length && h.Pool >= 0 && h.Pool < s.RetirementMonths.Length &&
            s.Accounts[h.Account]?.Pool == h.Pool && Within(h.Opening, 0, 1e12) && Within(h.IncomeWeight, 0, 1.00000001)),
            "Invalid holding. Broker assets must be carved out of Equity Offshore, not added as opening holdings.");
        Require(s.Accounts.All(a => a is not null && a.Pool >= 0 && a.Pool < s.RetirementMonths.Length && a.Interval is >= 1 and <= 60 &&
            Within(a.FixedAnnualWithdrawal, 0, 1e12) && Within(a.WithdrawalRate, 0, 1)) &&
            s.RetirementMonths.All(m => m is >= 0 and <= 60), "Invalid account or retirement inputs.");
        bool Indices(int[]? xs) => xs is { Length: > 0 } && xs.Length <= s.Holdings.Length &&
            xs.All(i => i >= 0 && i < s.Holdings.Length) && xs.Distinct().Count() == xs.Length;
        Require(s.Supports.All(x => x is not null && x.Pool >= 0 && x.Pool < s.RetirementMonths.Length && Indices(x.Scope) &&
            Indices(x.Targets) && x.Targets.All(i => x.Scope.Contains(i)) &&
            (!x.Income || x.Scope.All(i => s.Holdings[i].Pool == x.Pool)) && x.Targets.Sum(i => s.Holdings[i].Opening) > 0 &&
            Within(x.AnnualAmount, 0, 1e10)), "Invalid support scope or deposit allocation.");
        bool Series(double[]? x, int n, double max) => x?.Length == n && x.All(v => Within(v, 0, max));
        Require(s.Periods.All(p => p is not null && Within(p.Costs, 0, 1e12) && Series(p.Contributions, s.Holdings.Length, 1e12) &&
            Series(p.WithdrawalWeights, s.Holdings.Length, 1) && Series(p.EstateWeights, s.Holdings.Length, 1) &&
            Series(p.SupportSurvival, s.Supports.Length, 1)), "Invalid monthly cash flow or mortality inputs.");
        Require(s.FeeTerms.All(x => x is not null && Within(x.Nav, .0001, 1e9) && Within(x.HighWater, .0001, 1e9) &&
            Within(x.HurdleBase, .0001, 1e9) && Within(x.Rate, 0, .79) && Within(x.Hurdle, 0, 1)) &&
            new[] { s.BrokerShare, s.BrokerWrapperCost, s.PdfIncomeShare, s.OffshorePdfEnabled, s.RetirementRate,
                s.SupportRate, s.UsCpi2025, s.FutureUsCpi, s.SaHurdle, s.SaPdfRate, s.RecoveryShare }.All(x => Within(x, 0, 1)) &&
            Within(s.MonthlyFeeIncome, 0, 1e12) && Within(s.FeeBaseAum, 1, 1e13) && Within(s.SupportThreshold, 0, 1e12) &&
            Within(s.RecoveryCar, -.8, 2) && s.RecoveryMonth is >= 0 and <= 60, "Invalid business or fee assumptions.");
        Require(s.Funds.Single(f => f.Name == Equity).Car > -1 + Math.Pow(s.FeeTerms[1].Rate, 4), "EO CAR is incompatible with its historical fee basis.");
        if (s.BusinessBaseline is { } b)
            Require(b.Date.Year >= 2000 && b.Date <= s.AsOf && b.FirstSalaryIncrease >= b.Date &&
                Within(b.SalaryIncrease, 0, 1) &&
                new[] { b.MonthlyOperatingCosts, b.MonthlySalary, b.OtherMonthlyOperatingCosts, b.MonthlyIncomeSupport, b.AnnualFeeSupport }
                    .All(v => Within(v, 0, 1e12)), "Invalid business baseline.");
        if (s.MortalityBasis is { } m)
            Require(Within(m.WithdrawalProbability, 0, 1) && m.DelayMonths is >= 0 and <= 60 &&
                Within(m.EligibleVoluntary, 0, m.TotalVoluntary) && Within(m.UnprovenJoint, 0, m.TotalVoluntary) &&
                Within(m.TotalVoluntary, 0, 1e14) && Within(m.Unclassified, 0, 1e14), "Invalid mortality reporting basis.");
    }

    public static AumProjectionResult Run(AumSnapshot s, IReadOnlyDictionary<string, double>? overrides = null)
    {
        Validate(s);
        var indices = s.Funds.Select((f, i) => (f.Name, i)).ToDictionary(x => x.Name, x => x.i, StringComparer.Ordinal);
        Require(overrides?.All(x => indices.ContainsKey(x.Key) && x.Key != MoriahUsd && Within(x.Value, -.8, 2)) != false,
            "Each custom CAR must apply to a forecast fund and be between -80% and 200%.");
        var rates = s.Funds.Select(f => overrides?.GetValueOrDefault(f.Name, f.Car) ?? f.Car).ToArray();
        var eo = indices[Equity]; var ib = indices[Broker]; var mg = indices[Moriah]; var so = indices[Stable];
        bool hasMgUsd = indices.TryGetValue(MoriahUsd, out var mgUsd);
        if (hasMgUsd) rates[mgUsd] = rates[mg];
        var factors = rates.Select(Factor).ToArray();
        var eoFactor = Math.Pow(1 + (Math.Pow(1 + rates[eo], .25) - 1) / (1 - s.FeeTerms[1].Rate), 1d / 3);
        Require(double.IsFinite(eoFactor) && eoFactor > 0, "EO CAR is incompatible with its historical fee basis.");
        var ibFactor = factors[ib] * (1 - s.BrokerWrapperCost / 12);
        var fees = s.FeeTerms.Select(t => new FeeState(t, s.AsOf)).ToArray();
        var balances = s.Holdings.Select(h => h.Opening).ToArray();
        var annualAccount = new double[s.Accounts.Length];
        var retirementBase = new double[s.RetirementMonths.Length];
        var supportActive = s.Supports.Select(_ => true).ToArray();
        var annualSupport = new double[s.Supports.Length];
        var targetWeights = s.Supports.Select(x => x.Targets.Select(i => s.Holdings[i].Opening /
            x.Targets.Sum(j => s.Holdings[j].Opening)).ToArray()).ToArray();
        var months = new List<AumProjectionMonth>(60);
        var years = new List<AumProjectionYear>(5);
        var ibWeight = s.BrokerShare;
        var yearSaAverage = new double[s.Funds.Length];

        for (var month = 1; month <= 60; month++)
        {
            var p = s.Periods[month - 1];
            var start = s.AsOf.AddMonths(month - 1); var end = s.AsOf.AddMonths(month);
            var fundOpening = new double[s.Funds.Length]; var fundClosing = new double[s.Funds.Length];
            var accounts = new double[s.Accounts.Length]; var pools = new double[s.RetirementMonths.Length];
            for (var i = 0; i < balances.Length; i++)
            {
                var h = s.Holdings[i]; fundOpening[h.Fund] += balances[i]; accounts[h.Account] += balances[i]; pools[h.Pool] += balances[i];
            }
            if (month % 12 == 1)
                for (var a = 0; a < accounts.Length; a++)
                    annualAccount[a] = s.Accounts[a].FixedAnnualWithdrawal + accounts[a] * s.Accounts[a].WithdrawalRate;
            var poolAnnual = new double[pools.Length];
            for (var a = 0; a < accounts.Length; a++) poolAnnual[s.Accounts[a].Pool] += annualAccount[a];
            for (var j = 0; j < pools.Length; j++)
            {
                var from = s.RetirementMonths[j];
                if (from == 0 || month < from) continue;
                if ((month - from) % 12 == 0) retirementBase[j] = pools[j];
                poolAnnual[j] = retirementBase[j] * s.RetirementRate;
            }

            var supportDeposits = new double[balances.Length];
            var supportRows = new AumSupportBalance[s.Supports.Length];
            double costSupport = 0, incomeSupport = 0;
            for (var j = 0; j < s.Supports.Length; j++)
            {
                var x = s.Supports[j]; var opening = x.Scope.Sum(i => balances[i]);
                if (x.Income)
                {
                    var planned = poolAnnual[x.Pool];
                    supportActive[j] &= opening > 0 ? planned / opening > s.SupportRate : planned > 0;
                }
                else if (month % 12 == 1) supportActive[j] &= opening < s.SupportThreshold;
                var deposit = supportActive[j] ? x.AnnualAmount * p.SupportSurvival[j] * (x.Income ? 1d / 12 : month % 12 == 1 ? 1 : 0) : 0;
                supportRows[j] = new(j + 1, x.Income ? "Withdrawal rate" : "Balance threshold", opening, 0,
                    x.Income && opening > 0 ? poolAnnual[x.Pool] / opening : null, deposit, supportActive[j]);
                if (x.Income)
                {
                    costSupport += deposit;
                    incomeSupport += deposit;
                }
                else
                {
                    if (month % 12 == 1) annualSupport[j] = deposit;
                    costSupport += annualSupport[j] / 12;
                }
                for (var t = 0; t < x.Targets.Length; t++) supportDeposits[x.Targets[t]] += deposit * targetWeights[j][t];
            }

            var net = (double[])factors.Clone();
            var eoGross = eoFactor * (1 - ibWeight) + ibFactor * ibWeight;
            var mgGross = month > s.RecoveryMonth
                ? factors[mg] * (1 - s.RecoveryShare) + Factor(s.RecoveryCar) * s.RecoveryShare : factors[mg];
            var inflation = start.Year == 2026 ? s.UsCpi2025 : s.FutureUsCpi;
            net[mg] = fees[0].Advance(mgGross, start, end, inflation, legacy: false);
            if (hasMgUsd) net[mgUsd] = net[mg];
            net[eo] = fees[1].Advance(eoGross, start, end, inflation, legacy: false);
            fees[2].Advance(net[so], start, end, inflation, legacy: true);

            var pre = new double[balances.Length]; var deficits = new double[pools.Length]; var positives = new double[pools.Length];
            double growth = 0, requested = 0, unfunded = 0, estate = 0;
            for (var i = 0; i < balances.Length; i++)
            {
                var h = s.Holdings[i]; var a = s.Accounts[h.Account]; var m = s.RetirementMonths[h.Pool];
                var due = (a.Advance ? month - 1 : month) % a.Interval == 0;
                var withdrawal = m > 0 && month >= m
                    ? pools[h.Pool] > 0 ? poolAnnual[h.Pool] / 12 * balances[i] / pools[h.Pool] : 0
                    : due ? annualAccount[h.Account] / 12 * a.Interval * h.IncomeWeight : 0;
                withdrawal *= p.WithdrawalWeights[i];
                var gain = balances[i] * (net[h.Fund] - 1);
                requested += withdrawal; growth += gain;
                pre[i] = balances[i] + gain + p.Contributions[i] + supportDeposits[i] - withdrawal;
                deficits[h.Pool] += Math.Max(-pre[i], 0); positives[h.Pool] += Math.Max(pre[i], 0);
            }
            for (var i = 0; i < balances.Length; i++)
            {
                var h = s.Holdings[i]; var deficit = Math.Max(-pre[i], 0); var positive = Math.Max(pre[i], 0);
                var fraction = positives[h.Pool] > 0 ? Math.Min(1, deficits[h.Pool] / positives[h.Pool]) : 0;
                unfunded += deficit * (deficits[h.Pool] > 0 ? 1 - Math.Min(1, positives[h.Pool] / deficits[h.Pool]) : 0);
                var beforeEstate = positive * (1 - fraction);
                estate += beforeEstate * p.EstateWeights[i];
                balances[i] = beforeEstate * (1 - p.EstateWeights[i]);
                fundClosing[h.Fund] += balances[i];
            }

            var mgOpening = fundOpening[mg] + (hasMgUsd ? fundOpening[mgUsd] : 0);
            var mgClosing = fundClosing[mg] + (hasMgUsd ? fundClosing[mgUsd] : 0);
            var receiptScale = s.PdfIncomeShare * s.OffshorePdfEnabled;
            var mgReceipt = fees[0].Receipt(mgOpening, mgClosing);
            var eoReceipt = fees[1].Receipt(fundOpening[eo], fundClosing[eo]);
            var soReceipt = fees[2].Receipt(fundOpening[so], fundClosing[so]);
            var offshorePdf = receiptScale * (mgReceipt + eoReceipt + soReceipt);
            AumOffshoreFeeMonth[] offshoreFees = [
                new(Moriah, mgOpening, mgClosing, receiptScale * mgReceipt, fees[0].Checkpoint, inflation),
                new(Equity, fundOpening[eo], fundClosing[eo], receiptScale * eoReceipt, fees[1].Checkpoint, inflation),
                new(Stable, fundOpening[so], fundClosing[so], receiptScale * soReceipt, fees[2].Checkpoint, null)];
            var openingTotal = fundOpening.Sum(); var closingTotal = fundClosing.Sum();
            for (var j = 0; j < s.Supports.Length; j++)
                supportRows[j] = supportRows[j] with { Closing = s.Supports[j].Scope.Sum(i => balances[i]) };
            var eoEndWeight = fundOpening[eo] > 0 ? ibWeight * ibFactor / eoGross : s.BrokerShare;
            fundOpening[ib] = fundOpening[eo] * ibWeight; fundOpening[eo] -= fundOpening[ib];
            fundClosing[ib] = fundClosing[eo] * eoEndWeight; fundClosing[eo] -= fundClosing[ib];
            ibWeight = eoEndWeight;
            double saPdf = 0;
            List<AumSaFeeYear> saFees = [];
            for (var f = 0; f < s.Funds.Length; f++)
            {
                if (!s.Funds[f].SaPdf) continue;
                yearSaAverage[f] += (fundOpening[f] + fundClosing[f]) / 24;
                if (month % 12 != 0) continue;
                var estimate = yearSaAverage[f] * Math.Max(0, rates[f] - s.SaHurdle) * s.SaPdfRate;
                saPdf += estimate;
                saFees.Add(new(s.Funds[f].Name, yearSaAverage[f], rates[f], s.SaHurdle, s.SaPdfRate, estimate));
                yearSaAverage[f] = 0;
            }
            months.Add(new(end, openingTotal, closingTotal, growth, p.Contributions.Sum(), supportDeposits.Sum(), requested,
                unfunded, estate, (openingTotal + closingTotal) / 2 * s.MonthlyFeeIncome / s.FeeBaseAum,
                offshorePdf, saPdf, p.Costs, costSupport,
                s.Funds.Select((f, i) => new AumFundBalance(f.Name, fundOpening[i], fundClosing[i])).ToArray())
                {
                    SupportBalances = supportRows, IncomeSupport = incomeSupport, Sleeves = new(eoFactor, ibFactor, eoGross),
                    OffshoreFees = offshoreFees, SaFees = saFees.ToArray()
                });
            if (month % 12 != 0) continue;
            var y = months.TakeLast(12).ToArray();
            years.Add(new(month / 12, end, closingTotal, y.Sum(x => x.Recurring), y.Sum(x => x.OffshorePdf),
                y.Sum(x => x.SaPdf), y.Sum(x => x.Costs), y.Sum(x => x.Support), y.Sum(x => x.Result)));
        }
        return new(months.ToArray(), years.ToArray());
    }

    private sealed class FeeState(AumFeeTerms terms, DateOnly start)
    {
        private double nav = terms.Nav, highWater = terms.HighWater, hurdleBase = terms.HurdleBase;
        private DateOnly quarterStart = start;
        private double fraction, receiptRatio;
        public AumFeeCheckpoint Checkpoint { get; private set; } = null!;

        public double Advance(double factor, DateOnly from, DateOnly end, double cpi, bool legacy)
        {
            var opening = nav;
            var openingHwm = highWater;
            var openingBase = hurdleBase;
            fraction = 0; receiptRatio = 0;
            if (from.Month % 3 != 0)
            {
                nav *= factor;
                if (legacy) hurdleBase += highWater * terms.Hurdle / 12;
                Checkpoint = new(opening, nav, openingHwm, openingBase, null, 0, null, null, null, highWater, hurdleBase);
                return nav / opening;
            }
            var quarter = new DateOnly(from.Year, from.Month, DateTime.DaysInMonth(from.Year, from.Month));
            fraction = (double)(quarter.DayNumber - from.DayNumber) / (end.DayNumber - from.DayNumber);
            var beforeFee = nav * Math.Pow(factor, fraction);
            var hurdle = legacy ? hurdleBase + highWater * terms.Hurdle / 12 * fraction
                : hurdleBase * Math.Pow(1 + cpi, Math.Max(0, quarter.DayNumber - quarterStart.DayNumber) / 365d);
            var excess = Math.Max(0, beforeFee - Math.Max(highWater, hurdle));
            var perUnit = excess * terms.Rate;
            if (legacy)
            {
                receiptRatio = beforeFee > 0 ? excess * terms.Rate / (1 - terms.Rate) / beforeFee : 0;
                if (excess > 0)
                {
                    highWater = Math.Max(highWater, beforeFee);
                    hurdleBase = highWater * (1 + terms.Hurdle / 12 * (1 - fraction));
                }
                else hurdleBase += highWater * terms.Hurdle / 12;
                nav *= factor;
            }
            else
            {
                var fee = perUnit; var afterFee = beforeFee - fee;
                receiptRatio = afterFee > 0 ? fee / afterFee : 0;
                if (fee > 0) highWater = Math.Max(highWater, afterFee);
                hurdleBase = afterFee; quarterStart = quarter;
                nav = afterFee * Math.Pow(factor, 1 - fraction);
            }
            Checkpoint = new(opening, nav, openingHwm, openingBase, quarter, fraction, beforeFee, hurdle,
                legacy ? perUnit / (1 - terms.Rate) : perUnit, highWater, hurdleBase);
            return nav / opening;
        }

        public double Receipt(double openingAum, double closingAum)
            => receiptRatio * (openingAum + (closingAum - openingAum) * fraction);
    }
}
