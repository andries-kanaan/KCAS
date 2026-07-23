using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientEvidenceScanRoot
{
    public int Id { get; set; }

    [MaxLength(512)]
    public string RootPath { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAtUtc { get; set; }

    [MaxLength(191)]
    public string? UpdatedBy { get; set; }
}
