using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientEvidenceException
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    public int ClientEvidenceRequirementId { get; set; }

    public ClientEvidenceRequirement Requirement { get; set; } = null!;

    public string Reason { get; set; } = "";

    [MaxLength(191)]
    public string ApprovedBy { get; set; } = "";

    public DateTime ApprovedAtUtc { get; set; } = DateTime.UtcNow;

    public DateOnly? ReviewDate { get; set; }

    public bool IsActive { get; set; } = true;
}
