using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using KCAS.Admin.Security;
using Microsoft.EntityFrameworkCore;
using static KCAS.Admin.Data.ComplianceWorkflowAccess;

namespace KCAS.Admin.Data;

public sealed partial class ComplaintRegisterService
{
    public async Task<IReadOnlyList<LegacyComplaintPreview>> PreviewLegacyAsync(byte[] workbook, string sourceReference, ClaimsPrincipal principal)
    {
        await using var db = await factory.CreateDbContextAsync(); var actor = await ActorAsync(db, principal, KcasPermissions.ComplianceManage);
        if (!await IsAdminAsync(db, actor.Id)) throw new UnauthorizedAccessException("Administrator permission is required to preserve and match historical entries.");
        var rows = ComplaintWorkbook.Read(workbook, Required(sourceReference, "Source register reference", 512));
        var stored = await db.ComplaintCases.AsNoTracking().Where(x => x.LegacyKey != null).ToDictionaryAsync(x => x.LegacyKey!);
        return rows.Select(x => new LegacyComplaintPreview(x, stored.TryGetValue(x.Key, out var item) ? item.Id : null,
            stored.TryGetValue(x.Key, out item) && item.LegacyRowHash != x.RowHash)).ToList();
    }

    public async Task<int> ImportLegacyAsync(byte[] workbook, string sourceReference, string reason, ClaimsPrincipal principal)
        => await WriteAsync(principal, async (db, actor) =>
        {
            if (!await IsAdminAsync(db, actor.Id)) throw new UnauthorizedAccessException("Administrator permission is required for historical import.");
            var rows = ComplaintWorkbook.Read(workbook, Required(sourceReference, "Source register reference", 512));
            var stored = await db.ComplaintCases.Where(x => x.LegacyKey != null).ToDictionaryAsync(x => x.LegacyKey!);
            if (rows.Any(x => stored.TryGetValue(x.Key, out var item) && item.LegacyRowHash != x.RowHash))
                throw new ValidationException("A previously preserved source row changed. Review that case rather than overwriting its history or importing a second version as a new complaint.");
            var added = 0;
            foreach (var row in rows.Where(x => !stored.ContainsKey(x.Key)))
            {
                var item = new ComplaintCase { ComplainantName = row.Values.GetValueOrDefault("Client", "Unidentified historical complainant"),
                    Allegation = row.Values.GetValueOrDefault("Nature", ""), SecondaryThemes = row.Values.GetValueOrDefault("TCF Outcome", ""),
                    ContactDetails = "Not recorded in the source register; requires review.", Channel = "Other", HandlerUserId = actor.Id,
                    Status = "NeedsReview", IsReportable = false, ReceivedAtUtc = row.ReceivedAtUtc, LegacyKey = row.Key, LegacyRowHash = row.RowHash,
                    LegacySourceJson = JsonSerializer.Serialize(new { sourceReference, row.Sheet, row.Row, row.Values }, new JsonSerializerOptions { WriteIndented = true }), UpdatedBy = Label(actor) };
                // Only an exact unique name match can restore the client's existing visibility boundary.
                var clients = await db.Clients.Where(x => x.DisplayName == item.ComplainantName).OrderBy(x => x.Id).Take(2).ToListAsync();
                if (clients.Count == 1) item.ClientId = clients[0].Id;
                db.ComplaintCases.Add(item); await db.SaveChangesAsync(); await EnsureTaskAsync(db, item, actor);
                db.ComplaintEvents.Add(new() { ComplaintCaseId = item.Id, Kind = "SourcePreserved", OccurredAtUtc = DateTime.UtcNow,
                    Details = "Historical source entry preserved without inferring classification, acknowledgement, decision, payment or closure.\n" + row.Values.GetValueOrDefault("Details", ""),
                    EvidenceReference = sourceReference + ": " + row.Sheet + " row " + row.Row, RecordedBy = Label(actor) });
                Audit(db, nameof(ComplaintCase), item.Id, "HistoricalEntryPreserved", actor, reason, new { row.Key, row.RowHash, sourceReference, row.Sheet, row.Row });
                added++;
            }
            return added;
        });
}

public sealed record LegacyComplaintRow(string Key, string RowHash, string Sheet, int Row, Dictionary<string, string> Values, DateTime? ReceivedAtUtc);
public sealed record LegacyComplaintPreview(LegacyComplaintRow Source, int? ExistingCaseId, bool Changed);

