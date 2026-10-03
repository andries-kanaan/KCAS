using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientBraRiskReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientBraRiskReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    SourceContentHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    MethodVersion = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    BraReference = table.Column<string>(type: "longtext", nullable: false),
                    ContentJson = table.Column<string>(type: "longtext", nullable: false),
                    PerformedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    RecordedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ImportPackageId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientBraRiskReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientBraRiskReports_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientBraRiskReports_ClientId_RecordedAtUtc",
                table: "ClientBraRiskReports",
                columns: new[] { "ClientId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientBraRiskReports");
        }
    }
}
