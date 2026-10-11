using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

// Funding history is independent of a client's compliance reconciliation decisions.
public sealed class InvestmentFundingConnection
{
    public int Id { get; set; }
    public int SourceAccountId { get; set; }
    public ClientInvestmentAccount SourceAccount { get; set; } = null!;
    public int DestinationAccountId { get; set; }
    public ClientInvestmentAccount DestinationAccount { get; set; } = null!;
    public DateOnly MatchedThroughDate { get; set; }
    [MaxLength(64)] public string SourceSnapshot { get; set; } = "";
    [MaxLength(64)] public string DestinationSnapshot { get; set; } = "";
    [MaxLength(512)] public string EvidenceReference { get; set; } = "";
    [MaxLength(1000)] public string Reason { get; set; } = "";
    [MaxLength(191)] public string PerformedBy { get; set; } = "";
    [MaxLength(191)] public string RecordedBy { get; set; } = "";
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}
