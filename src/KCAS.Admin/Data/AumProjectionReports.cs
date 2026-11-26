using System.Globalization;
using KCAS.Admin.Security;

namespace KCAS.Admin.Data;

public static class AumProjectionReports
{
    public static void MapAumProjectionReports(this WebApplication app)
    {
        app.MapGet("/aum-projections/runs/{id:long}/{format}", async Task<IResult>
            (long id, string format, AumProjectionService service, HttpContext context) =>
        {
            try
            {
                if (format is not ("xlsx" or "pdf")) return Results.NotFound();
                var run = await service.LoadAsync(id, context.User);
                if (run is null) return Results.NotFound();
                context.Response.Headers.CacheControl = "private, no-store";
                return format == "xlsx"
                    ? Results.File(Excel(run), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"KCAS-AUM-{id}.xlsx")
                    : Results.File(Pdf(run), "application/pdf", $"KCAS-AUM-{id}.pdf");
            }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
        }).RequireAuthorization(KcasPolicies.AdministratorOnly);
    }

    internal static byte[] Excel(AumSavedProjection run)
    {
        return ModelWorkbook.Write(AumProjectionTables.SheetNames.Select(name =>
        {
            var rows = new List<object?[]>();
            if (name == AumProjectionTables.Business)
            {
                rows.Add([new ModelWorkbook.Caption("Kanaan AUM and business projection")]);
                rows.Add(["Scenario", run.Name, "Snapshot", run.AsOf.ToString("yyyy-MM-dd"), "Currency", "ZAR"]);
                rows.Add(["Projection only; not a fee claim."]);
                rows.Add([]);
            }
            else if (name == AumProjectionTables.Inputs)
            {
                rows.Add(["Source workbook", run.Source]);
                rows.Add(["Source SHA-256", run.SourceHash]);
                rows.Add([]);
            }
            foreach (var table in AumProjectionTables.For(run, name))
            {
                if (!string.IsNullOrEmpty(table.Title)) rows.Add([new ModelWorkbook.Caption(table.Title)]);
                rows.Add(table.Headings.Select(h => (object?)new ModelWorkbook.Caption(h)).ToArray());
                foreach (var row in table.Rows)
                    rows.Add(row.Select(WorkbookValue).ToArray());
                rows.Add([]);
            }
            return (name, rows);
        }).ToList(), formatted: true);
    }

    private static object? WorkbookValue(object? value) => value switch
    {
        AumPercent p => new ModelWorkbook.Number(p.Value, 2),
        AumDecimal d => new ModelWorkbook.Number(d.Value, 3),
        double d => new ModelWorkbook.Number(d, 1),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => value
    };

    internal static byte[] Pdf(AumSavedProjection run)
    {
        var writer = new InvestmentSummaryService.SimplePdfWriter("Kanaan AUM projection", run.Name, "PLANNING ONLY");
        writer.WriteReportHeader("Kanaan AUM projection", run.Name,
            [("Snapshot", run.AsOf.ToString("yyyy-MM-dd")), ("Currency", "ZAR")]);
        writer.WriteParagraph("EO and IBKR use separate return paths inside the existing EO wrapper. No merger or additional broker AUM is assumed.");
        writer.WriteParagraph(run.Overrides.Count == 0 ? "CAR assumptions: snapshot defaults." :
            "CAR overrides: " + string.Join("; ", run.Overrides.Select(r => $"{r.Key} {r.Value.ToString("0.####%", CultureInfo.InvariantCulture)}")) + ".");
        foreach (var (a, b) in run.Active.Years.Zip(run.Defaults.Years))
        {
            writer.WriteSection($"Year {a.Year}: {a.End:yyyy-MM-dd}");
            writer.WriteParagraph($"Closing AUM {Z(a.Closing)} (defaults {Z(b.Closing)}). Result after support {Z(a.Result)} (defaults {Z(b.Result)}).");
            writer.WriteParagraph($"Recurring fees {Z(a.Recurring)}; offshore performance fees {Z(a.OffshorePdf)}; estimated SA performance fees {Z(a.SaPdf)}; costs {Z(a.Costs)}; support {Z(a.Support)}.");
        }
        writer.WriteMuted("Projection only. Does not establish investment performance, actual fees or client instructions.");
        return writer.Build();
    }

    private static string Z(double amount) => "R " + amount.ToString("N0", CultureInfo.InvariantCulture);
}
