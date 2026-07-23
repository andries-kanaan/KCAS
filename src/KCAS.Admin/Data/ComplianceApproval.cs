namespace KCAS.Admin.Data;

public sealed class ComplianceApproval
{
    public int Id { get; set; }
    public string TargetEntityType { get; set; } = "";
    public int TargetEntityId { get; set; }
    public string Decision { get; set; } = "";
    public string? Approver { get; set; }
    public DateTime DecidedAtUtc { get; set; } = DateTime.UtcNow;
    public string Reason { get; set; } = "";
}
