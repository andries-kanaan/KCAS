using System.Globalization;

namespace KCAS.Admin.Data;

public static class FundModelPresentation
{
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
