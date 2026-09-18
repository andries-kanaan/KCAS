using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientAdviceTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TransferKey",
                table: "ClientAdviceCases",
                type: "varchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClientAdviceTransferRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PackageId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    Direction = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    ContentSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "varchar(260)", maxLength: 260, nullable: false),
                    StoragePath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    CaseCount = table.Column<int>(type: "int", nullable: false),
                    DocumentCount = table.Column<int>(type: "int", nullable: false),
                    SummaryJson = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AppliedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAdviceTransferRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAdviceTransferRecords_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceCases_TransferKey",
                table: "ClientAdviceCases",
                column: "TransferKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceTransferRecords_ClientId_CreatedAtUtc",
                table: "ClientAdviceTransferRecords",
                columns: new[] { "ClientId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceTransferRecords_Direction_ContentSha256",
                table: "ClientAdviceTransferRecords",
                columns: new[] { "Direction", "ContentSha256" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceTransferRecords_Direction_PackageId",
                table: "ClientAdviceTransferRecords",
                columns: new[] { "Direction", "PackageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientAdviceTransferRecords");

            migrationBuilder.DropIndex(
                name: "IX_ClientAdviceCases_TransferKey",
                table: "ClientAdviceCases");

            migrationBuilder.DropColumn(
                name: "TransferKey",
                table: "ClientAdviceCases");
        }
    }
}
