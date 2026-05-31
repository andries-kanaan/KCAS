using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientKycPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientKycPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    LegacyKycId = table.Column<int>(type: "int", nullable: false),
                    LegacyClientId = table.Column<int>(type: "int", nullable: true),
                    KanaanId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyMainClassId = table.Column<int>(type: "int", nullable: true),
                    MainClassName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacySubClassId = table.Column<int>(type: "int", nullable: true),
                    SubClassName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    SubClassExtra = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Administrator = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Product = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    PolicyNumber = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    Fund = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    LifeCover = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DisabilityCover = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    DreadDiseaseCover = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CompulsoryContributionValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    VoluntaryContributionValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Debt = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MonthlyPremium = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OnceOffPremium = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MonthlyIncome = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CapitalAdequacyRatioPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    TaxPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    IncludeInCalculations = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SurrenderOrLiquidate = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsRetirementAnnuity = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsPreservationFund = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsRetrenchmentPackage = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsQuote = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ValuationDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
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
                    table.PrimaryKey("PK_ClientKycPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientKycPolicies_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientKycPolicies_ClientId",
                table: "ClientKycPolicies",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientKycPolicies_IncludeInCalculations_IsQuote",
                table: "ClientKycPolicies",
                columns: new[] { "IncludeInCalculations", "IsQuote" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientKycPolicies_LegacyKycId",
                table: "ClientKycPolicies",
                column: "LegacyKycId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientKycPolicies_LegacyMainClassId_LegacySubClassId",
                table: "ClientKycPolicies",
                columns: new[] { "LegacyMainClassId", "LegacySubClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientKycPolicies_PolicyNumber",
                table: "ClientKycPolicies",
                column: "PolicyNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientKycPolicies");
        }
    }
}
