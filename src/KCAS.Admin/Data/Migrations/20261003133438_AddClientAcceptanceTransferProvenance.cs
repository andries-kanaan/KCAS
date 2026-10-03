using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientAcceptanceTransferProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImportSourceReference",
                table: "ClientOnboardingProfiles",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportSourceReference",
                table: "ClientCodexReviewRequests",
                type: "longtext",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImportSourceReference",
                table: "ClientOnboardingProfiles");

            migrationBuilder.DropColumn(
                name: "ImportSourceReference",
                table: "ClientCodexReviewRequests");
        }
    }
}
