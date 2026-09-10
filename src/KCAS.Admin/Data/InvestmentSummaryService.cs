using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace KCAS.Admin.Data;

public sealed class InvestmentSummaryService(ApplicationDbContext db)
{
    public async Task<InvestmentSummaryModel> LoadAsync(
        InvestmentSummaryQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        query ??= new InvestmentSummaryQuery();
        var staleCutoff = DateOnly.FromDateTime(DateTime.Today.AddDays(-query.StaleAfterDays));

        var clientOptions = await db.Clients
            .AsNoTracking()
            .Where(client => client.InvestmentAccounts.Any() || client.FundValuations.Any())
            .OrderBy(client => client.DisplayName)
            .Select(client => new InvestmentSummaryClientOption(
                client.Id,
                client.KanaanId,
                client.DisplayName,
                client.LifecycleStatus))
            .ToListAsync(cancellationToken);

        var clientsQuery = db.Clients
            .AsNoTracking()
            .Where(client => client.InvestmentAccounts.Any() || client.FundValuations.Any());
        if (query.ClientId.HasValue)
        {
            clientsQuery = clientsQuery.Where(client => client.Id == query.ClientId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(query.KanaanId))
        {
            var kanaanId = query.KanaanId.Trim();
            clientsQuery = clientsQuery.Where(client => client.KanaanId == kanaanId);
        }

        var clients = await clientsQuery
            .Include(client => client.InvestmentAccounts)
                .ThenInclude(account => account.Transactions)
            .Include(client => client.InvestmentReconciliationReviews)
            .Include(client => client.FundValuations)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var allRows = clients
            .SelectMany(client =>
            {
                var rows = InvestmentSummaryCalculator.BuildRows(client.InvestmentAccounts, client.FundValuations);
                ApplyLatestReviews(rows, client.InvestmentReconciliationReviews);
                return rows.Select(row => InvestmentSummaryRow.From(client, row, staleCutoff));
            })
            .ToList();

        var fundOptions = allRows
            .Select(row => row.FundName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .Cast<string>()
            .ToList();
        var administratorOptions = allRows
            .Select(row => row.Administrator)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value)
            .Cast<string>()
            .ToList();

        IEnumerable<InvestmentSummaryRow> filtered = allRows;
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            filtered = filtered.Where(row =>
                Contains(row.ClientDisplayName, search) ||
                Contains(row.KanaanId, search) ||
                Contains(row.FundName, search) ||
                Contains(row.Administrator, search) ||
                Contains(row.ProductName, search) ||
                Contains(row.ProductType, search) ||
                Contains(row.AccountNumber, search));
        }

        if (!string.IsNullOrWhiteSpace(query.LifecycleStatus))
        {
            filtered = filtered.Where(row =>
                string.Equals(row.LifecycleStatus, query.LifecycleStatus, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.FundName))
        {
            filtered = filtered.Where(row =>
                string.Equals(row.FundName, query.FundName, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Administrator))
        {
            filtered = filtered.Where(row =>
                string.Equals(row.Administrator, query.Administrator, StringComparison.OrdinalIgnoreCase));
        }

        var summaryRows = filtered.ToList();
        var displayedRows = query.Scope switch
        {
            InvestmentSummaryScopes.Historical => summaryRows.Where(row => row.IsHistorical),
            InvestmentSummaryScopes.All => summaryRows,
            _ => summaryRows.Where(row => !row.IsHistorical)
        };

        displayedRows = ApplySort(displayedRows, query.SortColumn, query.SortDescending);
        var currentRows = summaryRows.Where(row => !row.IsHistorical).ToList();
        var totalCurrentValueZar = Sum(currentRows.Select(row => row.CurrentValueZar));
        var allocation = currentRows
            .GroupBy(row => string.IsNullOrWhiteSpace(row.FundName) ? "Not captured" : row.FundName.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var amount = Sum(group.Select(row => row.CurrentValueZar));
                var percentage = totalCurrentValueZar.HasValue &&
                                 totalCurrentValueZar.Value != 0 &&
                                 amount.HasValue
                    ? amount.Value / totalCurrentValueZar.Value * 100
                    : (decimal?)null;
                return new InvestmentFundAllocation(
                    group.Key,
                    InvestmentGeographies.Classify(group),
                    amount,
                    percentage,
                    group.Select(row => row.ClientId).Distinct().Count());
            })
            .OrderByDescending(item => item.AmountZar)
            .ThenBy(item => item.FundName)
            .ToList();

        return new InvestmentSummaryModel
        {
            Query = query,
            Rows = displayedRows.ToList(),
            ClientOptions = clientOptions,
            KanaanIdOptions = clientOptions
                .Select(option => option.KanaanId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value)
                .Cast<string>()
                .ToList(),
            FundOptions = fundOptions,
            AdministratorOptions = administratorOptions,
            FundAllocation = allocation,
            TotalCurrentValueZar = totalCurrentValueZar,
            CurrentClientCount = currentRows.Select(row => row.ClientId).Distinct().Count(),
            CurrentHoldingCount = currentRows.Count,
            HistoricalHoldingCount = summaryRows.Count(row => row.IsHistorical),
            StatusCorrectionCount = summaryRows.Count(row => row.NeedsStatusCorrection),
            UnmatchedValuationCount = currentRows.Count(row => row.Source == "Unmatched fund valuation"),
            StaleValuationCount = currentRows.Count(row => row.IsValuationStale),
            LatestValuationDate = currentRows
                .Where(row => row.CurrentValueDate.HasValue)
                .MaxBy(row => row.CurrentValueDate)
                ?.CurrentValueDate,
            SouthAfricanValueZar = Sum(currentRows
                .Where(row => row.Geography == InvestmentGeographies.SouthAfrica)
                .Select(row => row.CurrentValueZar)),
            OffshoreValueZar = Sum(currentRows
                .Where(row => row.Geography == InvestmentGeographies.Offshore)
                .Select(row => row.CurrentValueZar))
        };
    }

    public async Task<byte[]> ExportCsvAsync(
        InvestmentSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        var model = await LoadAsync(query, cancellationToken);
        var csv = new StringBuilder();
        csv.AppendLine(
            "Client,Kanaan ID,Lifecycle,Valuation date,Fund,Geography,Administrator,Product,Product type,Account,Native currency,Native value,ZAR value,Position,Source,Correction");
        foreach (var row in model.Rows)
        {
            csv.AppendLine(string.Join(",",
                Csv(row.ClientDisplayName),
                Csv(row.KanaanId),
                Csv(row.LifecycleStatus),
                Csv(row.CurrentValueDate?.ToString("yyyy-MM-dd")),
                Csv(row.FundName),
                Csv(row.Geography),
                Csv(row.Administrator),
                Csv(row.ProductName),
                Csv(row.ProductType),
                Csv(row.AccountNumber),
                Csv(row.ForeignCurrencyCode),
                Csv(Number(row.CurrentValueForeign)),
                Csv(Number(row.CurrentValueZar)),
                Csv(row.IsHistorical ? "Historical" : "Current"),
                Csv(row.Source),
                Csv(row.NeedsStatusCorrection ? row.StatusReason : null)));
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString());
    }

    public async Task<byte[]> ExportPdfAsync(
        InvestmentSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        var model = await LoadAsync(query, cancellationToken);
        var subject = ReportSubject(model, query);
        var writer = new SimplePdfWriter("KCAS Investment Summary Report", subject);

        writer.WriteReportHeader("KCAS Investment Summary", subject, [
            ("Generated", DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("Scope", ScopeLabel(query.Scope)),
            ("Filters", FilterLabel(query))
        ]);

        writer.WriteMetricGrid([
            ("Total current value", Money(model.TotalCurrentValueZar)),
            ("South African", Money(model.SouthAfricanValueZar)),
            ("Offshore", Money(model.OffshoreValueZar)),
            ("Latest valuation", Date(model.LatestValuationDate)),
            ("Current holdings", model.CurrentHoldingCount.ToString(CultureInfo.InvariantCulture)),
            ("Historical holdings", model.HistoricalHoldingCount.ToString(CultureInfo.InvariantCulture)),
            ("Clients with holdings", model.CurrentClientCount.ToString(CultureInfo.InvariantCulture)),
            ("Stale / missing dates", model.StaleValuationCount.ToString(CultureInfo.InvariantCulture)),
            ("Unmatched valuations", model.UnmatchedValuationCount.ToString(CultureInfo.InvariantCulture)),
            ("Status corrections", model.StatusCorrectionCount.ToString(CultureInfo.InvariantCulture))
        ]);
        writer.WriteNote("A current valuation is stale when its value date is missing or more than 90 days old. Lifecycle and investment position are reported separately.");

        writer.WriteSection("Allocation by underlying fund");
        if (model.FundAllocation.Count == 0)
        {
            writer.WriteMuted("No current fund allocation matches these filters.");
        }
        else
        {
            writer.WriteTable(
                ["Fund", "Geography", "Clients", "Value", "Allocation"],
                [320, 95, 60, 105, 80],
                model.FundAllocation.Select(item => new[]
                {
                    Display(item.FundName),
                    Display(item.Geography),
                    item.ClientCount.ToString(CultureInfo.InvariantCulture),
                    Money(item.AmountZar),
                    Percent(item.Percentage)
                }));
        }

        writer.WriteSection("Investment detail");
        if (model.Rows.Count == 0)
        {
            writer.WriteMuted("No investments match these filters.");
        }
        else
        {
            writer.WriteTable(
                ["Client", "Kanaan ID", "Date", "Fund", "Administrator", "Account", "ZAR value", "Status"],
                [135, 58, 60, 140, 115, 80, 80, 88],
                model.Rows.Select(row => new[]
                {
                    Display(row.ClientDisplayName),
                    Display(row.KanaanId),
                    Date(row.CurrentValueDate),
                    Display(row.FundName),
                    Display(row.Administrator),
                    Display(row.AccountNumber),
                    Money(row.CurrentValueZar),
                    InvestmentStatusLabel(row)
                }));
        }

        return writer.Build();
    }

    private static IOrderedEnumerable<InvestmentSummaryRow> ApplySort(
        IEnumerable<InvestmentSummaryRow> rows,
        string? column,
        bool descending) =>
        (column, descending) switch
        {
            ("kanaanId", true) => rows.OrderByDescending(row => row.KanaanId),
            ("kanaanId", false) => rows.OrderBy(row => row.KanaanId),
            ("lifecycle", true) => rows.OrderByDescending(row => row.LifecycleStatus),
            ("lifecycle", false) => rows.OrderBy(row => row.LifecycleStatus),
            ("date", true) => rows.OrderByDescending(row => row.CurrentValueDate),
            ("date", false) => rows.OrderBy(row => row.CurrentValueDate),
            ("fund", true) => rows.OrderByDescending(row => row.FundName),
            ("fund", false) => rows.OrderBy(row => row.FundName),
            ("administrator", true) => rows.OrderByDescending(row => row.Administrator),
            ("administrator", false) => rows.OrderBy(row => row.Administrator),
            ("account", true) => rows.OrderByDescending(row => row.AccountNumber),
            ("account", false) => rows.OrderBy(row => row.AccountNumber),
            ("value", true) => rows.OrderByDescending(row => row.CurrentValueZar),
            ("value", false) => rows.OrderBy(row => row.CurrentValueZar),
            ("client", true) => rows.OrderByDescending(row => row.ClientDisplayName).ThenBy(row => row.FundName),
            _ => rows.OrderBy(row => row.ClientDisplayName).ThenBy(row => row.FundName)
        };

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static void ApplyLatestReviews(
        IEnumerable<ClientFundSummaryRowModel> rows,
        IEnumerable<ClientInvestmentReconciliationReview> reviews)
    {
        var latestReviews = reviews
            .GroupBy(review => review.ClientInvestmentAccountId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(review => review.ReviewedAtUtc)
                    .ThenByDescending(review => review.Id)
                    .First());

        foreach (var row in rows)
        {
            if (!row.AccountId.HasValue ||
                !latestReviews.TryGetValue(row.AccountId.Value, out var review))
            {
                continue;
            }

            var outcomeLabel = ClientInvestmentReconciliationOutcomes.Label(review.Outcome);
            row.Source = $"Reconciliation review: {outcomeLabel}";
            row.StatusReason = string.IsNullOrWhiteSpace(review.Reason)
                ? outcomeLabel
                : $"{outcomeLabel}: {review.Reason}";
            row.NeedsStatusCorrection = review.Outcome == ClientInvestmentReconciliationOutcomes.NeedsFollowUp;
        }
    }

    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        var captured = values.Where(value => value.HasValue).Select(value => value!.Value).ToList();
        return captured.Count == 0 ? null : captured.Sum();
    }

    private static string? Number(decimal? value) =>
        value?.ToString("0.00####", CultureInfo.InvariantCulture);

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Not captured" : value;

    private static string Money(decimal? value) =>
        value.HasValue ? $"R {value.Value:N2}" : "Not captured";

    private static string Percent(decimal? value) =>
        value.HasValue ? $"{value.Value:N2}%" : "-";

    private static string Date(DateOnly? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "Not captured";

    private static string ScopeLabel(string? scope) => scope switch
    {
        InvestmentSummaryScopes.Historical => "Historical only",
        InvestmentSummaryScopes.All => "Current and historical",
        _ => "Current only"
    };

    private static string FilterLabel(InvestmentSummaryQuery query)
    {
        var filters = new List<string>();
        if (query.ClientId.HasValue)
        {
            filters.Add($"Client ID {query.ClientId.Value}");
        }

        if (!string.IsNullOrWhiteSpace(query.KanaanId))
        {
            filters.Add($"Kanaan ID {query.KanaanId}");
        }

        if (!string.IsNullOrWhiteSpace(query.LifecycleStatus))
        {
            filters.Add($"Lifecycle {query.LifecycleStatus}");
        }

        if (!string.IsNullOrWhiteSpace(query.FundName))
        {
            filters.Add($"Fund {query.FundName}");
        }

        if (!string.IsNullOrWhiteSpace(query.Administrator))
        {
            filters.Add($"Administrator {query.Administrator}");
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            filters.Add($"Search {query.Search}");
        }

        return filters.Count == 0 ? "All investments" : string.Join("; ", filters);
    }

    private static string InvestmentStatusLabel(InvestmentSummaryRow row)
    {
        var status = row.IsHistorical ? "Historical" : "Current";
        if (row.IsValuationStale)
        {
            status += "; stale";
        }

        if (row.NeedsStatusCorrection)
        {
            status += "; correction required";
        }

        return status;
    }

    private static string ReportSubject(InvestmentSummaryModel model, InvestmentSummaryQuery query)
    {
        if (query.ClientId.HasValue)
        {
            var row = model.Rows.FirstOrDefault();
            var option = model.ClientOptions.FirstOrDefault(option => option.Id == query.ClientId.Value);
            var name = row?.ClientDisplayName ?? option?.DisplayName ?? $"Client ID {query.ClientId.Value}";
            var kanaanId = row?.KanaanId ?? option?.KanaanId;
            return string.IsNullOrWhiteSpace(kanaanId)
                ? name
                : $"{name} | Kanaan ID {kanaanId}";
        }

        if (!string.IsNullOrWhiteSpace(query.KanaanId))
        {
            var clientCount = model.Rows.Select(row => row.ClientId).Distinct().Count();
            return $"Kanaan ID {query.KanaanId} | {clientCount} client{(clientCount == 1 ? "" : "s")}";
        }

        return "All clients";
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private sealed class SimplePdfWriter
    {
        private const double PageWidth = 841.89;
        private const double PageHeight = 595.28;
        private const double Margin = 36;
        private const double BottomMargin = 42;
        private const double LineHeight = 12;
        private const double ContentWidth = PageWidth - Margin * 2;
        private readonly List<string> pages = [];
        private readonly string title;
        private readonly string subject;
        private StringBuilder content = new();
        private double y = PageHeight - Margin;

        public SimplePdfWriter(string title, string subject)
        {
            this.title = title;
            this.subject = subject;
            BeginPage();
        }

        public void WriteReportHeader(
            string value,
            string subtitle,
            IEnumerable<(string Label, string Value)> metadata)
        {
            Fill(0.94, 0.96, 0.98);
            Rect(Margin, y - 72, ContentWidth, 82, fill: true, stroke: false);
            Stroke(0.72, 0.78, 0.84);
            Rect(Margin, y - 72, ContentWidth, 82, fill: false, stroke: true);
            Fill(0.12, 0.18, 0.24);
            Text(value, 20, Margin + 16, y - 10);
            Fill(0.28, 0.33, 0.38);
            Text(subtitle, 10, Margin + 16, y - 30);

            var metaY = y - 10;
            foreach (var (label, itemValue) in metadata)
            {
                Fill(0.34, 0.39, 0.44);
                Text($"{label}: {itemValue}", 8, Margin + 500, metaY, maxWidth: ContentWidth - 520);
                metaY -= 13;
            }

            Fill(0, 0, 0);
            y -= 96;
        }

        public void WriteSection(string value)
        {
            EnsureSpace(30);
            Fill(0.15, 0.21, 0.27);
            Text(value, 13, Margin, y);
            Stroke(0.72, 0.78, 0.84);
            DrawLine(Margin, y - 5, Margin + ContentWidth, y - 5);
            Fill(0, 0, 0);
            y -= 20;
        }

        public void WriteMuted(string value)
        {
            EnsureSpace(LineHeight);
            Fill(0.38, 0.42, 0.46);
            Text(value, 8, Margin, y);
            Fill(0, 0, 0);
            y -= LineHeight;
        }

        public void WriteNote(string value)
        {
            EnsureSpace(26);
            Fill(0.97, 0.98, 0.99);
            Rect(Margin, y - 18, ContentWidth, 26, fill: true, stroke: false);
            Fill(0.33, 0.37, 0.42);
            Text(value, 7.5, Margin + 10, y - 7, maxWidth: ContentWidth - 20);
            Fill(0, 0, 0);
            y -= 36;
        }

        public void WriteMetricGrid(IReadOnlyList<(string Label, string Value)> values)
        {
            const int columns = 5;
            const double gap = 8;
            const double cardHeight = 44;
            var cardWidth = (ContentWidth - gap * (columns - 1)) / columns;
            for (var i = 0; i < values.Count; i += columns)
            {
                EnsureSpace(cardHeight + 8);
                for (var column = 0; column < columns && i + column < values.Count; column++)
                {
                    var (label, value) = values[i + column];
                    var x = Margin + column * (cardWidth + gap);
                    Fill(0.98, 0.99, 1);
                    Rect(x, y - cardHeight + 8, cardWidth, cardHeight, fill: true, stroke: false);
                    Stroke(0.78, 0.82, 0.86);
                    Rect(x, y - cardHeight + 8, cardWidth, cardHeight, fill: false, stroke: true);
                    Fill(0.44, 0.49, 0.54);
                    Text(label, 6.8, x + 8, y - 7, maxWidth: cardWidth - 16);
                    Fill(0.12, 0.18, 0.24);
                    Text(value, 10.5, x + 8, y - 25, maxWidth: cardWidth - 16);
                    Fill(0, 0, 0);
                }

                y -= cardHeight + 10;
            }
        }

        public void WriteTable(string[] headers, double[] widths, IEnumerable<string[]> rows)
        {
            var rowIndex = 0;
            WriteTableHeader(headers, widths);
            foreach (var row in rows.ToList())
            {
                if (EnsureSpace(TableRowHeight(row, widths) + 3))
                {
                    WriteTableHeader(headers, widths);
                }

                WriteTableRow(row, widths, rowIndex);
                rowIndex++;
            }

            y -= 10;
        }

        public byte[] Build()
        {
            FinishPage();

            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>"
            };
            var pageKids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(index => $"{4 + index * 2} 0 R"));
            objects.Add($"<< /Type /Pages /Kids [{pageKids}] /Count {pages.Count} >>");
            objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

            for (var i = 0; i < pages.Count; i++)
            {
                var pageObjectNumber = 4 + i * 2;
                var contentObjectNumber = pageObjectNumber + 1;
                objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Invariant(PageWidth)} {Invariant(PageHeight)}] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObjectNumber} 0 R >>");
                var stream = pages[i] + Footer(i + 1, pages.Count);
                objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream");
            }

