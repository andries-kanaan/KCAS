using System.Security.Cryptography;
using System.Text;

namespace KCAS.Admin.Data;

public sealed class FeeStatementHistory
{
    public string SourceFingerprint { get; set; } = "";
    public string Status { get; set; } = "Detailed history only";
    public bool BeginningCovered { get; set; }
    public DateOnly? FirstFundingDate { get; set; }
    public string FirstFundingReference { get; set; } = "";
    public bool StartsWithTransfer { get; set; }
    public bool FeeScopeBeginningCovered { get; set; }
    public List<FeeStatementScopeEntry> FeeScopeEntries { get; set; } = [];
    public List<FeeStatementPeriod> Periods { get; set; } = [];
    public List<FeeStatementSource> Sources { get; set; } = [];
    public List<string> Issues { get; set; } = [];

    internal static FeeStatementHistory Build(List<FeeStatementDocument> documents, string account, FeeStatementLedger ledger)
    {
        var history = new FeeStatementHistory
        {
            Sources = documents.Where(x => x.Rows.Any(r => r.Account.Equals(account, StringComparison.OrdinalIgnoreCase)))
                .Select(x => new FeeStatementSource(x.FileName, x.Sha256)).Distinct().ToList(),
            Periods = documents.SelectMany(x => x.Periods).Where(x => x.Account.Equals(account, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(x => FeeJson.Write(x)).OrderBy(x => x.From).ThenBy(x => x.To).ThenBy(x => x.OpeningReference, StringComparer.Ordinal).ToList()
        };
        var first = history.Periods.FirstOrDefault();
        if (first is not null)
        {
            history.FeeScopeEntries = FeeStatementScopeEntries.Find(ledger, first.From);
            history.FeeScopeBeginningCovered = FeeStatementScopeEntries.Covers(ledger, history.FeeScopeEntries);
        }
        foreach (var period in history.Periods)
        {
            if (period.To is null || period.To < period.From || period.ClosingValue is null)
            { history.Issues.Add($"Incomplete account summary at {period.OpeningReference}; obtain the dated closing summary."); continue; }
            var openings = ledger.Rows.Where(x => x.Kind == "Opening" && x.Date == period.From).ToList();
            var closings = ledger.Rows.Where(x => x.Kind == "Closing" && x.Date == period.To).ToList();
            decimal Tolerance(int count) => Math.Max(0.02m, count * 0.005m);
            if (openings.Count == 0 || Math.Abs(openings.Sum(x => x.Amount) - period.OpeningValue) > Tolerance(openings.Count))
                history.Issues.Add($"{period.From:yyyy-MM-dd}: account summary opening {period.OpeningValue:N2} does not reconcile with detailed opening holdings; {period.OpeningReference}.");
            if (closings.Count == 0 || Math.Abs(closings.Sum(x => x.Amount) - period.ClosingValue.Value) > Tolerance(closings.Count))
                history.Issues.Add($"{period.To:yyyy-MM-dd}: account summary closing {period.ClosingValue:N2} does not reconcile with detailed closing holdings; {period.ClosingReference}.");
            if (Math.Abs(period.OpeningValue + period.Entries.Sum(x => x.Amount) - period.ClosingValue.Value) > Tolerance(period.Entries.Count))
                history.Issues.Add($"{period.From:yyyy-MM-dd} to {period.To:yyyy-MM-dd}: summary opening + signed movements + market movement does not reconcile to closing; {period.ClosingReference}.");
        }
        foreach (var group in history.Periods.GroupBy(x => (x.From, x.To)))
            if (group.Select(x => (x.OpeningValue, x.ClosingValue)).Distinct().Count() > 1)
                history.Issues.Add($"Conflicting account summaries for {group.Key.From:yyyy-MM-dd} to {group.Key.To:yyyy-MM-dd}.");
        if (first is not null)
        {
            var funding = ledger.Rows.Where(x => x.Date >= first.From && x.Amount > 0 && x.Kind is "Contribution" or "Transfer" or "FundAllocation")
                .OrderBy(x => x.Date).ThenBy(x => x.SourceRow).ToList();
            if (first.OpeningValue == 0 && ledger.Rows.Where(x => x.Kind == "Opening" && x.Date == first.From).All(x => x.UnitBalance == 0 && x.Amount == 0) && funding.Count > 0)
            {
                history.BeginningCovered = true; history.FirstFundingDate = funding[0].Date;
                var initial = funding.Where(x => x.Date == history.FirstFundingDate).ToList();
                history.FirstFundingReference = string.Join("; ", initial.Select(x => x.Source));
                history.StartsWithTransfer = initial.Any(x => x.Kind == "Transfer");
                history.Status = history.StartsWithTransfer ? "Beginning covered; transfer source required" : "Beginning covered";
            }
            else if (first.OpeningValue != 0)
            {
                if (history.FeeScopeBeginningCovered)
                    history.Status = "Fee-wrap beginning supplied; earlier holdings outside this fee scope";
                else
                {
                    history.Status = "Earlier AIMS/Glacier statement required";
                    history.Issues.Add($"Earlier AIMS/Glacier statement required before {first.From:yyyy-MM-dd}: this account opens at {first.OpeningValue:N2}. Do not request history with an earlier administrator or reset the opening HWM to market value.");
                }
            }
            else history.Status = "Initial funding evidence required";
        }
        if (ledger.Issues.Any(x => x.StartsWith("Statement coverage gap:", StringComparison.Ordinal))) history.Status = "Missing statement period";
        else if (history.Issues.Any(x => !x.StartsWith("Earlier AIMS/Glacier", StringComparison.Ordinal))) history.Status = "Summary reconciliation required";
        history.SourceFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FeeJson.Write(new
        {
            Parser = PerformanceFeeStatementReader.CurrentVersion, HistoryAlgorithm = "FeeScopeEntryV1", history.FeeScopeEntries,
            Sources = documents.Where(d => d.Rows.Any(r => r.Account.Equals(account, StringComparison.OrdinalIgnoreCase))).Select(d => new { d.FileName, d.Sha256 }).OrderBy(x => x.Sha256),
            history.Periods, Rows = ledger.Rows.Select(x => new { x.Key, x.Kind }), ledger.Issues
        }))));
        return history;
    }
}
public sealed record FeeStatementSource(string FileName, string Sha256);
