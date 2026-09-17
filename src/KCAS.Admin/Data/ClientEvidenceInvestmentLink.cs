using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientEvidenceInvestmentLink
{
    public int Id { get; set; }
    public int ClientEvidenceItemId { get; set; }
    public ClientEvidenceItem EvidenceItem { get; set; } = null!;
    public int ClientInvestmentAccountId { get; set; }
    public ClientInvestmentAccount InvestmentAccount { get; set; } = null!;
    public DateTime LinkedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(191)]
    public string? LinkedBy { get; set; }
}
