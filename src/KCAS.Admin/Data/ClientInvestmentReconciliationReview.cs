using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientInvestmentReconciliationReview
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    public int ClientInvestmentAccountId { get; set; }

    public ClientInvestmentAccount InvestmentAccount { get; set; } = null!;

    [MaxLength(32)]
    public string Outcome { get; set; } = ClientInvestmentReconciliationOutcomes.NeedsFollowUp;

    public int? RelatedClientInvestmentAccountId { get; set; }

    public ClientInvestmentAccount? RelatedInvestmentAccount { get; set; }

    public DateOnly? AppliedSurrenderDate { get; set; }

    [MaxLength(512)]
    public string EvidenceReference { get; set; } = "";

    [MaxLength(1000)]
    public string Reason { get; set; } = "";

    [MaxLength(64)]
    public string SnapshotSha256 { get; set; } = "";

    public DateTime ReviewedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(191)]
    public string ReviewedBy { get; set; } = "";
}

public static class ClientInvestmentReconciliationOutcomes
{
    public const string Current = "Current";
    public const string HistoricalSurrendered = "HistoricalSurrendered";
    public const string Transferred = "Transferred";
    public const string DuplicateContinuation = "DuplicateContinuation";
    public const string NeedsFollowUp = "NeedsFollowUp";

    public static readonly string[] All =
        [Current, HistoricalSurrendered, Transferred, DuplicateContinuation, NeedsFollowUp];

    public static string Label(string value) => value switch
    {
        Current => "Current",
        HistoricalSurrendered => "Historical - surrendered",
        Transferred => "Transferred",
        DuplicateContinuation => "Duplicate / continuation",
        NeedsFollowUp => "Needs follow-up",
        _ => value
    };
}
