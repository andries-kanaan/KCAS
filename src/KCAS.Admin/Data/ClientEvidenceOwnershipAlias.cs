using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientEvidenceOwnershipAlias
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    [MaxLength(512)]
    public string FolderPath { get; set; } = "";

    [MaxLength(160)]
    public string Alias { get; set; } = "";

    public bool IsJoint { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(191)]
    public string? CreatedBy { get; set; }
}
