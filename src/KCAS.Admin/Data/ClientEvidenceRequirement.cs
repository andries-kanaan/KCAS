using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientEvidenceRequirement
{
    public int Id { get; set; }

    [MaxLength(96)]
    public string ClientCategory { get; set; } = "All";

    [MaxLength(96)]
    public string RequirementGroup { get; set; } = "";

    [MaxLength(96)]
    public string EvidenceType { get; set; } = "";

    [MaxLength(240)]
    public string Title { get; set; } = "";

    public string? Description { get; set; }

    public bool IsBlocking { get; set; } = true;

    public bool RequiresVerification { get; set; } = true;

    public bool RequiresExpiryDate { get; set; }

    public int SortOrder { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = ClientEvidenceRequirementStatuses.Active;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    [MaxLength(191)]
    public string? UpdatedBy { get; set; }
}
