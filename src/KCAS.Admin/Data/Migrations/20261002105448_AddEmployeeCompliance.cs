using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    DisplayName = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    LegalName = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Aliases = table.Column<string>(type: "longtext", nullable: false),
                    Email = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    EmploymentStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    IdentityReference = table.Column<string>(type: "longtext", nullable: false),
                    Responsibilities = table.Column<string>(type: "longtext", nullable: false),
                    AuthorityLimits = table.Column<string>(type: "longtext", nullable: false),
                    SourceReference = table.Column<string>(type: "longtext", nullable: false),
                    RoleExposure = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    RiskRationale = table.Column<string>(type: "longtext", nullable: false),
                    SelectedChecks = table.Column<string>(type: "longtext", nullable: false),
                    RequireTraining = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RequireRegulatedCompetence = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RequireAdditionalCheck = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ProposedReviewMonths = table.Column<int>(type: "int", nullable: false),
                    EmploymentStart = table.Column<DateTime>(type: "date", nullable: true),
                    EmploymentEnd = table.Column<DateTime>(type: "date", nullable: true),
                    ExternalAccessScope = table.Column<string>(type: "longtext", nullable: false),
                    Version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeProfiles", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeTfsBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    SourceVersion = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    SourceUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeTfsBatches", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeAccountLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EmployeeProfileId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    VerificationReference = table.Column<string>(type: "longtext", nullable: false),
                    LinkedByUserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeAccountLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeAccountLinks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeAccountLinks_EmployeeProfiles_EmployeeProfileId",
                        column: x => x.EmployeeProfileId,
                        principalTable: "EmployeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeComplianceAuditEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EmployeeProfileId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false),
                    SnapshotJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeComplianceAuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeComplianceAuditEvents_EmployeeProfiles_EmployeeProfi~",
                        column: x => x.EmployeeProfileId,
                        principalTable: "EmployeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeComplianceReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EmployeeProfileId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ProfileVersion = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ProfileSnapshotJson = table.Column<string>(type: "longtext", nullable: false),
                    PreparedByUserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    NextReviewDate = table.Column<DateTime>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeComplianceReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeComplianceReviews_EmployeeProfiles_EmployeeProfileId",
                        column: x => x.EmployeeProfileId,
                        principalTable: "EmployeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeEvidenceDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EmployeeProfileId = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Category = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                    EvidencePath = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    EvidenceSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SourceNote = table.Column<string>(type: "longtext", nullable: false),
                    LinkedByUserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeEvidenceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeEvidenceDocuments_EmployeeProfiles_EmployeeProfileId",
                        column: x => x.EmployeeProfileId,
                        principalTable: "EmployeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeComplianceTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EmployeeProfileId = table.Column<int>(type: "int", nullable: false),
                    TriggerKey = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Kind = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false),
                    DueDate = table.Column<DateTime>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RecipientUserIdsJson = table.Column<string>(type: "longtext", nullable: false),
                    AcknowledgedByUserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    AcknowledgedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EmployeeTfsBatchId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeComplianceTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeComplianceTasks_EmployeeProfiles_EmployeeProfileId",
                        column: x => x.EmployeeProfileId,
                        principalTable: "EmployeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeComplianceTasks_EmployeeTfsBatches_EmployeeTfsBatchId",
                        column: x => x.EmployeeTfsBatchId,
                        principalTable: "EmployeeTfsBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeAccessConfirmations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EmployeeComplianceReviewId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    SystemAndScope = table.Column<string>(type: "longtext", nullable: false),
                    ApprovedScope = table.Column<string>(type: "longtext", nullable: false),
                    ActualScope = table.Column<string>(type: "longtext", nullable: false),
                    ActionConfirmation = table.Column<string>(type: "longtext", nullable: false),
                    IsAligned = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    VerifiedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    EvidenceReference = table.Column<string>(type: "longtext", nullable: false),
                    AccountScopeJson = table.Column<string>(type: "longtext", nullable: false),
                    RecordedByUserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeAccessConfirmations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeAccessConfirmations_EmployeeComplianceReviews_Employ~",
                        column: x => x.EmployeeComplianceReviewId,
                        principalTable: "EmployeeComplianceReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeComplianceChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EmployeeComplianceReviewId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                    Outcome = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    PerformerType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Performer = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    PerformedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SourceReference = table.Column<string>(type: "longtext", nullable: false),
                    SourceUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                    ListVersion = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    IdentifierScope = table.Column<string>(type: "longtext", nullable: false),
                    Finding = table.Column<string>(type: "longtext", nullable: false),
                    Limitations = table.Column<string>(type: "longtext", nullable: false),
                    EvidencePath = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                    EvidenceSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    SupersedesCheckId = table.Column<int>(type: "int", nullable: true),
                    EmployeeTfsBatchId = table.Column<int>(type: "int", nullable: true),
                    RecordedByUserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeComplianceChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeComplianceChecks_EmployeeComplianceChecks_Supersedes~",
                        column: x => x.SupersedesCheckId,
                        principalTable: "EmployeeComplianceChecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeComplianceChecks_EmployeeComplianceReviews_EmployeeC~",
                        column: x => x.EmployeeComplianceReviewId,
                        principalTable: "EmployeeComplianceReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeComplianceChecks_EmployeeTfsBatches_EmployeeTfsBatch~",
                        column: x => x.EmployeeTfsBatchId,
                        principalTable: "EmployeeTfsBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EmployeeReviewDecisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EmployeeComplianceReviewId = table.Column<int>(type: "int", nullable: false),
                    Decision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false),
                    RestrictionsAndFollowUp = table.Column<string>(type: "longtext", nullable: false),
                    ApprovedReviewMonths = table.Column<int>(type: "int", nullable: true),
                    ReviewerUserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ReviewerName = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    ReviewerEmployeeProfileId = table.Column<int>(type: "int", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    EvidenceSnapshotJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeReviewDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeReviewDecisions_EmployeeComplianceReviews_EmployeeCo~",
                        column: x => x.EmployeeComplianceReviewId,
                        principalTable: "EmployeeComplianceReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EmployeeReviewDecisions_EmployeeProfiles_ReviewerEmployeePro~",
                        column: x => x.ReviewerEmployeeProfileId,
                        principalTable: "EmployeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAccessConfirmations_EmployeeComplianceReviewId",
                table: "EmployeeAccessConfirmations",
                column: "EmployeeComplianceReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAccountLinks_EmployeeProfileId",
                table: "EmployeeAccountLinks",
                column: "EmployeeProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAccountLinks_UserId",
                table: "EmployeeAccountLinks",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeComplianceAuditEvents_EmployeeProfileId_TimestampUtc",
                table: "EmployeeComplianceAuditEvents",
                columns: new[] { "EmployeeProfileId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeComplianceChecks_EmployeeComplianceReviewId_Kind",
                table: "EmployeeComplianceChecks",
                columns: new[] { "EmployeeComplianceReviewId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeComplianceChecks_EmployeeTfsBatchId",
                table: "EmployeeComplianceChecks",
                column: "EmployeeTfsBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeComplianceChecks_SupersedesCheckId",
                table: "EmployeeComplianceChecks",
                column: "SupersedesCheckId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeComplianceReviews_EmployeeProfileId_Status",
                table: "EmployeeComplianceReviews",
                columns: new[] { "EmployeeProfileId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeComplianceTasks_EmployeeProfileId_TriggerKey",
                table: "EmployeeComplianceTasks",
                columns: new[] { "EmployeeProfileId", "TriggerKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeComplianceTasks_EmployeeTfsBatchId",
                table: "EmployeeComplianceTasks",
                column: "EmployeeTfsBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeEvidenceDocuments_EmployeeProfileId_EvidenceSha256",
                table: "EmployeeEvidenceDocuments",
                columns: new[] { "EmployeeProfileId", "EvidenceSha256" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeProfiles_EmploymentStatus_DisplayName",
                table: "EmployeeProfiles",
                columns: new[] { "EmploymentStatus", "DisplayName" });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeReviewDecisions_EmployeeComplianceReviewId",
                table: "EmployeeReviewDecisions",
                column: "EmployeeComplianceReviewId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeReviewDecisions_ReviewerEmployeeProfileId",
                table: "EmployeeReviewDecisions",
                column: "ReviewerEmployeeProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTfsBatches_SourceVersion",
                table: "EmployeeTfsBatches",
                column: "SourceVersion",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeAccessConfirmations");

            migrationBuilder.DropTable(
                name: "EmployeeAccountLinks");

            migrationBuilder.DropTable(
                name: "EmployeeComplianceAuditEvents");

            migrationBuilder.DropTable(
                name: "EmployeeComplianceChecks");

            migrationBuilder.DropTable(
                name: "EmployeeComplianceTasks");

            migrationBuilder.DropTable(
                name: "EmployeeEvidenceDocuments");

            migrationBuilder.DropTable(
                name: "EmployeeReviewDecisions");

            migrationBuilder.DropTable(
                name: "EmployeeTfsBatches");

            migrationBuilder.DropTable(
                name: "EmployeeComplianceReviews");

            migrationBuilder.DropTable(
                name: "EmployeeProfiles");
        }
    }
}
