using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class SurfaceLegacyClientSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EmployerPensionContributionAmount",
                table: "ClientRelationships",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EmployerPensionContributionPercent",
                table: "ClientRelationships",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossAnnualSalary",
                table: "ClientRelationships",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossMonthlySalary",
                table: "ClientRelationships",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OtherIncome",
                table: "ClientRelationships",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PensionFundName",
                table: "ClientRelationships",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "YearlyBonus",
                table: "ClientRelationships",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FamilyDetailRaw",
                table: "ClientPersonalProfiles",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WorkdayTravelPercent",
                table: "ClientPersonalProfiles",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PensionFundTax",
                table: "ClientFinancialProfiles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PreservationFundLumpSumPercent",
                table: "ClientFinancialProfiles",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RepresentativeAlternativeInvestmentsPercent",
                table: "ClientFinancialProfiles",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RepresentativeEquitiesPercent",
                table: "ClientFinancialProfiles",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RepresentativeFixedPropertyPercent",
                table: "ClientFinancialProfiles",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RepresentativeName",
                table: "ClientFinancialProfiles",
                type: "varchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RepresentativeOffshorePercent",
                table: "ClientFinancialProfiles",
                type: "decimal(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RetirementAnnuityTax",
                table: "ClientFinancialProfiles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RetirementProvisionTax",
                table: "ClientFinancialProfiles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmployerPensionContributionAmount",
                table: "ClientRelationships");

            migrationBuilder.DropColumn(
                name: "EmployerPensionContributionPercent",
                table: "ClientRelationships");

            migrationBuilder.DropColumn(
                name: "GrossAnnualSalary",
                table: "ClientRelationships");

            migrationBuilder.DropColumn(
                name: "GrossMonthlySalary",
                table: "ClientRelationships");

            migrationBuilder.DropColumn(
                name: "OtherIncome",
                table: "ClientRelationships");

            migrationBuilder.DropColumn(
                name: "PensionFundName",
                table: "ClientRelationships");

            migrationBuilder.DropColumn(
                name: "YearlyBonus",
                table: "ClientRelationships");

            migrationBuilder.DropColumn(
                name: "FamilyDetailRaw",
                table: "ClientPersonalProfiles");

            migrationBuilder.DropColumn(
                name: "WorkdayTravelPercent",
                table: "ClientPersonalProfiles");

            migrationBuilder.DropColumn(
                name: "PensionFundTax",
                table: "ClientFinancialProfiles");

            migrationBuilder.DropColumn(
                name: "PreservationFundLumpSumPercent",
                table: "ClientFinancialProfiles");

            migrationBuilder.DropColumn(
                name: "RepresentativeAlternativeInvestmentsPercent",
                table: "ClientFinancialProfiles");

            migrationBuilder.DropColumn(
                name: "RepresentativeEquitiesPercent",
                table: "ClientFinancialProfiles");

            migrationBuilder.DropColumn(
                name: "RepresentativeFixedPropertyPercent",
                table: "ClientFinancialProfiles");

            migrationBuilder.DropColumn(
                name: "RepresentativeName",
                table: "ClientFinancialProfiles");

            migrationBuilder.DropColumn(
                name: "RepresentativeOffshorePercent",
                table: "ClientFinancialProfiles");

            migrationBuilder.DropColumn(
                name: "RetirementAnnuityTax",
                table: "ClientFinancialProfiles");

            migrationBuilder.DropColumn(
                name: "RetirementProvisionTax",
                table: "ClientFinancialProfiles");
        }
    }
}
