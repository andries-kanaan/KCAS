using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class ClientPersonalProfile
{
    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    [MaxLength(13)]
    public string? SouthAfricanIdNumber { get; set; }

    [MaxLength(20)]
    public string? Gender { get; set; }

    [MaxLength(100)]
    public string? MaritalStatus { get; set; }

    [MaxLength(100)]
    public string? TaxOffice { get; set; }

    [MaxLength(50)]
    public string? TaxNumber { get; set; }

    public bool? IsTaxClient { get; set; }

    [MaxLength(150)]
    public string? HighestQualification { get; set; }

    public bool? Smoker { get; set; }

    public decimal? WorkdayTravelPercent { get; set; }

    public int? NumberOfDependents { get; set; }

    [MaxLength(1000)]
    public string? FamilyDetailRaw { get; set; }
}
