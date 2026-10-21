using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace KCAS.Admin.Data;

public sealed class FeeStatementDocument
{
    public int ParserVersion { get; set; }
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public List<FeeStatementRow> Rows { get; set; } = [];
    public List<string> Issues { get; set; } = [];
    public List<FeeStatementPeriod> Periods { get; set; } = [];
}

public sealed class FeeStatementPeriod
{
    public string Account { get; set; } = "";
    public DateOnly From { get; set; }
    public DateOnly? To { get; set; }
    public decimal OpeningValue { get; set; }
    public decimal? ClosingValue { get; set; }
    public string OpeningReference { get; set; } = "";
    public string ClosingReference { get; set; } = "";
    public List<FeeSummaryEntry> Entries { get; set; } = [];
}
public sealed record FeeSummaryEntry(string Description, decimal Amount, string Reference);

public sealed class FeeStatementRow
{
    public string Key { get; set; } = "";
    public string Account { get; set; } = "";
    public string Wrap { get; set; } = "";
    public string Holding { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateOnly? PriceDate { get; set; }
    public string Detail { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal Units { get; set; }
    public decimal UnitBalance { get; set; }
    public decimal Price { get; set; }
    public string Kind { get; set; } = "Unknown";
    public string Source { get; set; } = "";
    public int SourceRow { get; set; }
}

public static class FeeStatementKinds
{
    public static readonly string[] Reviewable = ["Contribution", "Withdrawal", "FundAllocation", "Reinvestment", "Expense", "InternalSwitch", "IncomeTransfer", "Transfer", "PerformanceFee", "Unknown"];
    public static string Classify(string detail)
    {
        var s = detail.Trim().ToLowerInvariant();
        if (s.Contains("opening balance") || s.Contains("openingsbalans")) return "Opening";
        if (s.Contains("closing balance") || s.Contains("sluitingsbalans")) return "Closing";
        if (s.Contains("performance") || s.Contains("prestasie") || Regex.IsMatch(s, @"\bpdf\b")) return "PerformanceFee";
        if (s.Contains("reinvest") || s.Contains("herbelegging")) return "Reinvestment";
        if (s.Contains("switch") || s.Contains("omskakeling") || s.Contains("rebalance") || s.Contains("herbalansering")) return "InternalSwitch";
        if (s.Contains("transfer") || s.Contains("oorplasing") || s.Contains("cash sweep") || s.Contains("kontantaansuiwering")) return "Transfer";
        if (s.Contains("income payment") || s.Contains("inkomstebetaling") || s.Contains("paye") || s.Contains("lbs-belasting") || s.Contains("redemption") || s.Contains("repurchase") || s.Contains("onttrekking") || s.Contains("tax deduction")) return "Withdrawal";
        if (s.Contains("investment") || s.Contains("belegging") || s.Contains("debit order") || s.Contains("debietorder")) return "Contribution";
        if (s.Contains("fee") || s.Contains("fooi") || s.Contains("charge") || s.Contains("koste") || s.Contains("expense") || s.Contains("onkoste") || s.Contains("commission adjustment") || s.Contains("vat on financial advisor commission")) return "Expense";
        return "Unknown";
    }
}

public static class PerformanceFeeStatementReader
{
    public const int CurrentVersion = 2;
    public const int MaximumBytes = 20 * 1024 * 1024;
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly CultureInfo Afrikaans = CultureInfo.GetCultureInfo("af-ZA");

    public static FeeStatementDocument Read(byte[] content, string name)
    {
        name = Regex.Split(name, @"[/\\]").Last();
        if (!name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || name.Length > 256 || content.Length is 0 or > MaximumBytes)
            throw new ValidationException("Upload an original administrator XLSX statement, no larger than 20 MB. Calculation/macro workbooks are not input statements.");
        var document = new FeeStatementDocument { ParserVersion = CurrentVersion, FileName = name, Sha256 = Convert.ToHexString(SHA256.HashData(content)) };
        try
        {
            using var archive = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
            if (archive.Entries.Count > 2000 || archive.Entries.Sum(x => x.Length) > 160L * 1024 * 1024 || archive.Entries.Any(x => x.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase)))
                throw new ValidationException("This workbook exceeds safe input limits or contains macros.");
            var strings = archive.GetEntry("xl/sharedStrings.xml") is { } shared
                ? Xml(shared).Descendants(Ns + "si").Select(x => string.Concat(x.Descendants(Ns + "t").Select(t => t.Value))).ToList() : [];
            var workbook = Xml(archive.GetEntry("xl/workbook.xml") ?? throw new ValidationException("Workbook metadata is missing."));
            var relations = Xml(archive.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new ValidationException("Workbook relationships are missing."));
            var date1904 = (string?)workbook.Root?.Element(Ns + "workbookPr")?.Attribute("date1904") is "true" or "1";
            XNamespace rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            foreach (var sheet in workbook.Descendants(Ns + "sheet"))
            {
                var sheetName = (string?)sheet.Attribute("name") ?? "Sheet";
                // Prefer original wide statement sheets; never ingest the old engine's derived tables.
                if (sheetName.StartsWith("Transactions", StringComparison.OrdinalIgnoreCase) || sheetName.StartsWith("Benchmark", StringComparison.OrdinalIgnoreCase) ||
                    sheetName is "Statement_txt" or "ProductPortfolio" or "PDF_SinglePeriod" or "Inflation+3") continue;
                var relation = relations.Root!.Elements().Single(x => (string?)x.Attribute("Id") == (string?)sheet.Attribute(rel + "id"));
                var target = ((string?)relation.Attribute("Target") ?? "").Replace('\\', '/');
                if ((string?)relation.Attribute("TargetMode") == "External" || target.Split('/').Contains("..")) throw new ValidationException("External or unsafe worksheet relationships are not allowed.");
                var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                var xml = Xml(archive.GetEntry(path) ?? throw new ValidationException("Worksheet missing."));
                var policy = ""; var holding = ""; var inTransactions = false;
                var summaryPolicy = ""; FeeStatementPeriod? summary = null;
                var outflows = false;
                Dictionary<string, string>? columns = null;
                foreach (var row in xml.Descendants(Ns + "row"))
                {
                    var cells = row.Elements(Ns + "c").ToDictionary(x => Regex.Match((string?)x.Attribute("r") ?? "", "^[A-Z]+").Value, x => x);
                    var values = cells.ToDictionary(x => x.Key, x => Value(x.Value));
                    var texts = values.Values.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                    var source = $"{name} / {sheetName} row {(int?)row.Attribute("r") ?? 0}";
                    var accountHeading = texts.FirstOrDefault(x => Regex.IsMatch(x, @"(?:Policy No|Polisno)\s*:", RegexOptions.IgnoreCase));
                    if (accountHeading is not null)
                    { summaryPolicy = Regex.Match(accountHeading, @"(?:Policy No|Polisno)\s*:\s*(\S+)", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant(); summary = null; }
                    var openingLabel = texts.FirstOrDefault(x => Regex.IsMatch(x, @"^(?:Opening market value|Beginmarkwaarde)\b", RegexOptions.IgnoreCase));
                    var closingLabel = texts.FirstOrDefault(x => Regex.IsMatch(x, @"^(?:Closing market value|Eindmarkwaarde)\b", RegexOptions.IgnoreCase));
                    if ((openingLabel is not null || summary is not null) && cells.Values.Any(c => c.Element(Ns + "f") is not null || (string?)c.Attribute("t") == "e"))
                        throw new ValidationException($"Account summaries must contain original values, not formulas or Excel errors, at {source}.");
                    if (openingLabel is not null && summaryPolicy != "")
                    {
                        summary = new() { Account = summaryPolicy, From = SummaryDate(openingLabel), OpeningValue = SummaryAmount(), OpeningReference = source };
                        document.Periods.Add(summary); outflows = false;
                    }
                    else if (closingLabel is not null && summary is not null)
                    {
                        summary.To = SummaryDate(closingLabel); summary.ClosingValue = SummaryAmount(); summary.ClosingReference = source; summary = null;
                    }
                    else if (summary is not null)
                    {
                        if (texts.Any(x => x.Trim() is "Outflows" or "Uitvloei")) outflows = true;
                        else if (texts.Any(x => x.Trim() is "Inflows" or "Invloei")) outflows = false;
                        else if (texts.Count >= 2 && texts.Any(x => Regex.IsMatch(x, @"\d")) &&
                            texts[0] is { } label && !Regex.IsMatch(label, @"^(?:Period|Tydperk)\b", RegexOptions.IgnoreCase))
                        {
                            var amount = SummaryAmount();
                            if (outflows && !Regex.IsMatch(label, @"^(?:Market movement|Markbeweging)\b", RegexOptions.IgnoreCase)) amount = -Math.Abs(amount);
                            summary.Entries.Add(new(label, amount, source));
                        }
                    }
                    decimal SummaryAmount()
                    {
                        var last = cells.Values.LastOrDefault(c => !string.IsNullOrWhiteSpace(Value(c)) && Value(c).Trim() != "R");
                        if (last is null || last.Element(Ns + "f") is not null || (string?)last.Attribute("t") == "e")
                            throw new ValidationException($"Summary amounts must be original administrator values at {source}.");
                        return Number(Regex.Replace(Value(last), @"(?i)\bZAR\b|R", "").Trim());
                    }
                    if (texts.Any(x => x.Contains("Detailed Transaction Statement", StringComparison.OrdinalIgnoreCase) || x.Contains("Gedetailleerde Transaksiestaat", StringComparison.OrdinalIgnoreCase)))
                    { inTransactions = true; columns = null; policy = holding = summaryPolicy = ""; summary = null; continue; }
                    if (!inTransactions) continue;
                    var policyText = texts.FirstOrDefault(x => Regex.IsMatch(x, @"(?:Policy No|Polisno)\s*:", RegexOptions.IgnoreCase));
                    if (policyText is not null)
                    { policy = Regex.Match(policyText, @"(?:Policy No|Polisno)\s*:\s*(\S+)", RegexOptions.IgnoreCase).Groups[1].Value.ToUpperInvariant(); holding = ""; continue; }
                    if (values.Any(x => x.Value.Trim().ToLowerInvariant() is "date" or "datum") && texts.Any(x => x.Trim().ToLowerInvariant() is "detail" or "besonderhede"))
                    {
                        if (texts.Count == 3 && texts.Any(x => x.Trim().ToLowerInvariant() is "value" or "waarde"))
                        { columns = null; continue; } // Cash/benefit summary, not a second unit transaction table.
                        columns = new();
                        foreach (var (col, value) in values)
                        {
                            var key = value.Trim().ToLowerInvariant();
                            var role = key switch {
                                "date" or "datum" => "date", "detail" or "besonderhede" => "detail", "unit price" or "eenheids-prys" => "price",
                                "units" or "eenhede" => "units", "unit balance" or "eenheidsbalans" => "balance", _ =>
                                key.StartsWith("market value") || key.StartsWith("markwaarde") ? "amount" : "" };
                            if (role != "") columns[role] = col;
                        }
                        if (new[] { "date", "detail", "price", "units", "balance", "amount" }.Any(x => !columns.ContainsKey(x))) throw new ValidationException("A transaction table has incomplete administrator columns.");
                        continue;
                    }
                    if (columns is null || policy == "") continue;
                    string At(string role) => values.GetValueOrDefault(columns[role], "").Trim();
                    var detail = At("detail"); var dateText = At("date");
                    if (dateText == "" && detail != "") { holding = detail; continue; }
                    if (dateText == "" || detail == "") continue;
                    if (!TryDate(dateText, date1904, out var date))
                    {
                        // End a table at nontransaction sections rather than interpreting page furniture as a holding.
                        if (texts.Any(x => x.Contains("Transaction Summary", StringComparison.OrdinalIgnoreCase) || x.Contains("Transaksieopsomming", StringComparison.OrdinalIgnoreCase))) columns = null;
                        continue;
                    }
                    if (holding == "") throw new ValidationException("A transaction is missing its underlying holding heading.");
                    if (columns.Values.Any(c => cells.TryGetValue(c, out var cell) && (cell.Element(Ns + "f") is not null || (string?)cell.Attribute("t") == "e")))
                        throw new ValidationException("Transaction inputs must be original values, not formulas or Excel errors.");
                    var number = (int?)row.Attribute("r") ?? 0;
                    var parsed = new FeeStatementRow { Account = policy, Holding = holding, Date = date, Detail = detail,
                        Wrap = Regex.Match(holding, @"\(([A-Z][A-Z0-9]{1,7})\)\s*$").Groups[1].Value,
                        Amount = Number(At("amount")), Units = Number(At("units")), UnitBalance = Number(At("balance")), Price = Number(At("price")),
                        Kind = FeeStatementKinds.Classify(detail), Source = $"{name} / {sheetName} row {number}", SourceRow = number };
                    if (parsed.Wrap == "KONTANT") parsed.Wrap = "CASH";
                    var priced = Regex.Match(detail, @"(?:priced|geprys).*?(\d{4}[/\-]\d{2}[/\-]\d{2})", RegexOptions.IgnoreCase);
                    if (priced.Success && TryDate(priced.Groups[1].Value.Replace('/', '-'), false, out var priceDate)) parsed.PriceDate = priceDate;
                    parsed.Key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FeeJson.Write(new { parsed.Account, parsed.Holding, parsed.Date, parsed.Detail, parsed.Amount, parsed.Units, parsed.UnitBalance, parsed.Price }))));
                    document.Rows.Add(parsed);
                    if (document.Rows.Count > 100000) throw new ValidationException("Use statements with no more than 100,000 transaction rows.");
                }
            }
            if (document.Rows.Count == 0) throw new ValidationException("No supported AIMS/Glacier detailed transaction tables were found. Upload the original Excel statement, including its detailed transactions.");
            foreach (var group in document.Rows.GroupBy(x => x.Key).Where(x => x.Count() > 1))
            {
                document.Issues.Add($"Repeated identical source rows at {group.First().Source}; verify these before claiming.");
                var occurrence = 0;
                foreach (var row in group) row.Key += "-" + (++occurrence).ToString(CultureInfo.InvariantCulture);
            }
            return document;

            string Value(XElement cell)
            {
                var value = cell.Element(Ns + "v")?.Value ?? "";
                return (string?)cell.Attribute("t") switch { "s" => strings[int.Parse(value, CultureInfo.InvariantCulture)], "inlineStr" => string.Concat(cell.Descendants(Ns + "t").Select(x => x.Value)), _ => value };
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or FormatException or ArgumentException or OverflowException or IndexOutOfRangeException)
        { throw new ValidationException("The administrator workbook could not be read safely: " + ex.Message); }
    }

