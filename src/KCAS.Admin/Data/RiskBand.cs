namespace KCAS.Admin.Data;

public sealed class RiskBand
{
    public int Id { get; set; }
    public int RiskMethodologyVersionId { get; set; }
    public RiskMethodologyVersion? MethodologyVersion { get; set; }
    public string Name { get; set; } = "";
    public decimal MinimumScore { get; set; }
    public decimal? MaximumScore { get; set; }
    public int SortOrder { get; set; }
}
