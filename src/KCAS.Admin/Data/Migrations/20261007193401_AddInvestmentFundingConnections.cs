using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestmentFundingConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvestmentFundingConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    SourceAccountId = table.Column<int>(type: "int", nullable: false),
                    DestinationAccountId = table.Column<int>(type: "int", nullable: false),
                    MatchedThroughDate = table.Column<DateTime>(type: "date", nullable: false),
                    SourceSnapshot = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DestinationSnapshot = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    EvidenceReference = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    PerformedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    RecordedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentFundingConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvestmentFundingConnections_ClientInvestmentAccounts_Destin~",
                        column: x => x.DestinationAccountId,
                        principalTable: "ClientInvestmentAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvestmentFundingConnections_ClientInvestmentAccounts_Source~",
                        column: x => x.SourceAccountId,
                        principalTable: "ClientInvestmentAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentFundingConnections_DestinationAccountId",
                table: "InvestmentFundingConnections",
                column: "DestinationAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentFundingConnections_SourceAccountId_RecordedAtUtc",
                table: "InvestmentFundingConnections",
                columns: new[] { "SourceAccountId", "RecordedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvestmentFundingConnections");
        }
    }
}
