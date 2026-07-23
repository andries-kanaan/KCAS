namespace KCAS.Admin.Data;

public sealed class RiskFactorDefinition
{
    public int Id { get; set; }
    public int RiskMethodologyVersionId { get; set; }
    public RiskMethodologyVersion? MethodologyVersion { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal Weight { get; set; }
    public bool IsMandatoryHighRiskTrigger { get; set; }
    public int SortOrder { get; set; }
    public List<RiskFactorOption> Options { get; set; } = [];
}
