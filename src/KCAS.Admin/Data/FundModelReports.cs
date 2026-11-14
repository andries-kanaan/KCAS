using System.Globalization;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Xml.Linq;
using KCAS.Admin.Security;

namespace KCAS.Admin.Data;

public static class FundModelReports
{
    public static void MapFundModelReports(this WebApplication app)
    {
        app.MapGet("/fund-models/runs/{id:long}/report.pdf", async (long id, FundModelService service, HttpContext context) =>
        {
            var run = await service.RunAsync(id, context.User);
            if (run.State != "Complete") return Results.BadRequest("Run is not complete.");
            return Results.File(Pdf(run), "application/pdf", $"KCAS-fund-model-{id}.pdf");
        }).RequireAuthorization(KcasPermissions.InvestmentsView);
        app.MapGet("/fund-models/runs/{id:long}/report.csv", async (long id, FundModelService service, HttpContext context) =>
        {
            var run = await service.RunAsync(id, context.User);
            if (run.State != "Complete") return Results.BadRequest("Run is not complete.");
            return Results.File(Encoding.UTF8.GetBytes(Csv(run)), "text/csv", $"KCAS-fund-model-{id}.csv");
        }).RequireAuthorization(KcasPermissions.InvestmentsView);
        app.MapGet("/fund-models/runs/{id:long}/report.xlsx", async (long id, FundModelService service, HttpContext context) =>
        {
            var run = await service.RunAsync(id, context.User);
            if (run.State != "Complete") return Results.BadRequest("Run is not complete.");
            return Results.File(Excel(run), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"KCAS-fund-model-{id}.xlsx");
        }).RequireAuthorization(KcasPermissions.InvestmentsView);
        app.MapGet("/fund-models/runs/{id:long}/snapshot.json", async (long id, FundModelService service, HttpContext context) =>
        {
            var run = await service.RunAsync(id, context.User);
            return Results.File(Encoding.UTF8.GetBytes(FundModelJson.Write(new {run.Key, run.Actor, run.CreatedUtc, run.Fingerprint, inputs=JsonSerializer.Deserialize<JsonElement>(run.InputJson), results=JsonSerializer.Deserialize<JsonElement>(run.ResultJson)})), "application/json", $"KCAS-fund-model-{id}.json");
        }).RequireAuthorization(KcasPermissions.InvestmentsView);
    }
    private static string P(JsonElement x) => x.ValueKind == JsonValueKind.Number ? x.GetDouble().ToString("P2", CultureInfo.InvariantCulture) : "-";
    public static byte[] Pdf(ModelRun run)
    {
        using var input = JsonDocument.Parse(run.InputJson); using var result = JsonDocument.Parse(run.ResultJson);
        var r = result.RootElement; var s = input.RootElement.GetProperty("scenario");
        var writer = new InvestmentSummaryService.SimplePdfWriter("Kanaan fund modelling", input.RootElement.GetProperty("name").GetString()!, "RESEARCH - NOT TRADE AUTHORITY");
        writer.WriteReportHeader(input.RootElement.GetProperty("name").GetString()!, s.GetProperty("name").GetString()!, [("Run", run.Id.ToString()),("Created UTC",run.CreatedUtc.ToString("yyyy-MM-dd HH:mm")),("Prepared by",run.Actor)]);
        writer.WriteParagraph($"Period: {s.GetProperty("start")} to {s.GetProperty("end")}. Basis: {s.GetProperty("basis")}. Volatility ceiling: {P(s.GetProperty("risk"))}.");
        writer.WriteParagraph($"Total return {P(r.GetProperty("totalReturn"))}; annualised {P(r.GetProperty("cagr"))}; realised volatility {P(r.GetProperty("volatility"))}; worst drawdown {P(r.GetProperty("maxDrawdown"))}.");
        writer.WriteSection("Assumptions and limitations");
        writer.WriteParagraph($"Holding cap {P(s.GetProperty("cap"))}; history {s.GetProperty("window")} / minimum {s.GetProperty("minimumHistory")} months; mean shrinkage {P(s.GetProperty("meanShrink"))}; covariance shrinkage {P(s.GetProperty("covShrink"))}; trading allowance {P(s.GetProperty("cost"))}.");
        foreach(var warning in r.GetProperty("warnings").EnumerateArray()) writer.WriteParagraph(warning.GetString()!);
        var keys = r.GetProperty("keys").EnumerateArray().Select(x=>x.GetString()).ToArray();
        foreach(var a in r.GetProperty("annual").EnumerateArray())
        {
            writer.WriteSection($"Allocation at {a.GetProperty("month")}");
            writer.WriteParagraph($"Expected return {P(a.GetProperty("expectedReturn"))}; estimated volatility {P(a.GetProperty("estimatedRisk"))}.");
            for(var i=0;i<keys.Length;i++) writer.WriteParagraph($"{keys[i]}: {P(a.GetProperty("weights")[i])}");
            foreach(var e in a.GetProperty("excluded").EnumerateArray()) writer.WriteParagraph($"Excluded {e.GetProperty("security")}: {e.GetProperty("reason")}");
        }
        writer.WriteSection("Annual achieved returns");
        foreach(var group in r.GetProperty("monthly").EnumerateArray().GroupBy(m=>m.GetProperty("month").GetString()![..4]))
            writer.WriteParagraph($"{group.Key} ({group.Count()} months): model {group.Aggregate(1d,(a,m)=>a*(1+m.GetProperty("net").GetDouble()))-1:P2}; equal mix {group.Aggregate(1d,(a,m)=>a*(1+m.GetProperty("equalNet").GetDouble()))-1:P2}");
        writer.WriteMuted("Input fingerprint: "+run.Fingerprint);
        return writer.Build();
    }
    public static string Csv(ModelRun run)
    {
        using var result=JsonDocument.Parse(run.ResultJson);var b=new StringBuilder("Month,Gross,TradingAllowance,Net,EqualMix,Index,Drawdown\r\n");
        foreach(var m in result.RootElement.GetProperty("monthly").EnumerateArray()) b.AppendLine(string.Join(",",new[]{"month","gross","cost","net","equalNet","wealth","drawdown"}.Select(k=>m.GetProperty(k).ToString())));
        return b.ToString();
    }
    public static byte[] Excel(ModelRun run)
    {
        using var result=JsonDocument.Parse(run.ResultJson);using var input=JsonDocument.Parse(run.InputJson);
        var r=result.RootElement;
        var sheets=new List<(string Name,List<object?[]> Rows)>();
        sheets.Add(("Summary",new() {new object?[]{"Kanaan fund modelling",input.RootElement.GetProperty("name").GetString()},new object?[]{"Scenario",input.RootElement.GetProperty("scenario").GetProperty("name").GetString()},new object?[]{"Run",run.Id},new object?[]{"Input fingerprint",run.Fingerprint},new object?[]{"Total return",r.GetProperty("totalReturn").GetDouble()},new object?[]{"CAGR",r.GetProperty("cagr").ValueKind==JsonValueKind.Number?r.GetProperty("cagr").GetDouble():null}}));
        foreach(var warning in r.GetProperty("warnings").EnumerateArray()) sheets[0].Rows.Add(new object?[]{warning.GetString()});
        var monthly=new List<object?[]> {new object?[]{"Month","Gross","Allowance","Net","Equal mix","Index","Drawdown"}};
        foreach(var m in r.GetProperty("monthly").EnumerateArray()) monthly.Add(new object?[]{m.GetProperty("month").GetString(),m.GetProperty("gross").GetDouble(),m.GetProperty("cost").GetDouble(),m.GetProperty("net").GetDouble(),m.GetProperty("equalNet").GetDouble(),m.GetProperty("wealth").GetDouble(),m.GetProperty("drawdown").GetDouble()});
        sheets.Add(("Monthly returns",monthly));
        var weights=new List<object?[]> {new object?[]{"Decision","Security","Weight","History months"}};
        foreach(var a in r.GetProperty("annual").EnumerateArray()) for(var i=0;i<r.GetProperty("keys").GetArrayLength();i++) weights.Add(new object?[]{a.GetProperty("month").GetString(),r.GetProperty("keys")[i].GetString(),a.GetProperty("weights")[i].GetDouble(),i<a.GetProperty("historyMonths").GetArrayLength()?a.GetProperty("historyMonths")[i].GetInt32():null});
        sheets.Add(("Allocations",weights));
        var prices=new List<object?[]> {new object?[]{"Security","Currency","Month","Price","Total-return index","Source"}};
        foreach(var security in input.RootElement.GetProperty("securities").EnumerateArray()) foreach(var p in security.GetProperty("series").EnumerateArray()) prices.Add(new object?[]{security.GetProperty("key").GetString(),security.GetProperty("currency").GetString(),p.GetProperty("month").GetString(),p.GetProperty("price").GetDouble(),p.GetProperty("totalReturnIndex").ValueKind==JsonValueKind.Number?p.GetProperty("totalReturnIndex").GetDouble():null,p.GetProperty("source").GetString()});
        sheets.Add(("Source observations",prices));
        return ModelWorkbook.Write(sheets);
    }
}

