namespace KCAS.Admin.Data;

public sealed class ControlledDocument
{
    public int Id { get; set; }
    public string DocumentType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Owner { get; set; }
    public string? VersionReference { get; set; }
    public string Status { get; set; } = ComplianceStatuses.Draft;
    public DateOnly? EffectiveDate { get; set; }
    public DateOnly? NextReviewDate { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}
