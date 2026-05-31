using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientFundValuations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientFundValuations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    LegacyFundId = table.Column<int>(type: "int", nullable: false),
                    LegacyClientId = table.Column<int>(type: "int", nullable: true),
                    KanaanId = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    FundName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    AmountForeign = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AmountZar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    FundDescription = table.Column<string>(type: "longtext", nullable: true),
                    CompanyClientNumber = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Administrator = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ProductName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ProductType = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    CompanyDescription = table.Column<string>(type: "longtext", nullable: true),
                    InvestmentUniqueNumber = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ValuationDate = table.Column<DateTime>(type: "date", nullable: true),
                    OpenedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyOpenedByUserId = table.Column<int>(type: "int", nullable: true),
                    LegacyUpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    LegacyOpenedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LegacyUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientFundValuations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientFundValuations_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientFundValuations_ClientId",
                table: "ClientFundValuations",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientFundValuations_InvestmentUniqueNumber",
                table: "ClientFundValuations",
                column: "InvestmentUniqueNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ClientFundValuations_KanaanId",
                table: "ClientFundValuations",
                column: "KanaanId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientFundValuations_LegacyClientId",
                table: "ClientFundValuations",
                column: "LegacyClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientFundValuations_LegacyFundId",
                table: "ClientFundValuations",
                column: "LegacyFundId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientFundValuations_ValuationDate",
                table: "ClientFundValuations",
                column: "ValuationDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientFundValuations");
        }
    }
}
