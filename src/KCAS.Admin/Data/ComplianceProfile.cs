namespace KCAS.Admin.Data;

public sealed class ComplianceProfile
{
    public int Id { get; set; }
    public string LegalName { get; set; } = "";
    public string? TradingName { get; set; }
    public string? FspNumber { get; set; }
    public string? AccountableInstitutionNumber { get; set; }
    public string? PrimaryContactName { get; set; }
    public string? PrimaryContactEmail { get; set; }
    public string? PrimaryContactPhone { get; set; }
    public string? RegisteredAddress { get; set; }
    public string? OperatingAddress { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string Status { get; set; } = ComplianceStatuses.Draft;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}
