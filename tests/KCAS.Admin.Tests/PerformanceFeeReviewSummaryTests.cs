using System.Text;
using KCAS.Admin.Data;

namespace KCAS.Admin.Tests;

public sealed class PerformanceFeeReviewSummaryTests
{
    [Fact]
    public void Ready_incomplete_stale_and_approved_rows_never_share_an_approval_total()
    {
        var (workspace, rows) = Example();
        var summary = rows.Select(x => FeeReviewSummaryLine.Create(workspace.Accounts.Single(a => a.Profile == x.Profile), x.Claim)).ToList();
        Assert.Single(summary, x => x.ReadyForApproval);
        Assert.Equal(1950, summary.Where(x => x.ReadyForApproval).Sum(x => x.FeeExVat));
        Assert.Equal(2242.5m, summary.Where(x => x.ReadyForApproval).Sum(x => x.Total));
        Assert.Null(summary[1].FeeExVat); Assert.Null(summary[1].Total);
        Assert.Contains("Review required", summary[1].Status);
        var account = workspace.Accounts[0];
        var input = FeeJson.Read<FeePreparation>(rows[0].Claim.InputJson); input.RequiresRebuild = true;
        rows[0].Claim.InputJson = FeeJson.Write(input);
        var stale = FeeReviewSummaryLine.Create(account, rows[0].Claim);
        Assert.False(stale.ReadyForApproval); Assert.Null(stale.FeeExVat); Assert.Equal("Refresh required", stale.Status);
        rows[0].Claim.InputJson = FeeJson.Write(PerformanceFeeCalculatorTests.Input());
        rows[0].Claim.Status = FeeClaimStages.Approved; rows[0].Claim.ApprovedAtUtc = DateTime.UtcNow;
        var approved = FeeReviewSummaryLine.Create(account, rows[0].Claim);
        Assert.False(approved.ReadyForApproval); Assert.True(approved.Approved);
        rows[0].Claim.Status = FeeClaimStages.Returned;
        Assert.False(FeeReviewSummaryLine.Create(account, rows[0].Claim).Approved);
    }

    [Fact]
    public void Concise_pdf_retains_amounts_status_and_authority_but_not_the_detailed_appendix()
    {
        var (workspace, rows) = Example();
        var bytes = PerformanceFeeExports.ClientSummaryPdf(workspace, rows, true);
        var text = Encoding.Latin1.GetString(bytes);
        Assert.Contains("DRAFT - NOT FOR SUBMISSION", text);
        Assert.Contains("1950.00", text); Assert.Contains("2242.50", text);
        Assert.Contains("Review required", text); Assert.Contains("Pending", text);
        Assert.DoesNotContain("private", text); Assert.DoesNotContain("SHA256", text);
        Assert.DoesNotContain("Internal preparation evidence", text);
        Assert.DoesNotContain("Evidenced hurdle periods", text);
        Assert.DoesNotContain("Material synthetic internal deliberation", text);
        var detailed = PerformanceFeeExports.CalculationPdf(rows[0].Profile, rows[0].Claim, true);
        Assert.True(detailed.Length > bytes.Length);
        rows[0].Claim.ApprovedBy = "Synthetic KI"; rows[0].Claim.ApprovedAtUtc = new(2026, 1, 1);
        rows[0].Claim.Status = FeeClaimStages.Approved;
        text = Encoding.Latin1.GetString(PerformanceFeeExports.ClientSummaryPdf(workspace, [rows[0]], false));
        Assert.DoesNotContain("DRAFT - NOT FOR SUBMISSION", text);
        Assert.Contains("Synthetic KI", text); Assert.Contains(rows[0].Claim.Reference, text);
        Assert.Contains("Only approved positive amounts may be claimed.", text);
        var qa = Environment.GetEnvironmentVariable("KCAS_FEE_SUMMARY_QA_PATH");
        if (!string.IsNullOrWhiteSpace(qa))
        {
            Directory.CreateDirectory(qa);
            File.WriteAllBytes(Path.Combine(qa, "synthetic-draft-summary.pdf"), bytes);
            File.WriteAllBytes(Path.Combine(qa, "synthetic-approved-summary.pdf"), PerformanceFeeExports.ClientSummaryPdf(workspace, [rows[0]], false));
        }
    }

    private static (FeeClientWorkspace Workspace, List<(PerformanceFeeProfile Profile, PerformanceFeeClaim Claim)> Rows) Example()
    {
        var client = new Client { DisplayName = "Synthetic summary investor", KanaanId = "QA-SUMMARY" };
        var rows = new List<(PerformanceFeeProfile, PerformanceFeeClaim)>();
        var accounts = new List<FeeClientAccount>();
        for (var i = 0; i < 2; i++)
        {
            var account = new ClientInvestmentAccount { Id = i + 1, Client = client, AccountNumber = "QA-SUMMARY-" + i };
            var input = PerformanceFeeCalculatorTests.Input();
            input.StatementHistory = new() { SourceFingerprint = "synthetic-summary-source" };
            input.MandateReference = "C:\\private\\mandate.pdf"; input.MethodRationale = "Material synthetic internal deliberation";
            if (i == 1) input.ClosingValue = null;
            var result = PerformanceFeeCalculator.Calculate(input, input.To!.Value);
            var claim = new PerformanceFeeClaim { Id = i + 1, From = input.From!.Value, To = input.To.Value, InputJson = FeeJson.Write(input), ResultJson = FeeJson.Write(result) };
            var profile = new PerformanceFeeProfile { Id = i + 1, ClientInvestmentAccountId = account.Id, Account = account, Claims = [claim] };
            accounts.Add(new(account.AccountNumber, account, profile, new() { History = new() { SourceFingerprint = "synthetic-summary-source" } }, null));
            rows.Add((profile, claim));
        }
        return (new(client, "version", [], accounts, []), rows);
    }
}
