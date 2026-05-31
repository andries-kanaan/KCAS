using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class ClientNote
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    public int? LegacyClientNoteId { get; set; }

    public DateOnly? NoteDate { get; set; }

    [MaxLength(256)]
    public string? Title { get; set; }

    public string? Details { get; set; }

    public bool IsDeleted { get; set; }

    public bool IsFinal { get; set; } = true;

    [MaxLength(256)]
    public string? OpenedBy { get; set; }

    [MaxLength(256)]
    public string? UpdatedBy { get; set; }

    public int? LegacyOpenedByUserId { get; set; }

    public int? LegacyUpdatedByUserId { get; set; }

    public DateTime? LegacyOpenedAt { get; set; }

    public DateTime? LegacyUpdatedAt { get; set; }

    public string PayloadJson { get; set; } = string.Empty;

    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
}
