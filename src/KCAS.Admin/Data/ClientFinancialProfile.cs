using System.ComponentModel.DataAnnotations;

namespace KCAS.Admin.Data;

public class ClientFinancialProfile
{
    public int ClientId { get; set; }

    public Client Client { get; set; } = null!;

    [MaxLength(150)]
    public string? Employer { get; set; }

    [MaxLength(150)]
    public string? Occupation { get; set; }

    public decimal? GrossMonthlySalary { get; set; }

    public decimal? GrossAnnualSalary { get; set; }

    public decimal? MonthlyExpenses { get; set; }

    public decimal? YearlyBonus { get; set; }

    public decimal? OtherIncome { get; set; }

    public int? RetirementAge { get; set; }

    [MaxLength(150)]
    public string? PensionFundName { get; set; }

    public decimal? EmployerPensionContributionAmount { get; set; }

    public decimal? EmployerPensionContributionPercent { get; set; }

    public decimal? CapitalRequirementPercent { get; set; }

    public decimal? MinimumRetirementIncomePercent { get; set; }

    public decimal? ExpectedRetirementIncomePercent { get; set; }

    public decimal? PreservationFundLumpSumPercent { get; set; }

    public decimal? RetirementProvisionTax { get; set; }

    public decimal? PensionFundTax { get; set; }

    public decimal? RetirementAnnuityTax { get; set; }

    [MaxLength(150)]
    public string? RepresentativeName { get; set; }

    public decimal? RepresentativeEquitiesPercent { get; set; }

    public decimal? RepresentativeAlternativeInvestmentsPercent { get; set; }

    public decimal? RepresentativeFixedPropertyPercent { get; set; }

    public decimal? RepresentativeOffshorePercent { get; set; }

    [MaxLength(1000)]
    public string? BankDetailRaw { get; set; }

    [MaxLength(1000)]
    public string? WillDetailRaw { get; set; }

    [MaxLength(1000)]
    public string? OtherGoalsRaw { get; set; }

    [MaxLength(1000)]
    public string? OtherDetailsRaw { get; set; }
}
