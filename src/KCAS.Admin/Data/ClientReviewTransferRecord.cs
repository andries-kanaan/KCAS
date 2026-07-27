using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public sealed class ClientReviewTransferRecord
{
    public long Id { get; set; }

    [MaxLength(36)]
    public string PackageId { get; set; } = "";

    [MaxLength(16)]
    public string Direction { get; set; } = "";

    [MaxLength(64)]
    public string ContentSha256 { get; set; } = "";

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    [MaxLength(32)]
    public string Status { get; set; } = "";

    [MaxLength(260)]
    public string FileName { get; set; } = "";

    [MaxLength(512)]
    public string StoragePath { get; set; } = "";

    public string SummaryJson { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AppliedAtUtc { get; set; }

    [MaxLength(191)]
    public string? AppliedBy { get; set; }
}

public static class ClientReviewTransferDirections
{
    public const string Outgoing = "Outgoing";
    public const string Incoming = "Incoming";
}

public static class ClientReviewTransferStatuses
{
    public const string Exported = "Exported";
    public const string Applied = "Applied";
}