internal static class ComplaintWorkbook
{
    public static IReadOnlyList<LegacyComplaintRow> Read(byte[] content, string source)
    {
        if (content.Length > 8 * 1024 * 1024) throw new ValidationException("Use an XLSX register no larger than 8 MB.");
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var strings = archive.GetEntry("xl/sharedStrings.xml") is { } shared ? ReadXml(shared).Descendants(ns + "si").Select(x => string.Concat(x.Descendants(ns + "t").Select(t => t.Value))).ToList() : [];
            var workbook = ReadXml(archive.GetEntry("xl/workbook.xml") ?? throw new ValidationException("This is not a supported XLSX workbook."));
            var relationships = ReadXml(archive.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new ValidationException("Workbook relationships are missing."));
            var results = new List<LegacyComplaintRow>();
            var foundHeaders = false;
            var date1904 = (string?)workbook.Root?.Element(ns + "workbookPr")?.Attribute("date1904") is "1" or "true";
            foreach (var sheet in workbook.Descendants(ns + "sheet"))
            {
                var relation = relationships.Root!.Elements().Single(x => (string?)x.Attribute("Id") == (string?)sheet.Attribute(rel + "id"));
                if ((string?)relation.Attribute("TargetMode") == "External") throw new ValidationException("External worksheets are not supported.");
                var target = ((string?)relation.Attribute("Target") ?? "").Replace('\\', '/');
                var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                if (path.Split('/').Contains("..")) throw new ValidationException("Invalid worksheet relationship.");
                var xml = ReadXml(archive.GetEntry(path) ?? throw new ValidationException("Worksheet not found."));
                var sheetName = (string?)sheet.Attribute("name") ?? "Sheet";
                Dictionary<string, string>? headers = null;
                foreach (var row in xml.Descendants(ns + "row"))
                {
                    var cells = row.Elements(ns + "c").ToDictionary(x => new string(((string?)x.Attribute("r") ?? "").TakeWhile(char.IsLetter).ToArray()), x => Cell(x));
                    if (headers is null)
                    {
                        if (new[] { "Date", "Client", "Nature", "TCF Outcome", "Details" }.All(h => cells.Values.Any(v => string.Equals(v.Trim(), h, StringComparison.OrdinalIgnoreCase))))
                        {
                            headers = cells.Where(x => new[] { "Date", "Client", "Nature", "TCF Outcome", "Details" }.Any(h => string.Equals(h, x.Value.Trim(), StringComparison.OrdinalIgnoreCase)))
                                .ToDictionary(x => x.Key, x => new[] { "Date", "Client", "Nature", "TCF Outcome", "Details" }.First(h => string.Equals(h, x.Value.Trim(), StringComparison.OrdinalIgnoreCase)));
                            foundHeaders = true;
                        }
                        continue;
                    }
                    var values = headers.Where(x => x.Value != "").ToDictionary(x => x.Value, x => cells.GetValueOrDefault(x.Key, ""));
                    if (values.Values.All(string.IsNullOrWhiteSpace)) continue;
                    var number = (int?)row.Attribute("r") ?? throw new ValidationException("A source row number is missing.");
                    var key = Hash(source + "|" + sheetName + "|" + number);
                    var hash = Hash(JsonSerializer.Serialize(values.OrderBy(x => x.Key)));
                    var date = values.GetValueOrDefault("Date", ""); DateTime? received = null;
                    if (double.TryParse(date, NumberStyles.Float, CultureInfo.InvariantCulture, out var oa) && oa is > 36525 and < 100000)
                        received = DateTime.SpecifyKind(DateTime.FromOADate(oa + (date1904 ? 1462 : 0)), DateTimeKind.Local).ToUniversalTime();
                    else if (DateTime.TryParseExact(date, ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd MMM yyyy", "dd-MM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                        received = DateTime.SpecifyKind(parsed, DateTimeKind.Local).ToUniversalTime();
                    results.Add(new(key, hash, sheetName, number, values, received));
                    if (results.Count > 5000) throw new ValidationException("The register exceeds 5,000 source entries.");
                }
            }
            if (!foundHeaders) throw new ValidationException("Date, Client, Nature, TCF Outcome and Details headers were not found.");
            return results;
            string Cell(XElement cell)
            {
                var value = cell.Element(ns + "v")?.Value ?? "";
                return (string?)cell.Attribute("t") switch { "s" => strings[int.Parse(value, CultureInfo.InvariantCulture)], "inlineStr" => string.Concat(cell.Descendants(ns + "t").Select(x => x.Value)), _ => value };
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or FormatException or ArgumentException or IndexOutOfRangeException)
        { throw new ValidationException("The XLSX register could not be read safely: " + ex.Message); }
    }
    private static XDocument ReadXml(ZipArchiveEntry entry)
    {
        if (entry.Length > 20 * 1024 * 1024) throw new ValidationException("A workbook XML part is too large.");
        using var stream = entry.Open(); using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 20 * 1024 * 1024 });
        return XDocument.Load(reader);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
