using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceOfFundsInvestmentCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientEvidenceInvestmentLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientEvidenceItemId = table.Column<int>(type: "int", nullable: false),
                    ClientInvestmentAccountId = table.Column<int>(type: "int", nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LinkedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEvidenceInvestmentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientEvidenceInvestmentLinks_ClientEvidenceItems_ClientEvid~",
                        column: x => x.ClientEvidenceItemId,
                        principalTable: "ClientEvidenceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientEvidenceInvestmentLinks_ClientInvestmentAccounts_Clien~",
                        column: x => x.ClientInvestmentAccountId,
                        principalTable: "ClientInvestmentAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceInvestmentLinks_ClientEvidenceItemId_ClientInv~",
                table: "ClientEvidenceInvestmentLinks",
                columns: new[] { "ClientEvidenceItemId", "ClientInvestmentAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceInvestmentLinks_ClientInvestmentAccountId",
                table: "ClientEvidenceInvestmentLinks",
                column: "ClientInvestmentAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientEvidenceInvestmentLinks");
        }
    }
}
