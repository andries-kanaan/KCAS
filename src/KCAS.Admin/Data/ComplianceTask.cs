namespace KCAS.Admin.Data;

public sealed class ComplianceTask
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Owner { get; set; }
    public DateOnly? DueDate { get; set; }
    public string Priority { get; set; } = "Normal";
    public string Status { get; set; } = ComplianceStatuses.Draft;
    public string? LinkedEntityType { get; set; }
    public int? LinkedEntityId { get; set; }
    public string? ClosureNotes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}
