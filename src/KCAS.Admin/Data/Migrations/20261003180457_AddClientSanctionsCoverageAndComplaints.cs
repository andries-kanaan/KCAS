using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientSanctionsCoverageAndComplaints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientSanctionsBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    SourceVersion = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    SourceUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    SourcePublishedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Version = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    RecipientUserIdsJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientSanctionsBatches", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ComplaintCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: true),
                    ComplainantName = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    ContactDetails = table.Column<string>(type: "longtext", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Channel = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                    Allegation = table.Column<string>(type: "longtext", nullable: false),
                    RequestedOutcome = table.Column<string>(type: "longtext", nullable: false),
                    Category = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    SecondaryThemes = table.Column<string>(type: "longtext", nullable: false),
                    HandlerUserId = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    ImplicatedUserId = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    IsReportable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Decision = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: true),
                    DecisionReasons = table.Column<string>(type: "longtext", nullable: true),
                    Remedy = table.Column<string>(type: "longtext", nullable: true),
                    CompensationAwarded = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GoodwillAwarded = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DecidedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    NextUpdateDate = table.Column<DateTime>(type: "date", nullable: true),
                    ComplianceTaskId = table.Column<int>(type: "int", nullable: true),
                    LegacyKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    LegacyRowHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    LegacySourceJson = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Version = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplaintCases_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComplaintCases_ComplianceTasks_ComplianceTaskId",
                        column: x => x.ComplianceTaskId,
                        principalTable: "ComplianceTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientSanctionsSubjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientSanctionsBatchId = table.Column<int>(type: "int", nullable: false),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    SubjectKey = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    ScopeHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SubjectType = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    SubjectName = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    ClientRelatedPartyId = table.Column<int>(type: "int", nullable: true),
                    IdentitySummary = table.Column<string>(type: "longtext", nullable: false),
                    IsCurrent = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ComplianceTaskId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientSanctionsSubjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientSanctionsSubjects_ClientSanctionsBatches_ClientSanctio~",
                        column: x => x.ClientSanctionsBatchId,
                        principalTable: "ClientSanctionsBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientSanctionsSubjects_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientSanctionsSubjects_ComplianceTasks_ComplianceTaskId",
                        column: x => x.ComplianceTaskId,
                        principalTable: "ComplianceTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ComplaintEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ComplaintCaseId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Details = table.Column<string>(type: "longtext", nullable: false),
                    EvidenceReference = table.Column<string>(type: "longtext", nullable: false),
                    PerformedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    RecourseDetails = table.Column<string>(type: "longtext", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RecordedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplaintEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplaintEvents_ComplaintCases_ComplaintCaseId",
                        column: x => x.ComplaintCaseId,
                        principalTable: "ComplaintCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientSanctionsCoverageRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientSanctionsSubjectId = table.Column<int>(type: "int", nullable: false),
                    ClientEvidenceItemId = table.Column<int>(type: "int", nullable: true),
                    EvidenceFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Outcome = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false),
                    ExclusionReference = table.Column<string>(type: "longtext", nullable: true),
                    RecordedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientSanctionsCoverageRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientSanctionsCoverageRecords_ClientEvidenceItems_ClientEvi~",
                        column: x => x.ClientEvidenceItemId,
                        principalTable: "ClientEvidenceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientSanctionsCoverageRecords_ClientSanctionsSubjects_Clien~",
                        column: x => x.ClientSanctionsSubjectId,
                        principalTable: "ClientSanctionsSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientSanctionsBatches_SourceVersion",
                table: "ClientSanctionsBatches",
                column: "SourceVersion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientSanctionsCoverageRecords_ClientEvidenceItemId",
                table: "ClientSanctionsCoverageRecords",
                column: "ClientEvidenceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientSanctionsCoverageRecords_ClientSanctionsSubjectId_Reco~",
                table: "ClientSanctionsCoverageRecords",
                columns: new[] { "ClientSanctionsSubjectId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientSanctionsSubjects_ClientId",
                table: "ClientSanctionsSubjects",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientSanctionsSubjects_ClientSanctionsBatchId_SubjectKey_Sc~",
                table: "ClientSanctionsSubjects",
                columns: new[] { "ClientSanctionsBatchId", "SubjectKey", "ScopeHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientSanctionsSubjects_ComplianceTaskId",
                table: "ClientSanctionsSubjects",
                column: "ComplianceTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintCases_ClientId",
                table: "ComplaintCases",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintCases_ComplianceTaskId",
                table: "ComplaintCases",
                column: "ComplianceTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintCases_LegacyKey",
                table: "ComplaintCases",
                column: "LegacyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintCases_Status_ReceivedAtUtc",
                table: "ComplaintCases",
                columns: new[] { "Status", "ReceivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplaintEvents_ComplaintCaseId_OccurredAtUtc",
                table: "ComplaintEvents",
                columns: new[] { "ComplaintCaseId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientSanctionsCoverageRecords");

            migrationBuilder.DropTable(
                name: "ComplaintEvents");

            migrationBuilder.DropTable(
                name: "ClientSanctionsSubjects");

            migrationBuilder.DropTable(
                name: "ComplaintCases");

            migrationBuilder.DropTable(
                name: "ClientSanctionsBatches");
        }
    }
}