internal static class ModelWorkbook
{
    internal static byte[] Write(List<(string Name,List<object?[]> Rows)> sheets)
    {
        XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main",rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships",pkg="http://schemas.openxmlformats.org/package/2006/relationships",types="http://schemas.openxmlformats.org/package/2006/content-types";
        using var output=new MemoryStream();
        using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))
        {
            void Add(string name,XElement doc) {using var stream=zip.CreateEntry(name).Open();doc.Save(stream);}
            Add("[Content_Types].xml",new XElement(types+"Types",new XElement(types+"Default",new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),new XElement(types+"Default",new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")),new XElement(types+"Override",new XAttribute("PartName","/xl/workbook.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),sheets.Select((s,i)=>new XElement(types+"Override",new XAttribute("PartName",$"/xl/worksheets/sheet{i+1}.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
            Add("_rels/.rels",new XElement(pkg+"Relationships",new XElement(pkg+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type",rel.NamespaceName+"/officeDocument"),new XAttribute("Target","xl/workbook.xml"))));
            Add("xl/workbook.xml",new XElement(ns+"workbook",new XElement(ns+"sheets",sheets.Select((s,i)=>new XElement(ns+"sheet",new XAttribute("name",s.Name),new XAttribute("sheetId",i+1),new XAttribute(rel+"id","rId"+(i+1)))))));
            Add("xl/_rels/workbook.xml.rels",new XElement(pkg+"Relationships",sheets.Select((s,i)=>new XElement(pkg+"Relationship",new XAttribute("Id","rId"+(i+1)),new XAttribute("Type",rel.NamespaceName+"/worksheet"),new XAttribute("Target",$"worksheets/sheet{i+1}.xml")))));
            XElement Cell(object? value) => value is string text
                ? new XElement(ns+"c",new XAttribute("t","inlineStr"),new XElement(ns+"is",new XElement(ns+"t",text)))
                : new XElement(ns+"c",new XElement(ns+"v",value is null?"":Convert.ToString(value,CultureInfo.InvariantCulture)));
            for(var i=0;i<sheets.Count;i++)
            {
                var data = new XElement(ns+"sheetData",sheets[i].Rows.Select((row,j) =>
                    new XElement(ns+"row",new XAttribute("r",j+1),row.Select(Cell))));
                var columns = new XElement(ns+"cols",new XElement(ns+"col",new XAttribute("min",1),new XAttribute("max",12),new XAttribute("width",24),new XAttribute("customWidth",1)));
                Add($"xl/worksheets/sheet{i+1}.xml",new XElement(ns+"worksheet",columns,data));
            }
        }
        return output.ToArray();
    }
}
