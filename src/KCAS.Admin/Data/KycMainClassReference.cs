using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class KycMainClassReference
{
    public int Id { get; set; }

    public int? LegacyMainClassId { get; set; }

    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? AfrikaansDescription { get; set; }

    [MaxLength(512)]
    public string? EnglishDescription { get; set; }

    [MaxLength(256)]
    public string? OpenedBy { get; set; }

    [MaxLength(256)]
    public string? UpdatedBy { get; set; }

    public DateTime? LegacyOpenedAt { get; set; }

    public DateTime? LegacyUpdatedAt { get; set; }

    public ICollection<KycSubClassReference> SubClasses { get; } = [];
}
