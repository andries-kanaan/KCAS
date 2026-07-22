using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIncrementalLegacyReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LegacyReconciliationStatus",
                table: "Clients",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unscanned");

            migrationBuilder.CreateTable(
                name: "LegacyImportRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Mode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    SourceLabel = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    NewCount = table.Column<int>(type: "int", nullable: false),
                    UnchangedCount = table.Column<int>(type: "int", nullable: false),
                    ChangedCount = table.Column<int>(type: "int", nullable: false),
                    MissingCount = table.Column<int>(type: "int", nullable: false),
                    InvalidCount = table.Column<int>(type: "int", nullable: false),
                    OrphanedCount = table.Column<int>(type: "int", nullable: false),
                    AppliedCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    ErrorSummary = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyImportRuns", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LegacySourceSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    SourceTable = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    Fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AcceptedFromRunId = table.Column<long>(type: "bigint", nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacySourceSnapshots", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LegacyImportRowStates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    LegacyImportRunId = table.Column<long>(type: "bigint", nullable: false),
                    SourceTable = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    Classification = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ApplyStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    TargetEntityType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    TargetEntityId = table.Column<long>(type: "bigint", nullable: true),
                    IncomingFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    BaselineFingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    IncomingPayloadJson = table.Column<string>(type: "longtext", nullable: false),
                    BaselinePayloadJson = table.Column<string>(type: "longtext", nullable: true),
                    SourceUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Error = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyImportRowStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegacyImportRowStates_LegacyImportRuns_LegacyImportRunId",
                        column: x => x.LegacyImportRunId,
                        principalTable: "LegacyImportRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "LegacyImportDifferences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    LegacyImportRowStateId = table.Column<long>(type: "bigint", nullable: false),
                    FieldName = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    BaselineValue = table.Column<string>(type: "longtext", nullable: true),
                    IncomingValue = table.Column<string>(type: "longtext", nullable: true),
                    Decision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ResolvedValue = table.Column<string>(type: "longtext", nullable: true),
                    ReviewedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReviewReason = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegacyImportDifferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegacyImportDifferences_LegacyImportRowStates_LegacyImportRo~",
                        column: x => x.LegacyImportRowStateId,
                        principalTable: "LegacyImportRowStates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportDifferences_Decision",
                table: "LegacyImportDifferences",
                column: "Decision");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportDifferences_LegacyImportRowStateId_FieldName",
                table: "LegacyImportDifferences",
                columns: new[] { "LegacyImportRowStateId", "FieldName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportRowStates_LegacyImportRunId_Classification",
                table: "LegacyImportRowStates",
                columns: new[] { "LegacyImportRunId", "Classification" });

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportRowStates_LegacyImportRunId_SourceTable_SourceId",
                table: "LegacyImportRowStates",
                columns: new[] { "LegacyImportRunId", "SourceTable", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportRuns_StartedAtUtc",
                table: "LegacyImportRuns",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportRuns_Status",
                table: "LegacyImportRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_LegacySourceSnapshots_LastSeenAtUtc",
                table: "LegacySourceSnapshots",
                column: "LastSeenAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LegacySourceSnapshots_SourceTable_SourceId",
                table: "LegacySourceSnapshots",
                columns: new[] { "SourceTable", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegacyImportDifferences");

            migrationBuilder.DropTable(
                name: "LegacySourceSnapshots");

            migrationBuilder.DropTable(
                name: "LegacyImportRowStates");

            migrationBuilder.DropTable(
                name: "LegacyImportRuns");

            migrationBuilder.DropColumn(
                name: "LegacyReconciliationStatus",
                table: "Clients");
        }
    }
}
