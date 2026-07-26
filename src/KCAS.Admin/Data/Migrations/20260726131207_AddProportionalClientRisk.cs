using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProportionalClientRisk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReviewMonths",
                table: "RiskBands",
                type: "int",
                nullable: false,
                defaultValue: 36);

            migrationBuilder.CreateTable(
                name: "ClientRiskAssessments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    RiskMethodologyVersionId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CalculatedScore = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    CalculatedRating = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: true),
                    FinalRating = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: true),
                    IsOverride = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    OverrideReason = table.Column<string>(type: "longtext", nullable: true),
                    HasPepExposure = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    HasSanctionsConcern = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    HasAdverseInformation = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RequiresEdd = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StandardControlsApplied = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Narrative = table.Column<string>(type: "longtext", nullable: true),
                    EffectiveDate = table.Column<DateTime>(type: "date", nullable: true),
                    NextReviewDate = table.Column<DateTime>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FinalisedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PreparedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    FinalisedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    SnapshotJson = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientRiskAssessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientRiskAssessments_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientRiskAssessments_RiskMethodologyVersions_RiskMethodolog~",
                        column: x => x.RiskMethodologyVersionId,
                        principalTable: "RiskMethodologyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientRiskAssessmentApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientRiskAssessmentId = table.Column<int>(type: "int", nullable: false),
                    Approver = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Decision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientRiskAssessmentApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientRiskAssessmentApprovals_ClientRiskAssessments_ClientRi~",
                        column: x => x.ClientRiskAssessmentId,
                        principalTable: "ClientRiskAssessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientRiskAssessmentResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientRiskAssessmentId = table.Column<int>(type: "int", nullable: false),
                    RiskFactorDefinitionId = table.Column<int>(type: "int", nullable: false),
                    RiskFactorOptionId = table.Column<int>(type: "int", nullable: true),
                    ClientEvidenceItemId = table.Column<int>(type: "int", nullable: true),
                    Score = table.Column<int>(type: "int", nullable: false),
                    WeightedScore = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    Explanation = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientRiskAssessmentResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientRiskAssessmentResponses_ClientEvidenceItems_ClientEvid~",
                        column: x => x.ClientEvidenceItemId,
                        principalTable: "ClientEvidenceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ClientRiskAssessmentResponses_ClientRiskAssessments_ClientRi~",
                        column: x => x.ClientRiskAssessmentId,
                        principalTable: "ClientRiskAssessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientRiskAssessmentResponses_RiskFactorDefinitions_RiskFact~",
                        column: x => x.RiskFactorDefinitionId,
                        principalTable: "RiskFactorDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientRiskAssessmentResponses_RiskFactorOptions_RiskFactorOp~",
                        column: x => x.RiskFactorOptionId,
                        principalTable: "RiskFactorOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRiskAssessmentApprovals_ClientRiskAssessmentId_Approver",
                table: "ClientRiskAssessmentApprovals",
                columns: new[] { "ClientRiskAssessmentId", "Approver" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientRiskAssessmentResponses_ClientEvidenceItemId",
                table: "ClientRiskAssessmentResponses",
                column: "ClientEvidenceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRiskAssessmentResponses_ClientRiskAssessmentId_RiskFac~",
                table: "ClientRiskAssessmentResponses",
                columns: new[] { "ClientRiskAssessmentId", "RiskFactorDefinitionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientRiskAssessmentResponses_RiskFactorDefinitionId",
                table: "ClientRiskAssessmentResponses",
                column: "RiskFactorDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRiskAssessmentResponses_RiskFactorOptionId",
                table: "ClientRiskAssessmentResponses",
                column: "RiskFactorOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRiskAssessments_ClientId_Status",
                table: "ClientRiskAssessments",
                columns: new[] { "ClientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientRiskAssessments_FinalRating_Status",
                table: "ClientRiskAssessments",
                columns: new[] { "FinalRating", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientRiskAssessments_NextReviewDate",
                table: "ClientRiskAssessments",
                column: "NextReviewDate");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRiskAssessments_RiskMethodologyVersionId",
                table: "ClientRiskAssessments",
                column: "RiskMethodologyVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientRiskAssessmentApprovals");

            migrationBuilder.DropTable(
                name: "ClientRiskAssessmentResponses");

            migrationBuilder.DropTable(
                name: "ClientRiskAssessments");

            migrationBuilder.DropColumn(
                name: "ReviewMonths",
                table: "RiskBands");
        }
    }
}
