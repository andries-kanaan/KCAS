using System.Globalization;

namespace KCAS.Admin.Data;

public static class FundModelPresentation
{
    public static (string From, string To)? SavedPeriod(IEnumerable<ModelObservation> observations, int? months)
    {
        var rows=observations.OrderBy(x=>x.Month,StringComparer.Ordinal).ToList();
        if(rows.Count==0)return null;
        var to=rows[^1].Month;
        if(months is null)return(rows[0].Month,to);
        if(months<=0 || !DateTime.TryParseExact(to,"yyyy-MM",CultureInfo.InvariantCulture,DateTimeStyles.None,out var end))return null;
        var from=end.AddMonths(-months.Value).ToString("yyyy-MM",CultureInfo.InvariantCulture);
        return rows.Any(x=>x.Month==from)?(from,to):null;
    }
    public sealed record PeriodReturn(double Growth, double Car, int Months, double Opening, double Closing);
    public static PeriodReturn? ReturnSince(IEnumerable<ModelObservation> observations, string from, string to, bool totalReturn)
    {
        if (!DateTime.TryParseExact(from, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !DateTime.TryParseExact(to, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)) return null;
        var months = (end.Year-start.Year)*12+end.Month-start.Month;
        if (months <= 0) return null;
        var first = observations.SingleOrDefault(x => x.Month == from);
        var last = observations.SingleOrDefault(x => x.Month == to);
        var opening = totalReturn ? first?.TotalReturnIndex : first?.Price;
        var closing = totalReturn ? last?.TotalReturnIndex : last?.Price;
        if (opening is not > 0 || closing is not > 0 || !double.IsFinite(opening.Value) || !double.IsFinite(closing.Value)) return null;
        var ratio = closing.Value/opening.Value;
        var car = Math.Pow(ratio,12d/months)-1;
        return double.IsFinite(car) ? new(ratio-1,car,months,opening.Value,closing.Value) : null;
    }
    public static double? MonthlyReturn(IEnumerable<ModelObservation> observations, string month, bool totalReturn)
    {
        var date = DateTime.ParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture);
        var previous = date.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var current = observations.SingleOrDefault(x => x.Month == month);
        var prior = observations.SingleOrDefault(x => x.Month == previous);
        var close = totalReturn ? current?.TotalReturnIndex : current?.Price;
        var open = totalReturn ? prior?.TotalReturnIndex : prior?.Price;
        return close is > 0 && open is > 0 ? close / open - 1 : null;
    }
    public static (double Minimum, double Maximum, double Step) Axis(IEnumerable<double> values, bool nonnegative = false)
    {
        var points = values.Where(double.IsFinite).Append(0).ToArray();
        var low = nonnegative ? 0 : points.Min(); var high = points.Max();
        var rough = Math.Max(.01, high-low)/5;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var fraction = rough/magnitude;
        var step = (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10)*magnitude;
        var minimum = Math.Floor(low/step)*step;
        var maximum = Math.Ceiling(high/step)*step;
        if (maximum <= minimum) maximum = minimum+step*5;
        return (minimum,maximum,step);
    }
}
