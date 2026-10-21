using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace KCAS.Admin.Data;

public sealed class PerformanceFeeExports(PerformanceFeeService service)
{
    public async Task<byte[]> ClientPreviewAsync(int profileId, int[] claimIds, ClaimsPrincipal principal)
        => await ClientReportAsync(profileId, claimIds, principal, true);

    public async Task<byte[]> ClientReportAsync(int profileId, int[] claimIds, ClaimsPrincipal principal, bool draft = false)
    {
        var (workspace, selected) = await ClientSelection(profileId, claimIds, principal, draft);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            var summary = new InvestmentSummaryService.SimplePdfWriter("Client PDF review", draft ? "Internal draft" : "KI-approved review", draft ? "DRAFT - NOT FOR SUBMISSION" : null);
            summary.WriteReportHeader("Client Performance Fee Review", workspace.Client.DisplayName, [("Kanaan ID", workspace.Client.KanaanId ?? "-"), ("Accounts", selected.Count.ToString()), ("Currency", "ZAR")]);
            summary.WriteParagraph("Each account and wrap is reviewed separately. A verified 0% mandate, no chargeable outperformance and missing evidence are different outcomes. Only approved positive claim amounts authorise a fee request; a zero closing balance does not itself establish a 0% mandate.");
            foreach (var item in selected.OrderBy(x => x.Profile.Account.AccountNumber))
            {
                var result = FeeJson.Read<FeeCalculationResult>(item.Claim.ResultJson);
                var input = FeeJson.Read<FeePreparation>(item.Claim.InputJson);
                summary.WriteSection(item.Profile.Account.AccountNumber ?? "Account");
                summary.WriteParagraph($"{item.Claim.Status}; {FeeResultDescription.Describe(result)}. " + (result.IsReady ? $"PDF including VAT: {Zar(result.Total)}." : result.HasCalculation ? $"Calculated draft PDF excluding VAT: {Zar(result.FeeExVat)}; not authorised for submission." : "Calculation inputs remain incomplete; no claim amount asserted."));
                if (result.Wraps.Count > 0)
                    summary.WriteTable(["Wrap", "Rate", "Closing ZAR", "Outcome", "PDF ex VAT", "PDF incl. VAT"], [60, 55, 115, 309, 115, 115], result.Wraps.Select(x =>
                    { var basis = input.Wraps.Single(w => w.Code == x.Code).Input; return new[] { x.Code, Percent(basis.FeePercent), basis.ClosingValue is { } value ? Number(value) : "-", FeeResultDescription.Describe(x.Result), x.Result.CanShowCalculation ? Number(x.Result.FeeExVat) : "Pending", x.Result.IsReady || x.Result.HasVatCalculation ? Number(x.Result.Total) : "Pending" }; }));
                foreach (var scope in input.OtherScopes) summary.WriteParagraph($"{scope.Code}; closing {Zar(scope.ClosingValue)}. {scope.Reason}");
                if (draft) foreach (var issue in result.Issues) summary.WriteParagraph(issue);
                await Put(zip, item.Claim.Reference + (draft ? "-DRAFT.pdf" : ".pdf"), CalculationPdf(item.Profile, item.Claim, draft));
            }
            foreach (var other in workspace.Accounts.Where(x => x.Profile == null && x.Account != null))
            { summary.WriteSection(other.Number); summary.WriteParagraph(other.Exclusion ?? "Not yet prepared; no fee assumed."); }
            await Put(zip, draft ? "Client review - DRAFT.pdf" : "Client fee review.pdf", summary.Build());
        }
        return output.ToArray();
    }

    public async Task<byte[]> ClientSummaryAsync(int profileId, int[] claimIds, ClaimsPrincipal principal, bool draft = true)
    {
        var (workspace, selected) = await ClientSelection(profileId, claimIds, principal, draft);
        return ClientSummaryPdf(workspace, selected, draft);
    }

    private async Task<(FeeClientWorkspace Workspace, List<(PerformanceFeeProfile Profile, PerformanceFeeClaim Claim)> Selected)>
        ClientSelection(int profileId, int[] claimIds, ClaimsPrincipal principal, bool draft)
    {
        if (!draft) await service.RequireSubmissionAsync(principal);
        if (claimIds.Length is 0 or > 200 || claimIds.Distinct().Count() != claimIds.Length) throw new ValidationException("Select valid client calculations.");
        var workspace = await service.ClientWorkspaceAsync(profileId, principal);
        var selected = workspace.Accounts.Where(x => x.Profile != null).SelectMany(x => x.Profile!.Claims.Where(c => claimIds.Contains(c.Id)).Select(c => (Profile: x.Profile!, Claim: c))).ToList();
        if (selected.Count != claimIds.Length) throw new UnauthorizedAccessException("The selected calculations do not all belong to this client's available statement accounts.");
        if (draft && selected.Any(x => x.Claim.Status == FeeClaimStages.Draft && workspace.Accounts.Single(a => a.Profile?.Id == x.Profile.Id).RequiresStatementRebuild))
            throw new ValidationException("Prepare the client accounts again: statement summaries or history changed since these drafts were saved.");
        if (!draft && selected.Any(x => x.Claim.ApprovedAtUtc == null || !(x.Claim.Status == FeeClaimStages.NoFee || FeeClaimStages.Exportable(x.Claim.Status)) || !FeeJson.Read<FeeCalculationResult>(x.Claim.ResultJson).IsReady))
            throw new ValidationException("The final client report contains only supported KI-approved reviews, including zero-fee reviews.");
        return (workspace, selected);
    }

    internal static byte[] ClientSummaryPdf(FeeClientWorkspace workspace,
        List<(PerformanceFeeProfile Profile, PerformanceFeeClaim Claim)> selected, bool draft)
    {
        var rows = selected.OrderBy(x => x.Profile.Account.AccountNumber).ThenBy(x => x.Claim.From).Select(x =>
            (x.Profile, x.Claim, Line: FeeReviewSummaryLine.Create(workspace.Accounts.Single(a => a.Profile?.Id == x.Profile.Id), x.Claim))).ToList();
        var totals = rows.Where(x => draft ? x.Line.ReadyForApproval : x.Line.Approved).ToList();
        var writer = new InvestmentSummaryService.SimplePdfWriter("Performance fee summary", draft ? "Internal draft" : "KI-approved claims", draft ? "DRAFT - NOT FOR SUBMISSION" : null);
        writer.WriteReportHeader("Performance-Driven Fee Summary", workspace.Client.DisplayName,
            [("Kanaan ID", workspace.Client.KanaanId ?? "-"), ("Accounts", rows.Count.ToString()), ("Currency", "ZAR"), ("Status", draft ? "Draft review" : "KI approved")]);
        writer.WriteSection(draft ? $"Ready for KI approval: {totals.Count} account(s)" : $"Approved: {totals.Count} account(s)");
        writer.WriteMetricGrid([("PDF excluding VAT", Zar(totals.Sum(x => x.Line.FeeExVat ?? 0))), ("VAT", Zar(totals.Sum(x => x.Line.Vat ?? 0))), ("Total including VAT", Zar(totals.Sum(x => x.Line.Total ?? 0)))]);
        writer.WriteTable(["Account", "Calculation period", "Status", "PDF ex VAT", "VAT", "Total ZAR"], [115, 150, 189, 110, 95, 110],
            rows.Select(x => new[] { x.Line.Account, $"{x.Line.From:yyyy-MM-dd} to {x.Line.To:yyyy-MM-dd}", x.Line.Status,
                Amount(x.Line.FeeExVat), Amount(x.Line.Vat), Amount(x.Line.Total) }));
        if (rows.Count != totals.Count)
            writer.WriteNote($"{rows.Count - totals.Count} account(s) are excluded from the {(draft ? "approval-ready" : "approved")} totals above. Their status is shown separately; an unavailable amount is not a zero-fee conclusion.");
        var fees = totals.SelectMany(x => FeeJson.Read<FeeCalculationResult>(x.Claim.ResultJson).Wraps
            .Where(w => w.Result.CanShowCalculation && w.Result.FeeExVat > 0).Select(w => (x.Line, w.Code, w.Result))).ToList();
        if (fees.Count > 0)
        {
            writer.WriteSection("Fee-bearing wrap schedule");
            writer.WriteTable(["Account", "Wrap", "PDF ex VAT", "VAT", "Total ZAR"], [190, 100, 160, 150, 169],
                fees.Select(x => new[] { x.Line.Account, x.Code, Number(x.Result.FeeExVat),
                    x.Result.IsReady || x.Result.HasVatCalculation ? Number(x.Result.Vat) : "Pending",
                    x.Result.IsReady || x.Result.HasVatCalculation ? Number(x.Result.Total) : "Pending" }));
        }
        if (!draft)
        {
            writer.WriteSection("Claim authority");
            foreach (var row in rows)
                writer.WriteParagraph($"{row.Line.Account}: {row.Claim.Reference}; approved by {row.Claim.ApprovedBy}, {row.Claim.ApprovedAtUtc:yyyy-MM-dd HH:mm} UTC.");
            writer.WriteNote("Use each unique claim reference once. Downloading does not record submission, deduction or payment. Only approved positive amounts may be claimed.");
        }
        else writer.WriteNote("Draft only; no fee deduction is authorised. Totals above cover approval-ready drafts only. Calculated amounts on rows requiring review are not approved claims.");
        return writer.Build();
    }

    private static string Amount(decimal? value) => value is { } amount ? Number(amount) : "Pending";

    public async Task<byte[]> PreviewAsync(int profileId, int claimId, ClaimsPrincipal principal)
    {
        var profile = await service.LoadAsync(profileId, principal);
        var claim = profile.Claims.Single(x => x.Id == claimId);
        if (claim.Status == FeeClaimStages.Draft)
        {
            var (_, ledger) = await service.StatementsAsync(profileId, principal);
            if (ledger.Rows.Count > 0 && FeeJson.Read<FeePreparation>(claim.InputJson).StatementHistory?.SourceFingerprint != ledger.History.SourceFingerprint)
                throw new ValidationException("Prepare the client accounts again before exporting the previous statement draft.");
        }
        return CalculationPdf(profile, claim, draft: true);
    }

    public async Task<byte[]> PackageAsync(int[] claimIds, ClaimsPrincipal principal)
    {
        await service.RequireSubmissionAsync(principal);
        claimIds = claimIds.Distinct().ToArray();
        if (claimIds.Length is 0 or > 200) throw new ValidationException("Select between 1 and 200 approved claims.");
        var all = await service.ListAsync(principal);
        var selected = all.SelectMany(p => p.Claims.Where(c => claimIds.Contains(c.Id)).Select(c => (Profile: p, Claim: c))).ToList();
        if (selected.Count != claimIds.Length) throw new UnauthorizedAccessException("One or more selected claims are not available to this account.");
        if (selected.Any(x => x.Claim.ApprovedAtUtc is null || x.Claim.Status is not (FeeClaimStages.Approved or FeeClaimStages.Submitted or FeeClaimStages.Accepted or FeeClaimStages.Deducted or FeeClaimStages.Paid)))
            throw new ValidationException("Only KI-approved, non-voided, positive claims can be included in an administrator submission.");
        var administrators = selected.Select(x => FeeJson.Read<FeePreparation>(x.Claim.InputJson).Administrator.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (administrators.Length != 1) throw new ValidationException("Export one administrator per submission.");
        var csv = new StringBuilder("Claim reference,Client,Kanaan ID,Account,Wrap,From,To,Currency,Fee excluding VAT,VAT,Total,Approved by,Approved UTC\r\n");
        var manifest = new List<object>();
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (profile, claim) in selected.OrderBy(x => x.Claim.Reference))
            {
                var input = FeeJson.Read<FeePreparation>(claim.InputJson); var result = FeeJson.Read<FeeCalculationResult>(claim.ResultJson);
                if (!result.IsReady || result.Total <= 0) throw new ValidationException("The selected claim has no supported positive fee.");
                var scopes = result.Wraps.Count > 0 ? result.Wraps.Select(x => (Name: x.Code + " " + x.Name, Result: x.Result)) : [(Name: input.WrapFund, Result: result)];
                foreach (var calculation in scopes)
                    csv.AppendLine(string.Join(',', new[] { claim.Reference, profile.Account.Client.DisplayName, profile.Account.Client.KanaanId ?? "", profile.Account.AccountNumber ?? "", calculation.Name,
                        claim.From.ToString("yyyy-MM-dd"), claim.To.ToString("yyyy-MM-dd"), "ZAR", Number(calculation.Result.FeeExVat), Number(calculation.Result.Vat), Number(calculation.Result.Total), claim.ApprovedBy ?? "", claim.ApprovedAtUtc?.ToString("O") ?? "" }.Select(Csv)));
                var fileName = claim.Reference + ".pdf";
                var bytes = CalculationPdf(profile, claim, draft: false);
                await Put(zip, fileName, bytes);
                manifest.Add(new { claim.Reference, File = fileName, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), claim.ApprovedBy, claim.ApprovedAtUtc, Currency = "ZAR", result.Total });
            }
            var cover = new InvestmentSummaryService.SimplePdfWriter("Kanaan performance fee submission", administrators[0]);
            cover.WriteReportHeader("Performance-Driven Fee Submission", administrators[0], [("Prepared", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'")), ("Claims", selected.Count.ToString()), ("Currency", "ZAR")]);
            cover.WriteParagraph("Please process the attached individually approved performance-driven fee claims against the specified client wrap accounts. The schedule and individual calculations identify each claim and its calculation period. Please confirm acceptance, the actual deduction date and amount, and remittance details against each unique claim reference. This request does not authorise any fee exceeding the applicable client mandate.");
            cover.WriteMetricGrid([("Fee excl. VAT", Zar(selected.Sum(x => FeeJson.Read<FeeCalculationResult>(x.Claim.ResultJson).FeeExVat))), ("VAT", Zar(selected.Sum(x => FeeJson.Read<FeeCalculationResult>(x.Claim.ResultJson).Vat))), ("Total claimed", Zar(selected.Sum(x => FeeJson.Read<FeeCalculationResult>(x.Claim.ResultJson).Total)))]);
            cover.WriteTable(["Reference", "Client", "Account", "Period end", "Total ZAR"], [205, 245, 130, 90, 99], selected.Select(x => new[] { x.Claim.Reference, x.Profile.Account.Client.DisplayName, x.Profile.Account.AccountNumber ?? "", x.Claim.To.ToString("yyyy-MM-dd"), Number(FeeJson.Read<FeeCalculationResult>(x.Claim.ResultJson).Total) }));
            cover.WriteNote("Reissuing this package does not create a second claim. Use the unique claim references to prevent duplicate deductions. Payment and deduction must be separately confirmed in KCAS.");
            await Put(zip, "Administrator request.pdf", cover.Build());
            await Put(zip, "Claim schedule.csv", Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray());
            await Put(zip, "manifest.json", Encoding.UTF8.GetBytes(FeeJson.Write(new { Administrator = administrators[0], ExportedAtUtc = DateTime.UtcNow, Claims = manifest })));
        }
        return output.ToArray();
    }

    internal static byte[] CalculationPdf(PerformanceFeeProfile profile, PerformanceFeeClaim claim, bool draft)
    {
        var input = FeeJson.Read<FeePreparation>(claim.InputJson); var result = FeeJson.Read<FeeCalculationResult>(claim.ResultJson);
        var writer = new InvestmentSummaryService.SimplePdfWriter("Performance-driven fee calculation", claim.Reference, draft ? "DRAFT - NOT FOR SUBMISSION" : null);
        writer.WriteReportHeader("Performance-Driven Fee Calculation", "Kanaan Asset Managers",
            [("Kanaan ID", profile.Account.Client.KanaanId ?? "-"), ("Account", profile.Account.AccountNumber ?? "-"), ("Claim", claim.Reference), ("Period", $"{claim.From:yyyy-MM-dd} to {claim.To:yyyy-MM-dd}")]);
        writer.WriteParagraph($"Client: {profile.Account.Client.DisplayName}. Administrator: {input.Administrator}. Wrap: {input.WrapFund}.");
        writer.WriteParagraph($"All amounts are ZAR. Administrator fee inception: {input.Inception?.ToString("yyyy-MM-dd") ?? "not established"}. Each fee scope retains its own mandate, hurdle and protected capital.");
        if (draft && result.Issues.Count != 0)
        {
            if (result.CalculationIssues.Count > 0) { writer.WriteSection("Missing calculation inputs"); foreach (var issue in result.CalculationIssues.Distinct()) writer.WriteParagraph(issue); }
            var review = result.Issues.Except(result.CalculationIssues).ToList();
            if (review.Count > 0) { writer.WriteSection("Review before approval"); foreach (var issue in review) writer.WriteParagraph(issue); }
        }
        if (result.Wraps.Count > 0)
        {
            writer.WriteSection("Separate wrap calculations");
            writer.WriteTable(["Wrap", "Protected ZAR", "Excess ZAR", "PDF ex VAT", "VAT", "Total ZAR"], [169, 120, 120, 120, 120, 120],
                result.Wraps.Select(x => new[] { x.Code + " " + x.Name, x.Result.IsZeroRateReview ? "Not applicable" : x.Result.CanShowCalculation ? Number(x.Result.Threshold) : "Pending", x.Result.CanShowCalculation && !x.Result.IsZeroRateReview ? Number(x.Result.Outperformance) : "-",
                    x.Result.CanShowCalculation ? Number(x.Result.FeeExVat) : "-", x.Result.IsReady || x.Result.HasVatCalculation ? Number(x.Result.Vat) : "Pending", x.Result.IsReady || x.Result.HasVatCalculation ? Number(x.Result.Total) : "Pending" }));
            foreach (var wrap in result.Wraps)
                WriteScope(writer, input.Wraps.Single(x => x.Code == wrap.Code).Input, wrap.Result, draft, wrap.Code);
            if (result.IsReady) writer.WriteMetricGrid([("PDF excluding VAT", Zar(result.FeeExVat)), ("VAT", Zar(result.Vat)), ("Total incl. VAT", Zar(result.Total))]);
            if (input.Transfers.Count > 0)
            {
                writer.WriteSection("Protected capital transfers");
                writer.WriteTable(["Date", "From", "To", "Amount ZAR"], [190, 190, 190, 199], input.Transfers.Select(t => new[] { t.Date.ToString("yyyy-MM-dd"), t.FromWrap, t.ToWrap, Number(t.Amount) }));
            }
        }
        else WriteScope(writer, input, result, draft, input.WrapFund);
        if (input.OtherScopes.Count > 0)
        {
            writer.WriteSection("Other statement holdings");
            foreach (var scope in input.OtherScopes) writer.WriteParagraph($"{scope.Code} {scope.Name}; closing {Zar(scope.ClosingValue)}. {scope.Reason}");
        }
        WriteEvidenceAppendix(writer, input);
        if (!draft) writer.WriteParagraph($"Approved by {claim.ApprovedBy} at {claim.ApprovedAtUtc:yyyy-MM-dd HH:mm} UTC. Reference: {claim.Reference}.");
        return writer.Build();
    }

    private static void WriteEvidenceAppendix(InvestmentSummaryService.SimplePdfWriter writer, FeePreparation input)
    {
        writer.WriteSection("Evidence and reconciliation appendix");
        if (input.CapitalTraces.Count > 0)
        {
            writer.WriteSection("Staged capital and settlement trace");
            foreach (var trace in input.CapitalTraces.Distinct())
            {
                writer.WriteParagraph($"{trace.Account}/{trace.Wrap}: {trace.From:yyyy-MM-dd} to {trace.To:yyyy-MM-dd}; capital {Zar(trace.Amount)}. {trace.Status}.");
                writer.WriteParagraph("Original rows: " + PublicSourceText(trace.Reference));
            }
        }
        if (input.StatementHistory is { } history)
        {
            writer.WriteParagraph($"Administrator statement coverage: {history.Status}. First funding evidenced: {history.FirstFundingDate:yyyy-MM-dd}. This is separate from any investment history with an earlier administrator.");
            foreach (var source in history.Sources)
            { writer.WriteParagraph($"Original statement: {source.FileName}"); writer.WriteParagraph($"SHA256: {source.Sha256}"); }
            foreach (var period in history.Periods)
            {
                writer.WriteSection($"Account summary {period.From:yyyy-MM-dd} to {period.To:yyyy-MM-dd}");
                writer.WriteParagraph($"Opening {Zar(period.OpeningValue)}; closing {(period.ClosingValue is { } closing ? Zar(closing) : "not supplied")}. Source: {period.OpeningReference}; {period.ClosingReference}.");
                writer.WriteTable(["Administrator summary movement", "Signed ZAR"], [604, 165], period.Entries.Select(x => new[] { x.Description, Number(x.Amount) }));
                if (period.ClosingValue is { } end)
                    writer.WriteParagraph($"Opening + signed summary movements (including market movement) - closing = {Zar(period.OpeningValue + period.Entries.Sum(x => x.Amount) - end)}. Reinvestments, switches and market movement are not automatically new external funding.");
            }
        }
        foreach (var wrap in input.Wraps.Count > 0 ? input.Wraps : [new FeeWrapPreparation { Code = input.WrapFund, Input = input }])
        {
            var basis = wrap.Input;
            writer.WriteSection(wrap.Code + " - mandate and fee protection evidence");
            writer.WriteParagraph($"Mandate effective {basis.MandateEffectiveFrom:yyyy-MM-dd}; fee {Percent(basis.FeePercent)} excluding VAT; hurdle CPI plus {Percent(basis.HurdleSpreadPercent)}; VAT {Percent(basis.VatPercent)}.");
            if (basis.UseSharedPolicy && basis.SharedPolicy is { } policy)
                writer.WriteParagraph($"Kanaan calculation convention {policy.Version}; " + (policy.ApprovedAtUtc is null ? "proposed, not yet adopted by a KI." : $"adopted by {policy.ApprovedBy} at {policy.ApprovedAtUtc:yyyy-MM-dd HH:mm} UTC.") + $" VAT treatment recorded by {policy.RecordedBy} at {policy.RecordedAtUtc:yyyy-MM-dd HH:mm} UTC.");
            foreach (var reference in PublicFileReferences(basis.MandateReference)) writer.WriteParagraph("Mandate source: " + reference);
            writer.WriteParagraph(basis.FeePercent == 0 && basis.ZeroRateConfirmed ? "The evidenced 0% mandate produces no claim and does not establish or reset a financial HWM." :
                basis.NoPriorFeeConfirmed ? "Recorded previous-fee review: no earlier performance fee identified in the reviewed history for this scope." :
                "Opening fee protection uses the recorded checkpoint or carried source-account protection; this is not a fresh market-value reset.");
            foreach (var reference in PublicFileReferences(basis.OpeningReference + "; " + basis.VatReference)) writer.WriteParagraph("Supporting source: " + reference);
            foreach (var transfer in basis.OpeningTransfers)
                writer.WriteParagraph($"Opening transfer from {transfer.Account}/{transfer.Wrap} on {transfer.Date:yyyy-MM-dd}: capital {Zar(transfer.Amount)}, carried benchmark {Zar(transfer.Benchmark)}, carried HWM {Zar(transfer.HighWaterMark)}. {transfer.Reference.Split("; continuity:", StringSplitOptions.None)[0]}");
            foreach (var movement in basis.Movements.Where(x => x.Kind == "TransferIn"))
                writer.WriteParagraph($"Transfer on {movement.Date:yyyy-MM-dd}: capital {Zar(movement.Amount)}, carried benchmark {Zar(movement.TransferredBenchmark ?? 0)}, carried HWM {Zar(movement.TransferredHighWaterMark ?? 0)}. {movement.Reference.Split("; continuity:", StringSplitOptions.None)[0]}");
            foreach (var reference in basis.Benchmarks.Select(x => x.Reference).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
                writer.WriteParagraph("CPI source: " + PublicSourceText(reference));
        }
    }

    private static IEnumerable<string> PublicFileReferences(string text) => Regex.Matches(text, @"(?i)([^\\/;\r\n]+\.(?:pdf|docx?|xlsx?|msg|eml))\b")
        .Select(x => x.Groups[1].Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);

    private static string PublicSourceText(string text) => Regex.Replace(text,
        @"(?i)https?://[^\s;]+|(?<![A-Z0-9])(?:[A-Z]:[\\/]|\\\\|/(?:home|tmp|Users|mnt|var)/)[^;\r\n]*?\.(?:pdf|docx?|xlsx?|csv|msg|eml)\b",
        match => match.Value.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? match.Value : Regex.Split(match.Value, @"[\\/]").Last());

    private static void WriteScope(InvestmentSummaryService.SimplePdfWriter writer, FeePreparation input, FeeCalculationResult result, bool draft, string label)
    {
        writer.WriteSection(label);
        writer.WriteParagraph(FeeResultDescription.Describe(result));
        if (result.IsZeroRateReview)
        {
            writer.WriteParagraph($"Mandate effective: {input.MandateEffectiveFrom:yyyy-MM-dd}. PDF rate: 0%. " + (result.IsReady ? $"PDF payable: {Zar(0)}. This verifies the zero-rate fee only; it does not reset or establish a financial high-water mark. No CPI or VAT assumption is required to levy no fee." : "Zero-rate mandate coverage still requires verification; no fee amount is asserted."));
        }
        else writer.WriteParagraph($"Mandate effective: {input.MandateEffectiveFrom:yyyy-MM-dd}. Fee excluding VAT: {Percent(input.FeePercent)}; hurdle: CPI plus {Percent(input.HurdleSpreadPercent)}; VAT: {Percent(input.VatPercent)}.");
        if (result.CanShowCalculation && !result.IsZeroRateReview)
        {
            writer.WriteMetricGrid([("Closing value before PDF", Zar(input.ClosingValue!.Value)), ("Protected threshold", Zar(result.Threshold)), ("Chargeable excess", Zar(result.Outperformance)), ("PDF excluding VAT", Zar(result.FeeExVat)), ("Total incl. VAT", result.IsReady || result.HasVatCalculation ? Zar(result.Total) : "Not confirmed")]);
            writer.WriteParagraph($"Opening actual capital: {Zar(input.OpeningValue!.Value)}. VAT on this fee: " + (result.IsReady || result.HasVatCalculation ? Zar(result.Vat) : "Not confirmed; excluding-VAT arithmetic is not a final claim."));
            if (!result.IsReady) writer.WriteNote("Calculated draft only. The stated calculation convention and outstanding review requirements must be resolved before KI approval and submission.");
            writer.WriteSection("Benchmark and high-water mark");
            writer.WriteTable(["Date", "Calculation step", "Benchmark ZAR", "HWM ZAR"], [100, 399, 135, 135], result.Steps.Select(x => new[] { x.Date.ToString("yyyy-MM-dd"), x.Description, Number(x.Benchmark), Number(x.HighWaterMark) }));
            writer.WriteSection("Evidenced hurdle periods");
            writer.WriteTable(["From", "To", "CPI %", "Spread %"], [190, 190, 190, 199], input.Benchmarks.Select(x => new[] { x.From.ToString("yyyy-MM-dd"), x.To.ToString("yyyy-MM-dd"), x.CpiPercent.ToString("0.####", CultureInfo.InvariantCulture), input.HurdleSpreadPercent?.ToString("0.####", CultureInfo.InvariantCulture) ?? "" }));
            if (input.Movements.Count > 0)
            {
                writer.WriteSection("Actual capital movements");
                writer.WriteTable(["Date", "Movement", "Amount ZAR", "Value before withdrawal ZAR"], [110, 220, 190, 249],
                    input.Movements.OrderBy(x => x.Date).Select(x => new[] { x.Date.ToString("yyyy-MM-dd"), x.Kind, Number(x.Amount), x.ValueBeforeWithdrawal is { } value ? Number(value) : "-" }));
            }
            writer.WriteSection("Calculation convention");
            writer.WriteParagraph("CPI-plus-spread benchmark compounds by actual days / 365. Contributions increase protected capital; withdrawals reduce it proportionally to verified pre-withdrawal value. Matched allocations from non-fee-bearing cash protect the amount allocated, not new client funding or wrap performance. Fee-bearing scope transfers carry source protection, never a fresh reset. The fee applies only above the higher adjusted benchmark/HWM. A positive crystallisation establishes after-fee capital; a zero-fee review preserves underperformance. Amounts are rounded to cents, half away from zero.");
        }
        if (draft)
        {
            foreach (var note in input.AutomationNotes) writer.WriteParagraph(note);
            writer.WriteSection("Internal preparation evidence");
            foreach (var note in new[] { input.ScopeRationale, input.MethodRationale, input.OpeningReference, input.PriorFeeReviewReference, input.ClosingReference, input.CashFlowReference, input.VatReference }.Where(x => !string.IsNullOrWhiteSpace(x)))
                writer.WriteParagraph(note);
        }
    }
    private static string Percent(decimal? value) => value is { } number ? number.ToString("0.####", CultureInfo.InvariantCulture) + "%" : "Not confirmed";
    private static async Task Put(ZipArchive zip, string name, byte[] bytes)
    { await using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open(); await stream.WriteAsync(bytes); }
    private static string Csv(string value)
    {
        if (value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Zar(decimal value) => "R " + value.ToString("N2", CultureInfo.InvariantCulture);
}
