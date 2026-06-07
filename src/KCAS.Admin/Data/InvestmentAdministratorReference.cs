using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class InvestmentAdministratorReference
{
    public int Id { get; set; }

    public int? LegacyLispId { get; set; }

    [MaxLength(256)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? ShortName { get; set; }

    public bool IsCurrent { get; set; } = true;

    public bool MonthlyUpload { get; set; }

    [MaxLength(256)]
    public string? OpenedBy { get; set; }

    [MaxLength(256)]
    public string? UpdatedBy { get; set; }

    public DateTime? LegacyOpenedAt { get; set; }

    public DateTime? LegacyUpdatedAt { get; set; }
}
