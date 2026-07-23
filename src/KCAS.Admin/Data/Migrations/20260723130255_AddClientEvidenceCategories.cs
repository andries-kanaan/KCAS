using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientEvidenceCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientCategory",
                table: "Clients",
                type: "varchar(96)",
                maxLength: 96,
                nullable: false,
                defaultValue: "NaturalPerson");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_ClientCategory",
                table: "Clients",
                column: "ClientCategory");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clients_ClientCategory",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ClientCategory",
                table: "Clients");
        }
    }
}
