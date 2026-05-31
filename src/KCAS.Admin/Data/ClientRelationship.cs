using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class ClientRelationship
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    [MaxLength(40)]
    public string RelationshipType { get; set; } = string.Empty;

    public int? LegacyRelatedClientId { get; set; }

    [MaxLength(200)]
    public string? Name { get; set; }

    [MaxLength(50)]
    public string? Initials { get; set; }

    [MaxLength(20)]
    public string? Gender { get; set; }

    public DateTime? BirthDate { get; set; }

    [MaxLength(13)]
    public string? SouthAfricanIdNumber { get; set; }

    [MaxLength(254)]
    public string? Email { get; set; }

    [MaxLength(30)]
    public string? HomePhone { get; set; }

    [MaxLength(30)]
    public string? WorkPhone { get; set; }

    [MaxLength(30)]
    public string? MobilePhone { get; set; }

    [MaxLength(150)]
    public string? Employer { get; set; }

    [MaxLength(150)]
    public string? Occupation { get; set; }

    [MaxLength(150)]
    public string? HighestQualification { get; set; }

    public decimal? GrossMonthlySalary { get; set; }

    public decimal? GrossAnnualSalary { get; set; }

    public decimal? YearlyBonus { get; set; }

    public decimal? OtherIncome { get; set; }

    [MaxLength(150)]
    public string? PensionFundName { get; set; }

    public decimal? EmployerPensionContributionAmount { get; set; }

    public decimal? EmployerPensionContributionPercent { get; set; }
}
