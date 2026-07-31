using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoAmlDailyChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoAmlDailyChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    CheckDate = table.Column<DateTime>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    StartedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    Notes = table.Column<string>(type: "longtext", nullable: true),
                    MessageReference = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    ActionOwner = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    ActionDueDate = table.Column<DateTime>(type: "date", nullable: true),
                    ComplianceTaskId = table.Column<int>(type: "int", nullable: true),
                    EvidenceFileName = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                    EvidencePath = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                    EvidenceContentType = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: true),
                    EvidenceSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    EvidenceSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoAmlDailyChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoAmlDailyChecks_ComplianceTasks_ComplianceTaskId",
                        column: x => x.ComplianceTaskId,
                        principalTable: "ComplianceTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GoAmlSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EvidenceRootPath = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    PortalUrl = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    TrackingStartDate = table.Column<DateTime>(type: "date", nullable: false),
                    DueHourLocal = table.Column<int>(type: "int", nullable: false),
                    BackupChecker = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoAmlSettings", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GoAmlDailyChecks_CheckDate",
                table: "GoAmlDailyChecks",
                column: "CheckDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoAmlDailyChecks_ComplianceTaskId",
                table: "GoAmlDailyChecks",
                column: "ComplianceTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_GoAmlDailyChecks_Status_CheckDate",
                table: "GoAmlDailyChecks",
                columns: new[] { "Status", "CheckDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoAmlDailyChecks");

            migrationBuilder.DropTable(
                name: "GoAmlSettings");
        }
    }
}
