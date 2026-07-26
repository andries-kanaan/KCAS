using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InspectionCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Reference = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    Title = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    RequestingAuthority = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    AsAtDate = table.Column<DateTime>(type: "date", nullable: false),
                    RequestDate = table.Column<DateTime>(type: "date", nullable: false),
                    DueDate = table.Column<DateTime>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Scope = table.Column<string>(type: "longtext", nullable: false),
                    Coordinator = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Notes = table.Column<string>(type: "longtext", nullable: true),
                    SnapshotJson = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FrozenAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionCases", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InspectionReadinessChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    InspectionCaseId = table.Column<int>(type: "int", nullable: false),
                    CheckType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    EvidenceLocation = table.Column<string>(type: "longtext", nullable: true),
                    Notes = table.Column<string>(type: "longtext", nullable: true),
                    TestedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    TestedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionReadinessChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionReadinessChecks_InspectionCases_InspectionCaseId",
                        column: x => x.InspectionCaseId,
                        principalTable: "InspectionCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InspectionRequestItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    InspectionCaseId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    Owner = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    DueDate = table.Column<DateTime>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    EvidenceTitle = table.Column<string>(type: "longtext", nullable: true),
                    EvidenceLocation = table.Column<string>(type: "longtext", nullable: true),
                    LinkedEntityType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    LinkedEntityId = table.Column<int>(type: "int", nullable: true),
                    ReviewNotes = table.Column<string>(type: "longtext", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionRequestItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionRequestItems_InspectionCases_InspectionCaseId",
                        column: x => x.InspectionCaseId,
                        principalTable: "InspectionCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionCases_Status_DueDate",
                table: "InspectionCases",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionReadinessChecks_InspectionCaseId_CheckType",
                table: "InspectionReadinessChecks",
                columns: new[] { "InspectionCaseId", "CheckType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionRequestItems_InspectionCaseId_Status_DueDate",
                table: "InspectionRequestItems",
                columns: new[] { "InspectionCaseId", "Status", "DueDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InspectionReadinessChecks");

            migrationBuilder.DropTable(
                name: "InspectionRequestItems");

            migrationBuilder.DropTable(
                name: "InspectionCases");
        }
    }
}
