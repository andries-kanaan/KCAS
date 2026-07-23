namespace KCAS.Admin.Data;

public sealed class GovernanceRoleAssignment
{
    public int Id { get; set; }
    public string RoleType { get; set; } = "";
    public string PersonName { get; set; } = "";
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? ResponsibilitySummary { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}