    internal static decimal Number(string value)
    {
        var text = value.Replace("\u00a0", "").Replace(" ", "").Trim();
        if (text == "") throw new ValidationException("A required transaction amount, price or unit value is blank.");
        if (text.StartsWith('(') && text.EndsWith(')')) text = "-" + text[1..^1];
        if (text.Contains(',')) text = text.LastIndexOf(',') > text.LastIndexOf('.') ? text.Replace(".", "").Replace(',', '.') : text.Replace(",", "");
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result))
            throw new ValidationException("Unrecognised administrator number: " + value);
        return result;
    }

    private static bool TryDate(string value, bool date1904, out DateOnly date)
    {
        date = default;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 1 and < 150000)
        { date = DateOnly.FromDateTime(DateTime.FromOADate(serial + (date1904 ? 1462 : 0))); return true; }
        if (DateOnly.TryParseExact(value, ["yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        return false;
    }

    private static DateOnly SummaryDate(string label)
    {
        var text = Regex.Match(label, @"\d{4}[/\-]\d{2}[/\-]\d{2}").Value;
        return TryDate(text, false, out var date) ? date : throw new ValidationException("An account summary has no supported dated opening/closing boundary.");
    }

    private static XDocument Xml(ZipArchiveEntry entry)
    {
        if (entry.Length > 100L * 1024 * 1024) throw new ValidationException("A worksheet XML part is too large.");
        using var stream = entry.Open(); using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 100L * 1024 * 1024 });
        return XDocument.Load(reader);
    }
}
