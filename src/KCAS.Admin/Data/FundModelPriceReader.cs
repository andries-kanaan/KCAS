using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Xml.Linq;

namespace KCAS.Admin.Data;

public static class FundModelPriceReader
{
    public static List<ModelObservation> Read(byte[] bytes, string fileName)
    {
        if (bytes.Length > 10*1024*1024) throw new ValidationException("Price file exceeds 10 MB.");
        if (Path.GetExtension(fileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)) return FundModelService.ParsePrices(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
        if (!Path.GetExtension(fileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) throw new ValidationException("Use CSV or XLSX.");
        using var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read);
        if(zip.Entries.Sum(x=>x.Length)>30*1024*1024) throw new ValidationException("Expanded workbook exceeds 30 MB.");
        XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XDocument Xml(string path) { var entry=zip.GetEntry(path)??throw new ValidationException("Missing worksheet part: "+path);using var stream=entry.Open();return XDocument.Load(stream); }
        var shared=zip.GetEntry("xl/sharedStrings.xml") is null?[]:Xml("xl/sharedStrings.xml").Descendants(ns+"si").Select(x=>string.Concat(x.Descendants(ns+"t").Select(t=>t.Value))).ToArray();
        var workbook=Xml("xl/workbook.xml");
        var first=workbook.Descendants(ns+"sheet").FirstOrDefault()??throw new ValidationException("Workbook has no sheets.");
        var relId=(string?)first.Attribute(XName.Get("id","http://schemas.openxmlformats.org/officeDocument/2006/relationships"));
        var target=Xml("xl/_rels/workbook.xml.rels").Root!.Elements().Single(x=>(string?)x.Attribute("Id")==relId).Attribute("Target")!.Value;
        var path=target.StartsWith('/')?target.TrimStart('/') : "xl/"+target;
        if(path.Contains(".."))throw new ValidationException("Unsupported workbook relationship.");
        var csv=new System.Text.StringBuilder();
        foreach(var row in Xml(path).Descendants(ns+"row"))
        {
            var values=new string[4];
            foreach(var cell in row.Elements(ns+"c"))
            {
                var address=(string?)cell.Attribute("r")??"";
                var column=new string(address.TakeWhile(char.IsLetter).ToArray());
                if(column.Length!=1 || column[0]<'A' || column[0]>'D')continue;
                var type=(string?)cell.Attribute("t");var value=cell.Element(ns+"v")?.Value??"";
                if(type=="s")value=shared[int.Parse(value)];
                else if(type=="inlineStr")value=string.Concat(cell.Descendants(ns+"t").Select(x=>x.Value));
                if(column=="A" && type is not "s" and not "inlineStr" && double.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var serial))value=DateTime.FromOADate(serial).ToString("yyyy-MM");
                values[column[0]-'A']=value;
            }
            if(values.All(string.IsNullOrWhiteSpace))continue;
            csv.AppendLine(string.Join(",",values));
        }
        return FundModelService.ParsePrices(csv.ToString());
    }
}