            var pdf = new StringBuilder();
            var offsets = new List<int> { 0 };
            pdf.Append("%PDF-1.4\n");
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
                pdf.Append(i + 1).Append(" 0 obj\n");
                pdf.Append(objects[i]).Append("\n");
                pdf.Append("endobj\n");
            }

            var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
            pdf.Append("xref\n");
            pdf.Append("0 ").Append(objects.Count + 1).Append("\n");
            pdf.Append("0000000000 65535 f \n");
            foreach (var offset in offsets.Skip(1))
            {
                pdf.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            }

            pdf.Append("trailer\n");
            pdf.Append($"<< /Size {objects.Count + 1} /Root 1 0 R /Info << /Title ({Escape(title)}) >> >>\n");
            pdf.Append("startxref\n");
            pdf.Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n");
            pdf.Append("%%EOF\n");

            return Encoding.ASCII.GetBytes(pdf.ToString());
        }

        private void BeginPage()
        {
            content = new StringBuilder();
            y = PageHeight - Margin;
        }

        private void FinishPage()
        {
            if (content.Length > 0)
            {
                pages.Add(content.ToString());
            }
        }

        private void NewPage()
        {
            FinishPage();
            BeginPage();
        }

        private bool EnsureSpace(double height)
        {
            if (y - height < BottomMargin)
            {
                NewPage();
                return true;
            }

            return false;
        }

        private void WriteTableHeader(string[] headers, double[] widths)
        {
            EnsureSpace(19);
            Fill(0.88, 0.91, 0.94);
            Rect(Margin, y - 14, widths.Sum(), 18, fill: true, stroke: false);
            Stroke(0.62, 0.67, 0.72);
            DrawLine(Margin, y - 14, Margin + widths.Sum(), y - 14);
            var x = Margin;
            for (var i = 0; i < widths.Length; i++)
            {
                Fill(0.16, 0.21, 0.27);
                Text(headers[i], 7.2, x + 4, y - 8, maxWidth: widths[i] - 8);
                x += widths[i];
            }

            Fill(0, 0, 0);
            y -= 20;
        }

        private void WriteTableRow(string[] cells, double[] widths, int rowIndex)
        {
            var rowHeight = TableRowHeight(cells, widths);
            if (rowIndex % 2 == 1)
            {
                Fill(0.985, 0.99, 0.995);
                Rect(Margin, y - rowHeight + 4, widths.Sum(), rowHeight, fill: true, stroke: false);
            }

            var x = Margin;
            for (var i = 0; i < widths.Length; i++)
            {
                var value = i < cells.Length ? cells[i] : "";
                var lines = Wrap(value, widths[i], maxLines: 2);
                Fill(0.08, 0.1, 0.12);
                for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
                {
                    Text(lines[lineIndex], 6.8, x + 4, y - lineIndex * 9, maxWidth: widths[i] - 8);
                }

                x += widths[i];
            }

            Stroke(0.88, 0.9, 0.92);
            DrawLine(Margin, y - rowHeight + 8, Margin + widths.Sum(), y - rowHeight + 8);
            Fill(0, 0, 0);
            y -= rowHeight;
        }

        private static double TableRowHeight(string[] cells, double[] widths)
        {
            var lineCount = 1;
            for (var i = 0; i < widths.Length && i < cells.Length; i++)
            {
                lineCount = Math.Max(lineCount, Wrap(cells[i], widths[i], maxLines: 2).Count);
            }

            return 14 + (lineCount - 1) * 9;
        }

        private void Text(string value, double size, double x, double textY, double? maxWidth = null)
        {
            if (maxWidth.HasValue)
            {
                value = Fit(value, maxWidth.Value, size);
            }

            content.Append("BT /F1 ")
                .Append(Invariant(size))
                .Append(" Tf ")
                .Append(Invariant(x))
                .Append(' ')
                .Append(Invariant(textY))
                .Append(" Td (")
                .Append(Escape(value))
                .Append(") Tj ET\n");
        }

        private void Rect(double x, double rectY, double width, double height, bool fill, bool stroke)
        {
            content.Append(Invariant(x))
                .Append(' ')
                .Append(Invariant(rectY))
                .Append(' ')
                .Append(Invariant(width))
                .Append(' ')
                .Append(Invariant(height))
                .Append(fill && stroke ? " re B\n" : fill ? " re f\n" : " re S\n");
        }

        private void Fill(double red, double green, double blue)
        {
            content.Append(Invariant(red))
                .Append(' ')
                .Append(Invariant(green))
                .Append(' ')
                .Append(Invariant(blue))
                .Append(" rg\n");
        }

        private void Stroke(double red, double green, double blue)
        {
            content.Append(Invariant(red))
                .Append(' ')
                .Append(Invariant(green))
                .Append(' ')
                .Append(Invariant(blue))
                .Append(" RG\n");
        }

        private void DrawLine(double x1, double y1, double x2, double y2)
        {
            content.Append(Invariant(x1))
                .Append(' ')
                .Append(Invariant(y1))
                .Append(" m ")
                .Append(Invariant(x2))
                .Append(' ')
                .Append(Invariant(y2))
                .Append(" l S\n");
        }

        private string Footer(int pageNumber, int pageCount)
        {
            var footer = new StringBuilder();
            footer.Append("0.48 0.52 0.56 rg\n");
            footer.Append("BT /F1 7 Tf ")
                .Append(Invariant(Margin))
                .Append(' ')
                .Append(Invariant(22))
                .Append(" Td (")
                .Append(Escape(subject))
                .Append(") Tj ET\n");
            footer.Append("BT /F1 7 Tf ")
                .Append(Invariant(PageWidth - Margin - 72))
                .Append(' ')
                .Append(Invariant(22))
                .Append(" Td (Page ")
                .Append(pageNumber.ToString(CultureInfo.InvariantCulture))
                .Append(" of ")
                .Append(pageCount.ToString(CultureInfo.InvariantCulture))
                .Append(") Tj ET\n");
            return footer.ToString();
        }

        private static List<string> Wrap(string? value, double width, int maxLines)
        {
            value = string.IsNullOrWhiteSpace(value) ? "Not captured" : value.Trim();
            var maxChars = Math.Max(8, (int)Math.Floor(width / 4.2));
            if (value.Length <= maxChars)
            {
                return [value];
            }

            var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>();
            var current = "";
            foreach (var word in words)
            {
                var candidate = string.IsNullOrWhiteSpace(current) ? word : $"{current} {word}";
                if (candidate.Length <= maxChars)
                {
                    current = candidate;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(current))
                {
                    lines.Add(current);
                }

                current = word;
                if (lines.Count == maxLines - 1)
                {
                    break;
                }
            }

            if (lines.Count < maxLines && !string.IsNullOrWhiteSpace(current))
            {
                lines.Add(current);
            }

            if (lines.Count == 0)
            {
                lines.Add(value[..Math.Min(value.Length, maxChars)]);
            }

            if (lines.Count == maxLines && string.Join(" ", lines).Length < value.Length)
            {
                lines[^1] = Fit(lines[^1], width, 6.8);
            }

            return lines;
        }

        private static string Fit(string? value, double width, double fontSize)
        {
            value = string.IsNullOrWhiteSpace(value) ? "Not captured" : value.Trim();
            var max = Math.Max(6, (int)Math.Floor(width / (fontSize * 0.55)));
            if (value.Length <= max)
            {
                return value;
            }

            return value[..Math.Max(0, max - 3)] + "...";
        }

        private static string Escape(string value) =>
            value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("(", "\\(", StringComparison.Ordinal)
                .Replace(")", "\\)", StringComparison.Ordinal)
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);

        private static string Invariant(double value) =>
            value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

