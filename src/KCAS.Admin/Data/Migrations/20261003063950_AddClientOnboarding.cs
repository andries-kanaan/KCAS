using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresClientAcceptance",
                table: "Clients",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ClientAcceptanceDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    Decision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: "longtext", nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false),
                    DecidedByUserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DecidedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    GovernanceRoleAssignmentId = table.Column<int>(type: "int", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ImportSourceReference = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAcceptanceDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAcceptanceDecisions_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientCodexReviewRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    ComplianceTaskId = table.Column<int>(type: "int", nullable: false),
                    MaterialHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Brief = table.Column<string>(type: "longtext", nullable: false),
                    RecipientUserIdsJson = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedContentHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    CompletionSummary = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientCodexReviewRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientCodexReviewRequests_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientCodexReviewRequests_ComplianceTasks_ComplianceTaskId",
                        column: x => x.ComplianceTaskId,
                        principalTable: "ComplianceTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientOnboardingProfiles",
                columns: table => new
                {
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    RequestedService = table.Column<string>(type: "longtext", nullable: false),
                    ResponsibleRepresentative = table.Column<string>(type: "longtext", nullable: false),
                    PurposeAndProposedFunds = table.Column<string>(type: "longtext", nullable: false),
                    DisclosureVersion = table.Column<string>(type: "longtext", nullable: false),
                    DisclosureDeliveredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DisclosureDeliveryReference = table.Column<string>(type: "longtext", nullable: false),
                    EnhancedMeasures = table.Column<string>(type: "longtext", nullable: false),
                    RelationshipCommencedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RelationshipAuthorityReference = table.Column<string>(type: "longtext", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientOnboardingProfiles", x => x.ClientId);
                    table.ForeignKey(
                        name: "FK_ClientOnboardingProfiles_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAcceptanceDecisions_ClientId_DecidedAtUtc",
                table: "ClientAcceptanceDecisions",
                columns: new[] { "ClientId", "DecidedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientCodexReviewRequests_ClientId_Status",
                table: "ClientCodexReviewRequests",
                columns: new[] { "ClientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientCodexReviewRequests_ComplianceTaskId",
                table: "ClientCodexReviewRequests",
                column: "ComplianceTaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientAcceptanceDecisions");

            migrationBuilder.DropTable(
                name: "ClientCodexReviewRequests");

            migrationBuilder.DropTable(
                name: "ClientOnboardingProfiles");

            migrationBuilder.DropColumn(
                name: "RequiresClientAcceptance",
                table: "Clients");
        }
    }
}
