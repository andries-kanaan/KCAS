using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompleteOutstandingWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClientKycPolicies_LegacyKycId",
                table: "ClientKycPolicies");

            migrationBuilder.AlterColumn<int>(
                name: "LegacyInvestmentHistoryId",
                table: "ClientInvestmentTransactions",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "LegacyInvestmentAccountId",
                table: "ClientInvestmentAccounts",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.CreateTable(
                name: "ClientKycRecommendations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    ClientKycPolicyId = table.Column<int>(type: "int", nullable: true),
                    LegacyRecommendationId = table.Column<int>(type: "int", nullable: true),
                    LegacyClientId = table.Column<int>(type: "int", nullable: true),
                    KanaanId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    RecommendationType = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Status = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    RecommendationDate = table.Column<DateTime>(type: "date", nullable: true),
                    Details = table.Column<string>(type: "longtext", nullable: true),
                    Outcome = table.Column<string>(type: "longtext", nullable: true),
                    OpenedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientKycRecommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientKycRecommendations_ClientKycPolicies_ClientKycPolicyId",
                        column: x => x.ClientKycPolicyId,
                        principalTable: "ClientKycPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ClientKycRecommendations_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientKycPolicies_LegacyKycId",
                table: "ClientKycPolicies",
                column: "LegacyKycId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientKycRecommendations_ClientId",
                table: "ClientKycRecommendations",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientKycRecommendations_ClientKycPolicyId",
                table: "ClientKycRecommendations",
                column: "ClientKycPolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientKycRecommendations_KanaanId",
                table: "ClientKycRecommendations",
                column: "KanaanId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientKycRecommendations_LegacyRecommendationId",
                table: "ClientKycRecommendations",
                column: "LegacyRecommendationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientKycRecommendations_Status",
                table: "ClientKycRecommendations",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientKycRecommendations");

            migrationBuilder.DropIndex(
                name: "IX_ClientKycPolicies_LegacyKycId",
                table: "ClientKycPolicies");

            migrationBuilder.AlterColumn<int>(
                name: "LegacyInvestmentHistoryId",
                table: "ClientInvestmentTransactions",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "LegacyInvestmentAccountId",
                table: "ClientInvestmentAccounts",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientKycPolicies_LegacyKycId",
                table: "ClientKycPolicies",
                column: "LegacyKycId",
                unique: true);
        }
    }
}
