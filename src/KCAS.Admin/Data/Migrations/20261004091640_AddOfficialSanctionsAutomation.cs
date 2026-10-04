using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOfficialSanctionsAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SanctionsSourceSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    SourceUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    ContentSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Payload = table.Column<byte[]>(type: "longblob", nullable: false),
                    RetrievedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Individuals = table.Column<int>(type: "int", nullable: false),
                    Entities = table.Column<int>(type: "int", nullable: false),
                    ClientSanctionsBatchId = table.Column<int>(type: "int", nullable: false),
                    EmployeeTfsBatchId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SanctionsSourceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SanctionsSourceSnapshots_ClientSanctionsBatches_ClientSancti~",
                        column: x => x.ClientSanctionsBatchId,
                        principalTable: "ClientSanctionsBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SanctionsSourceSnapshots_EmployeeTfsBatches_EmployeeTfsBatch~",
                        column: x => x.EmployeeTfsBatchId,
                        principalTable: "EmployeeTfsBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SanctionsAutomatedResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    SanctionsSourceSnapshotId = table.Column<int>(type: "int", nullable: false),
                    ClientSanctionsSubjectId = table.Column<int>(type: "int", nullable: true),
                    EmployeeProfileId = table.Column<int>(type: "int", nullable: true),
                    ScopeHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ScopeJson = table.Column<string>(type: "longtext", nullable: false),
                    Outcome = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CandidatesJson = table.Column<string>(type: "longtext", nullable: false),
                    Finding = table.Column<string>(type: "longtext", nullable: false),
                    PerformedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ClientEvidenceItemId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SanctionsAutomatedResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SanctionsAutomatedResults_ClientEvidenceItems_ClientEvidence~",
                        column: x => x.ClientEvidenceItemId,
                        principalTable: "ClientEvidenceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SanctionsAutomatedResults_ClientSanctionsSubjects_ClientSanc~",
                        column: x => x.ClientSanctionsSubjectId,
                        principalTable: "ClientSanctionsSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SanctionsAutomatedResults_EmployeeProfiles_EmployeeProfileId",
                        column: x => x.EmployeeProfileId,
                        principalTable: "EmployeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SanctionsAutomatedResults_SanctionsSourceSnapshots_Sanctions~",
                        column: x => x.SanctionsSourceSnapshotId,
                        principalTable: "SanctionsSourceSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SanctionsSourceChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SourceUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    Outcome = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    SanctionsSourceSnapshotId = table.Column<int>(type: "int", nullable: true),
                    Detail = table.Column<string>(type: "longtext", nullable: false),
                    SubjectsChecked = table.Column<int>(type: "int", nullable: false),
                    NoCandidates = table.Column<int>(type: "int", nullable: false),
                    NeedsReview = table.Column<int>(type: "int", nullable: false),
                    MaximumSourceAgeHours = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SanctionsSourceChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SanctionsSourceChecks_SanctionsSourceSnapshots_SanctionsSour~",
                        column: x => x.SanctionsSourceSnapshotId,
                        principalTable: "SanctionsSourceSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SanctionsAutomatedResults_ClientEvidenceItemId",
                table: "SanctionsAutomatedResults",
                column: "ClientEvidenceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SanctionsAutomatedResults_ClientSanctionsSubjectId_Performed~",
                table: "SanctionsAutomatedResults",
                columns: new[] { "ClientSanctionsSubjectId", "PerformedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SanctionsAutomatedResults_EmployeeProfileId",
                table: "SanctionsAutomatedResults",
                column: "EmployeeProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_SanctionsAutomatedResults_SanctionsSourceSnapshotId_Employee~",
                table: "SanctionsAutomatedResults",
                columns: new[] { "SanctionsSourceSnapshotId", "EmployeeProfileId", "PerformedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SanctionsSourceChecks_CompletedAtUtc",
                table: "SanctionsSourceChecks",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SanctionsSourceChecks_SanctionsSourceSnapshotId",
                table: "SanctionsSourceChecks",
                column: "SanctionsSourceSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_SanctionsSourceSnapshots_ClientSanctionsBatchId",
                table: "SanctionsSourceSnapshots",
                column: "ClientSanctionsBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SanctionsSourceSnapshots_ContentSha256",
                table: "SanctionsSourceSnapshots",
                column: "ContentSha256");

            migrationBuilder.CreateIndex(
                name: "IX_SanctionsSourceSnapshots_EmployeeTfsBatchId",
                table: "SanctionsSourceSnapshots",
                column: "EmployeeTfsBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SanctionsAutomatedResults");

            migrationBuilder.DropTable(
                name: "SanctionsSourceChecks");

            migrationBuilder.DropTable(
                name: "SanctionsSourceSnapshots");
        }
    }
}
