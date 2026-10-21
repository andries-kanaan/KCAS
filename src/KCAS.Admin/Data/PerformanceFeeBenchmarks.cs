using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;

namespace KCAS.Admin.Data;

public sealed record FeeCpiObservation(DateOnly Month, DateOnly PublishedOn, decimal AnnualPercent, string Reference);

public static class FeePublishedCpi
{
    public static List<FeeBenchmarkPeriod> Periods(IEnumerable<FeeCpiObservation> observations, DateOnly from, DateOnly to)
    {
        var rates = observations.OrderBy(x => x.PublishedOn).ToList();
        if (rates.GroupBy(x => x.PublishedOn).Any(g => g.Select(x => (x.Month, x.AnnualPercent)).Distinct().Count() != 1))
            throw new ValidationException("Conflicting CPI observations share a publication date; resolve the benchmark register.");
        var periods = new List<FeeBenchmarkPeriod>();
        var cursor = from;
        while (cursor < to)
        {
            var rate = rates.LastOrDefault(x => x.PublishedOn <= cursor);
            if (rate is null || cursor.DayNumber - rate.PublishedOn.DayNumber > 75)
                throw new ValidationException($"CPI benchmark register needs a published observation covering {cursor:yyyy-MM-dd}; no zero-rate or projection fallback was used.");
            var next = rates.FirstOrDefault(x => x.PublishedOn > cursor)?.PublishedOn ?? to;
            var following = rates.FirstOrDefault(x => x.PublishedOn > cursor);
            if (following is not null && following.PublishedOn < to && following.Month > rate.Month.AddMonths(1))
                throw new ValidationException($"Missing monthly CPI source between {rate.Month:yyyy-MM} and {following.Month:yyyy-MM}; no unchanged-rate assumption was used.");
            var end = next < to ? next : to;
            if (end.DayNumber - rate.PublishedOn.DayNumber > 75)
                throw new ValidationException($"CPI benchmark is stale before {end:yyyy-MM-dd}; add the missing published release.");
            periods.Add(new() { From = cursor, To = end, CpiPercent = rate.AnnualPercent,
                Reference = $"SA headline CPI {rate.Month:yyyy-MM}; published {rate.PublishedOn:yyyy-MM-dd}; latest published by accrual date; {rate.Reference}" });
            cursor = end;
        }
        return periods;
    }

