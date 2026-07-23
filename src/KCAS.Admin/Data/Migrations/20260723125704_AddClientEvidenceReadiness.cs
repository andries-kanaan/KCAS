using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientEvidenceReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientEvidenceRequirements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientCategory = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    RequirementGroup = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    EvidenceType = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    Title = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    IsBlocking = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RequiresVerification = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RequiresExpiryDate = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEvidenceRequirements", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientEvidenceScanRoots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    RootPath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEvidenceScanRoots", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientEvidenceScanRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    RootPath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    TotalFiles = table.Column<int>(type: "int", nullable: false),
                    LinkedFiles = table.Column<int>(type: "int", nullable: false),
                    UnmatchedFiles = table.Column<int>(type: "int", nullable: false),
                    AmbiguousFiles = table.Column<int>(type: "int", nullable: false),
                    SkippedFiles = table.Column<int>(type: "int", nullable: false),
                    ErrorMessage = table.Column<string>(type: "longtext", nullable: true),
                    StartedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEvidenceScanRuns", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientEvidenceExceptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    ClientEvidenceRequirementId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false),
                    ApprovedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReviewDate = table.Column<DateTime>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEvidenceExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientEvidenceExceptions_ClientEvidenceRequirements_ClientEv~",
                        column: x => x.ClientEvidenceRequirementId,
                        principalTable: "ClientEvidenceRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientEvidenceExceptions_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientEvidenceScanFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientEvidenceScanRunId = table.Column<int>(type: "int", nullable: false),
                    ClientId = table.Column<int>(type: "int", nullable: true),
                    FullPath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    RelativePath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    FileName = table.Column<string>(type: "varchar(260)", maxLength: 260, nullable: false),
                    FileSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    FileLastWriteTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    MatchStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    SuggestedEvidenceType = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: true),
                    MatchReason = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    CandidateCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEvidenceScanFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientEvidenceScanFiles_ClientEvidenceScanRuns_ClientEvidenc~",
                        column: x => x.ClientEvidenceScanRunId,
                        principalTable: "ClientEvidenceScanRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientEvidenceScanFiles_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientEvidenceItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    ClientEvidenceRequirementId = table.Column<int>(type: "int", nullable: true),
                    EvidenceType = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    Title = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    SourcePath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    RelativePath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    FileName = table.Column<string>(type: "varchar(260)", maxLength: 260, nullable: true),
                    FileSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    FileLastWriteTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReceivedDate = table.Column<DateTime>(type: "date", nullable: true),
                    VerifiedDate = table.Column<DateTime>(type: "date", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "date", nullable: true),
                    Reviewer = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Notes = table.Column<string>(type: "longtext", nullable: true),
                    ClientEvidenceScanFileId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEvidenceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientEvidenceItems_ClientEvidenceRequirements_ClientEvidenc~",
                        column: x => x.ClientEvidenceRequirementId,
                        principalTable: "ClientEvidenceRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ClientEvidenceItems_ClientEvidenceScanFiles_ClientEvidenceSc~",
                        column: x => x.ClientEvidenceScanFileId,
                        principalTable: "ClientEvidenceScanFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ClientEvidenceItems_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceExceptions_ClientEvidenceRequirementId",
                table: "ClientEvidenceExceptions",
                column: "ClientEvidenceRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceExceptions_ClientId_ClientEvidenceRequirementI~",
                table: "ClientEvidenceExceptions",
                columns: new[] { "ClientId", "ClientEvidenceRequirementId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceExceptions_ReviewDate",
                table: "ClientEvidenceExceptions",
                column: "ReviewDate");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceItems_ClientEvidenceRequirementId",
                table: "ClientEvidenceItems",
                column: "ClientEvidenceRequirementId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceItems_ClientEvidenceScanFileId",
                table: "ClientEvidenceItems",
                column: "ClientEvidenceScanFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceItems_ClientId_EvidenceType_Status",
                table: "ClientEvidenceItems",
                columns: new[] { "ClientId", "EvidenceType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceItems_ExpiryDate",
                table: "ClientEvidenceItems",
                column: "ExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceItems_FileSha256",
                table: "ClientEvidenceItems",
                column: "FileSha256");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceRequirements_ClientCategory_EvidenceType_Status",
                table: "ClientEvidenceRequirements",
                columns: new[] { "ClientCategory", "EvidenceType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceRequirements_RequirementGroup_SortOrder",
                table: "ClientEvidenceRequirements",
                columns: new[] { "RequirementGroup", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceScanFiles_ClientEvidenceScanRunId_MatchStatus",
                table: "ClientEvidenceScanFiles",
                columns: new[] { "ClientEvidenceScanRunId", "MatchStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceScanFiles_ClientId",
                table: "ClientEvidenceScanFiles",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceScanFiles_FileSha256",
                table: "ClientEvidenceScanFiles",
                column: "FileSha256");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceScanRoots_IsActive",
                table: "ClientEvidenceScanRoots",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceScanRuns_StartedAtUtc",
                table: "ClientEvidenceScanRuns",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceScanRuns_Status",
                table: "ClientEvidenceScanRuns",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientEvidenceExceptions");

            migrationBuilder.DropTable(
                name: "ClientEvidenceItems");

            migrationBuilder.DropTable(
                name: "ClientEvidenceScanRoots");

            migrationBuilder.DropTable(
                name: "ClientEvidenceRequirements");

            migrationBuilder.DropTable(
                name: "ClientEvidenceScanFiles");

            migrationBuilder.DropTable(
                name: "ClientEvidenceScanRuns");
        }
    }
}
