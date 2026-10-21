using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientPerformanceFees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PerformanceFeeProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientInvestmentAccountId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    InceptionDate = table.Column<DateTime>(type: "date", nullable: true),
                    NextReviewDate = table.Column<DateTime>(type: "date", nullable: true),
                    PreparationJson = table.Column<string>(type: "longtext", nullable: false),
                    CheckpointJson = table.Column<string>(type: "longtext", nullable: true),
                    CodexRequestVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformanceFeeProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PerformanceFeeProfiles_ClientInvestmentAccounts_ClientInvest~",
                        column: x => x.ClientInvestmentAccountId,
                        principalTable: "ClientInvestmentAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PerformanceFeeClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PerformanceFeeProfileId = table.Column<int>(type: "int", nullable: false),
                    Reference = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    Version = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    From = table.Column<DateTime>(type: "date", nullable: false),
                    To = table.Column<DateTime>(type: "date", nullable: false),
                    InputJson = table.Column<string>(type: "longtext", nullable: false),
                    ResultJson = table.Column<string>(type: "longtext", nullable: false),
                    PreparedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Performer = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    PreparedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ApprovedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Paid = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformanceFeeClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PerformanceFeeClaims_PerformanceFeeProfiles_PerformanceFeePr~",
                        column: x => x.PerformanceFeeProfileId,
                        principalTable: "PerformanceFeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PerformanceFeeEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PerformanceFeeProfileId = table.Column<int>(type: "int", nullable: false),
                    ClaimId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Performer = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    RecordedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    OccurredOn = table.Column<DateTime>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "longtext", nullable: false),
                    Summary = table.Column<string>(type: "longtext", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformanceFeeEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PerformanceFeeEvents_PerformanceFeeProfiles_PerformanceFeePr~",
                        column: x => x.PerformanceFeeProfileId,
                        principalTable: "PerformanceFeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceFeeClaims_PerformanceFeeProfileId_From_To",
                table: "PerformanceFeeClaims",
                columns: new[] { "PerformanceFeeProfileId", "From", "To" });

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceFeeClaims_Reference",
                table: "PerformanceFeeClaims",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceFeeEvents_PerformanceFeeProfileId",
                table: "PerformanceFeeEvents",
                column: "PerformanceFeeProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceFeeProfiles_ClientInvestmentAccountId",
                table: "PerformanceFeeProfiles",
                column: "ClientInvestmentAccountId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerformanceFeeClaims");

            migrationBuilder.DropTable(
                name: "PerformanceFeeEvents");

            migrationBuilder.DropTable(
                name: "PerformanceFeeProfiles");
        }
    }
}