public sealed record InvestmentSummaryQuery(
    int? ClientId = null,
    string? KanaanId = null,
    string? Search = null,
    string? LifecycleStatus = null,
    string? FundName = null,
    string? Administrator = null,
    string Scope = InvestmentSummaryScopes.Current,
    string SortColumn = "client",
    bool SortDescending = false,
    int StaleAfterDays = 90);

public static class InvestmentSummaryScopes
{
    public const string Current = "Current";
    public const string Historical = "Historical";
    public const string All = "All";
}

public static class InvestmentGeographies
{
    public const string SouthAfrica = "South Africa";
    public const string Offshore = "Offshore";

    public static string Classify(IEnumerable<InvestmentSummaryRow> rows) =>
        rows.Any(row => row.Geography == Offshore) ? Offshore : SouthAfrica;

    public static string Classify(string? fundName, string? currencyCode)
    {
        if (!string.IsNullOrWhiteSpace(currencyCode) ||
            fundName?.Contains("Offshore", StringComparison.OrdinalIgnoreCase) == true ||
            fundName?.Contains("Moriah", StringComparison.OrdinalIgnoreCase) == true ||
            fundName?.Contains("Cash account", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Offshore;
        }

        return SouthAfrica;
    }
}

public sealed class InvestmentSummaryModel
{
    public InvestmentSummaryQuery Query { get; init; } = new();
    public List<InvestmentSummaryRow> Rows { get; init; } = [];
    public List<InvestmentSummaryClientOption> ClientOptions { get; init; } = [];
    public List<string> KanaanIdOptions { get; init; } = [];
    public List<string> FundOptions { get; init; } = [];
    public List<string> AdministratorOptions { get; init; } = [];
    public List<InvestmentFundAllocation> FundAllocation { get; init; } = [];
    public decimal? TotalCurrentValueZar { get; init; }
    public decimal? SouthAfricanValueZar { get; init; }
    public decimal? OffshoreValueZar { get; init; }
    public int CurrentClientCount { get; init; }
    public int CurrentHoldingCount { get; init; }
    public int HistoricalHoldingCount { get; init; }
    public int StatusCorrectionCount { get; init; }
    public int UnmatchedValuationCount { get; init; }
    public int StaleValuationCount { get; init; }
    public DateOnly? LatestValuationDate { get; init; }
}

public sealed record InvestmentSummaryClientOption(
    int Id,
    string? KanaanId,
    string DisplayName,
    string LifecycleStatus);

public sealed record InvestmentFundAllocation(
    string FundName,
    string Geography,
    decimal? AmountZar,
    decimal? Percentage,
    int ClientCount);

public sealed class InvestmentSummaryRow
{
    public int ClientId { get; init; }
    public string? KanaanId { get; init; }
    public string ClientDisplayName { get; init; } = "";
    public string LifecycleStatus { get; init; } = ClientLifecycleStatuses.Unreviewed;
    public int? AccountId { get; init; }
    public string? AccountNumber { get; init; }
    public string? Administrator { get; init; }
    public string? ProductName { get; init; }
    public string? ProductType { get; init; }
    public string? FundName { get; init; }
    public string Geography { get; init; } = InvestmentGeographies.SouthAfrica;
    public decimal? CurrentValueZar { get; init; }
    public decimal? CurrentValueForeign { get; init; }
    public DateOnly? CurrentValueDate { get; init; }
    public string Source { get; init; } = "";
    public bool IsHistorical { get; init; }
    public bool NeedsStatusCorrection { get; init; }
    public string StatusReason { get; init; } = "";
    public string? ForeignCurrencyCode { get; init; }
    public bool IsValuationStale { get; init; }

    public static InvestmentSummaryRow From(
        Client client,
        ClientFundSummaryRowModel row,
        DateOnly staleCutoff) =>
        new()
        {
            ClientId = client.Id,
            KanaanId = client.KanaanId,
            ClientDisplayName = client.DisplayName,
            LifecycleStatus = client.LifecycleStatus,
            AccountId = row.AccountId,
            AccountNumber = row.AccountNumber,
            Administrator = row.Administrator,
            ProductName = row.ProductName,
            ProductType = row.ProductType,
            FundName = row.FundName,
            Geography = InvestmentGeographies.Classify(row.FundName, row.ForeignCurrencyCode),
            CurrentValueZar = row.CurrentValueZar,
            CurrentValueForeign = row.CurrentValueForeign,
            CurrentValueDate = row.CurrentValueDate,
            Source = row.Source,
            IsHistorical = row.IsHistorical,
            NeedsStatusCorrection = row.NeedsStatusCorrection,
            StatusReason = row.StatusReason,
            ForeignCurrencyCode = row.ForeignCurrencyCode,
            IsValuationStale = !row.IsHistorical &&
                               (!row.CurrentValueDate.HasValue || row.CurrentValueDate.Value < staleCutoff)
        };
}
