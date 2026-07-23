namespace KCAS.Admin.Data;

public sealed class RiskFactorOption
{
    public int Id { get; set; }
    public int RiskFactorDefinitionId { get; set; }
    public RiskFactorDefinition? FactorDefinition { get; set; }
    public string Code { get; set; } = "";
    public string Label { get; set; } = "";
    public int Score { get; set; }
    public bool TriggersHighRisk { get; set; }
    public int SortOrder { get; set; }
}
