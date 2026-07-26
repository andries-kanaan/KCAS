using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRmcpControlFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RmcpVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    BusinessRiskAssessmentId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    VersionReference = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Scope = table.Column<string>(type: "longtext", nullable: false),
                    Owner = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    ReviewMonths = table.Column<int>(type: "int", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "date", nullable: true),
                    NextReviewDate = table.Column<DateTime>(type: "date", nullable: true),
                    SignedDocumentLocation = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    ApprovalResolutionLocation = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    ChangeSummary = table.Column<string>(type: "longtext", nullable: false),
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
                    table.PrimaryKey("PK_RmcpVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RmcpVersions_BusinessRiskAssessments_BusinessRiskAssessmentId",
                        column: x => x.BusinessRiskAssessmentId,
                        principalTable: "BusinessRiskAssessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RmcpControls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    RmcpVersionId = table.Column<int>(type: "int", nullable: false),
                    BusinessRiskItemId = table.Column<int>(type: "int", nullable: true),
                    Domain = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                    Code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    ProcedureSummary = table.Column<string>(type: "longtext", nullable: false),
                    Owner = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Frequency = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    EvidenceExpectation = table.Column<string>(type: "longtext", nullable: false),
                    MonitoringMethod = table.Column<string>(type: "longtext", nullable: false),
                    EscalationProcedure = table.Column<string>(type: "longtext", nullable: false),
                    HasGap = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    GapDescription = table.Column<string>(type: "longtext", nullable: true),
                    TreatmentOwner = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    TreatmentDueDate = table.Column<DateTime>(type: "date", nullable: true),
                    ComplianceTaskId = table.Column<int>(type: "int", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RmcpControls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RmcpControls_BusinessRiskItems_BusinessRiskItemId",
                        column: x => x.BusinessRiskItemId,
                        principalTable: "BusinessRiskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RmcpControls_ComplianceTasks_ComplianceTaskId",
                        column: x => x.ComplianceTaskId,
                        principalTable: "ComplianceTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RmcpControls_RmcpVersions_RmcpVersionId",
                        column: x => x.RmcpVersionId,
                        principalTable: "RmcpVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RmcpControls_BusinessRiskItemId",
                table: "RmcpControls",
                column: "BusinessRiskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RmcpControls_ComplianceTaskId",
                table: "RmcpControls",
                column: "ComplianceTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_RmcpControls_RmcpVersionId_Code",
                table: "RmcpControls",
                columns: new[] { "RmcpVersionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RmcpVersions_BusinessRiskAssessmentId",
                table: "RmcpVersions",
                column: "BusinessRiskAssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RmcpVersions_Status_VersionReference",
                table: "RmcpVersions",
                columns: new[] { "Status", "VersionReference" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RmcpControls");

            migrationBuilder.DropTable(
                name: "RmcpVersions");
        }
    }
}
