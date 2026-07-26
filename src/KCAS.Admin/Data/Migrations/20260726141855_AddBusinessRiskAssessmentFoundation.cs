using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessRiskAssessmentFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BusinessRiskAssessments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    AssessmentYear = table.Column<int>(type: "int", nullable: false),
                    AsAtDate = table.Column<DateTime>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Scope = table.Column<string>(type: "longtext", nullable: false),
                    MethodologyNarrative = table.Column<string>(type: "longtext", nullable: false),
                    ManagementJudgement = table.Column<string>(type: "longtext", nullable: false),
                    Limitations = table.Column<string>(type: "longtext", nullable: false),
                    RiskTolerance = table.Column<string>(type: "longtext", nullable: false),
                    PortfolioSnapshotJson = table.Column<string>(type: "longtext", nullable: true),
                    SnapshotJson = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PreparedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessRiskAssessments", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BusinessRiskApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    BusinessRiskAssessmentId = table.Column<int>(type: "int", nullable: false),
                    Approver = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessRiskApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessRiskApprovals_BusinessRiskAssessments_BusinessRiskAs~",
                        column: x => x.BusinessRiskAssessmentId,
                        principalTable: "BusinessRiskAssessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "BusinessRiskItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    BusinessRiskAssessmentId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                    RiskStatement = table.Column<string>(type: "longtext", nullable: false),
                    EvidenceAndRationale = table.Column<string>(type: "longtext", nullable: false),
                    Likelihood = table.Column<int>(type: "int", nullable: false),
                    Impact = table.Column<int>(type: "int", nullable: false),
                    InherentScore = table.Column<int>(type: "int", nullable: false),
                    InherentRating = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    KeyControls = table.Column<string>(type: "longtext", nullable: false),
                    ControlEffectiveness = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ResidualRating = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ResidualRationale = table.Column<string>(type: "longtext", nullable: false),
                    TreatmentDecision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Owner = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    DueDate = table.Column<DateTime>(type: "date", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessRiskItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessRiskItems_BusinessRiskAssessments_BusinessRiskAssess~",
                        column: x => x.BusinessRiskAssessmentId,
                        principalTable: "BusinessRiskAssessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessRiskApprovals_BusinessRiskAssessmentId_Approver",
                table: "BusinessRiskApprovals",
                columns: new[] { "BusinessRiskAssessmentId", "Approver" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BusinessRiskAssessments_AssessmentYear_Status",
                table: "BusinessRiskAssessments",
                columns: new[] { "AssessmentYear", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessRiskItems_BusinessRiskAssessmentId_Category",
                table: "BusinessRiskItems",
                columns: new[] { "BusinessRiskAssessmentId", "Category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BusinessRiskApprovals");

            migrationBuilder.DropTable(
                name: "BusinessRiskItems");

            migrationBuilder.DropTable(
                name: "BusinessRiskAssessments");
        }
    }
}
