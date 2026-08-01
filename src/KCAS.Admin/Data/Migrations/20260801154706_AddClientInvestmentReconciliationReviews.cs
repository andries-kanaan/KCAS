using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientInvestmentReconciliationReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientInvestmentReconciliationReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    ClientInvestmentAccountId = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    RelatedClientInvestmentAccountId = table.Column<int>(type: "int", nullable: true),
                    AppliedSurrenderDate = table.Column<DateTime>(type: "date", nullable: true),
                    EvidenceReference = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    SnapshotSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ReviewedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientInvestmentReconciliationReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientInvestmentReconciliationReviews_ClientInvestmentAccoun~",
                        column: x => x.ClientInvestmentAccountId,
                        principalTable: "ClientInvestmentAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientInvestmentReconciliationReviews_ClientInvestmentAccou~1",
                        column: x => x.RelatedClientInvestmentAccountId,
                        principalTable: "ClientInvestmentAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientInvestmentReconciliationReviews_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientInvestmentReconciliationReviews_ClientId_ClientInvestm~",
                table: "ClientInvestmentReconciliationReviews",
                columns: new[] { "ClientId", "ClientInvestmentAccountId", "ReviewedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientInvestmentReconciliationReviews_ClientInvestmentAccoun~",
                table: "ClientInvestmentReconciliationReviews",
                column: "ClientInvestmentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientInvestmentReconciliationReviews_Outcome",
                table: "ClientInvestmentReconciliationReviews",
                column: "Outcome");

            migrationBuilder.CreateIndex(
                name: "IX_ClientInvestmentReconciliationReviews_RelatedClientInvestmen~",
                table: "ClientInvestmentReconciliationReviews",
                column: "RelatedClientInvestmentAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientInvestmentReconciliationReviews");
        }
    }
}
