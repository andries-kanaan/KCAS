using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveArtificialIdentityAddressExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE `ClientEvidenceRequirements`
                SET `RequiresExpiryDate` = FALSE
                WHERE `EvidenceType` IN ('Identity', 'Address');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE `ClientEvidenceRequirements`
                SET `RequiresExpiryDate` = TRUE
                WHERE `EvidenceType` IN ('Identity', 'Address');
                """);
        }
    }
}
