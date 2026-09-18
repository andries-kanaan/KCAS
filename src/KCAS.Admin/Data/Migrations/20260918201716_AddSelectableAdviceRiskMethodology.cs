using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSelectableAdviceRiskMethodology : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RiskMethodologyCode",
                table: "ClientAdviceCases",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "KCAS_EVIDENCED_V1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RiskMethodologyCode",
                table: "ClientAdviceCases");
        }
    }
}