    public static List<FeeCpiObservation> ReadCsv(string text)
    {
        if (text.Length > 500000) throw new ValidationException("CPI upload is too large.");
        using var parser = new TextFieldParser(new StringReader(text)) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var header = parser.ReadFields();
        if (header is null || !header.SequenceEqual(new[] { "Month", "PublishedOn", "AnnualPercent", "Reference" }, StringComparer.OrdinalIgnoreCase))
            throw new ValidationException("Use CSV columns Month,PublishedOn,AnnualPercent,Reference (ISO dates, percentage points without adding the mandate spread).");
        var rows = new List<FeeCpiObservation>();
        while (!parser.EndOfData)
        {
            var values = parser.ReadFields();
            if (values is not { Length: 4 } || !DateOnly.TryParseExact(values[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month) || month.Day != 1 ||
                !DateOnly.TryParseExact(values[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var published) ||
                !decimal.TryParse(values[2], NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var percent) ||
                month.Year < 1990 || published <= month || published > DateOnly.FromDateTime(DateTime.Today) || percent is <= -100 or > 100 || string.IsNullOrWhiteSpace(values[3]) || values[3].Length > 2000)
                throw new ValidationException($"Invalid CPI source row {parser.LineNumber}; use actual publication dates and sourced headline rates, not CPI-plus-spread values.");
            rows.Add(new(month, published, percent, values[3].Trim()));
        }
        if (rows.Count is 0 or > 1000 || rows.GroupBy(x => (x.Month, x.PublishedOn)).Any(g => g.Count() > 1)) throw new ValidationException("Provide 1-1000 distinct CPI observations.");
        return rows;
    }
}

public sealed partial class PerformanceFeeService
{
    private const string CpiCategory = "PerformanceFeeCpi";

    public async Task<List<FeeCpiObservation>> BenchmarksAsync(ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); await Actor(db, principal, false);
        return await CpiObservations(db);
    }

    public async Task ImportBenchmarksAsync(string csv, ClaimsPrincipal principal)
    {
        var observations = FeePublishedCpi.ReadCsv(csv);
        await using var db = await factory.CreateDbContextAsync(); var actor = await Actor(db, principal, true);
        var existing = await CpiObservations(db);
        foreach (var observation in observations)
        {
            var same = existing.SingleOrDefault(x => x.Month == observation.Month && x.PublishedOn == observation.PublishedOn);
            if (same is not null)
            {
                if (same != observation) throw new ValidationException("An existing published CPI source differs. Do not overwrite it; record a sourced correction with its actual later publication date.");
                continue;
            }
            if (existing.Concat(observations).Any(x => x.PublishedOn == observation.PublishedOn && (x.Month != observation.Month || x.AnnualPercent != observation.AnnualPercent)))
                throw new ValidationException("Resolve conflicting CPI observations on the same publication date.");
            db.ComplianceReferenceValues.Add(new() { Category = CpiCategory, Code = $"{observation.Month:yyyyMM}-{observation.PublishedOn:yyyyMMdd}",
                Name = $"SA headline CPI {observation.Month:yyyy-MM}", Description = FeeJson.Write(observation), UpdatedBy = ComplianceWorkflowAccess.Label(actor), UpdatedAtUtc = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();
    }

    private static async Task<List<FeeCpiObservation>> CpiObservations(ApplicationDbContext db) =>
        (await db.ComplianceReferenceValues.AsNoTracking().Where(x => x.Category == CpiCategory && x.IsActive).Select(x => x.Description!).ToListAsync())
        .Select(x => System.Text.Json.JsonSerializer.Deserialize<FeeCpiObservation>(x, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!).OrderBy(x => x.PublishedOn).ToList();

    private static void ApplyBenchmarks(FeePreparation input, List<FeeCpiObservation> rates)
    {
        foreach (var child in input.Wraps.Count > 0 ? input.Wraps.Select(x => x.Input) : new[] { input })
        {
            if (child.FeePercent == 0 || child.From is not { } from || child.To is not { } to) continue;
            if (!child.UsePublishedCpi && child.Benchmarks.Count > 0)
            {
                child.Benchmarks = child.Benchmarks.Where(x => x.To > from && x.From < to).Select(x => new FeeBenchmarkPeriod
                { From = x.From < from ? from : x.From, To = x.To > to ? to : x.To, CpiPercent = x.CpiPercent, Reference = x.Reference }).ToList();
                continue;
            }
            child.UsePublishedCpi = true; child.Benchmarks.Clear();
            try { child.Benchmarks = FeePublishedCpi.Periods(rates, from, to); }
            catch (ValidationException ex) { child.EvidenceIssues.Add(ex.Message); }
        }
    }

    private static async Task ValidateBenchmarkSnapshot(ApplicationDbContext db, FeePreparation input)
    {
        var scopes = input.Wraps.Count > 0 ? input.Wraps.Select(x => x.Input) : new[] { input };
        var automatic = scopes.Where(x => x.UsePublishedCpi && x.FeePercent != 0).ToList();
        if (automatic.Count == 0) return;
        var rates = await CpiObservations(db);
        foreach (var child in automatic)
            if (child.From is not { } from || child.To is not { } to || FeeJson.Write(child.Benchmarks) != FeeJson.Write(FeePublishedCpi.Periods(rates, from, to)))
                throw new ValidationException("Published CPI sources changed for this period. Prepare the calculations again before KI approval.");
    }
}
