namespace KCAS.Admin.Data;

public sealed class RiskMethodologyVersion
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? VersionLabel { get; set; }
    public string Status { get; set; } = ComplianceStatuses.Draft;
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Summary { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
    public List<RiskFactorDefinition> Factors { get; set; } = [];
    public List<RiskBand> Bands { get; set; } = [];
}
