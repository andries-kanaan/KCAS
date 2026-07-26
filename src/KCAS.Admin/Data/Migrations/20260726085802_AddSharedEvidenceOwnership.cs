using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedEvidenceOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OwnershipConfidence",
                table: "ClientEvidenceItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnershipReason",
                table: "ClientEvidenceItems",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OwnershipReviewedAtUtc",
                table: "ClientEvidenceItems",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnershipReviewedBy",
                table: "ClientEvidenceItems",
                type: "varchar(191)",
                maxLength: 191,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnershipStatus",
                table: "ClientEvidenceItems",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Confirmed");

            migrationBuilder.CreateTable(
                name: "ClientEvidenceOwnershipAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    FolderPath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    Alias = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false),
                    IsJoint = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEvidenceOwnershipAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientEvidenceOwnershipAliases_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceItems_ClientId_OwnershipStatus",
                table: "ClientEvidenceItems",
                columns: new[] { "ClientId", "OwnershipStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceOwnershipAliases_ClientId_FolderPath_Alias",
                table: "ClientEvidenceOwnershipAliases",
                columns: new[] { "ClientId", "FolderPath", "Alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceOwnershipAliases_FolderPath_IsActive",
                table: "ClientEvidenceOwnershipAliases",
                columns: new[] { "FolderPath", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientEvidenceOwnershipAliases");

            migrationBuilder.DropIndex(
                name: "IX_ClientEvidenceItems_ClientId_OwnershipStatus",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "OwnershipConfidence",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "OwnershipReason",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "OwnershipReviewedAtUtc",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "OwnershipReviewedBy",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "OwnershipStatus",
                table: "ClientEvidenceItems");
        }
    }
}
