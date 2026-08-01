using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoAmlTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GoAmlTransferRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PackageId = table.Column<string>(type: "varchar(36)", maxLength: 36, nullable: false),
                    Direction = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    ContentSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "varchar(260)", maxLength: 260, nullable: false),
                    StoragePath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    FirstCheckDate = table.Column<DateTime>(type: "date", nullable: false),
                    LastCheckDate = table.Column<DateTime>(type: "date", nullable: false),
                    CheckCount = table.Column<int>(type: "int", nullable: false),
                    SummaryJson = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AppliedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoAmlTransferRecords", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_GoAmlTransferRecords_Direction_ContentSha256",
                table: "GoAmlTransferRecords",
                columns: new[] { "Direction", "ContentSha256" });

            migrationBuilder.CreateIndex(
                name: "IX_GoAmlTransferRecords_Direction_CreatedAtUtc",
                table: "GoAmlTransferRecords",
                columns: new[] { "Direction", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_GoAmlTransferRecords_Direction_PackageId",
                table: "GoAmlTransferRecords",
                columns: new[] { "Direction", "PackageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoAmlTransferRecords");
        }
    }
}
