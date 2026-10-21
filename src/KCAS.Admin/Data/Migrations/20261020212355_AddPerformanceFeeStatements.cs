using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceFeeStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StatementReviewJson",
                table: "PerformanceFeeProfiles",
                type: "longtext",
                nullable: true);

            migrationBuilder.Sql("UPDATE `PerformanceFeeProfiles` SET `StatementReviewJson` = '{}' WHERE `StatementReviewJson` IS NULL;");
            migrationBuilder.AlterColumn<string>(name: "StatementReviewJson", table: "PerformanceFeeProfiles", type: "longtext", nullable: false,
                oldClrType: typeof(string), oldType: "longtext", oldNullable: true);

            migrationBuilder.CreateTable(
                name: "PerformanceFeeStatements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PerformanceFeeProfileId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Sha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Content = table.Column<byte[]>(type: "longblob", nullable: false),
                    DocumentJson = table.Column<string>(type: "longtext", nullable: false),
                    ImportedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformanceFeeStatements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PerformanceFeeStatements_PerformanceFeeProfiles_PerformanceF~",
                        column: x => x.PerformanceFeeProfileId,
                        principalTable: "PerformanceFeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PerformanceFeeStatements_PerformanceFeeProfileId_Sha256",
                table: "PerformanceFeeStatements",
                columns: new[] { "PerformanceFeeProfileId", "Sha256" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerformanceFeeStatements");

            migrationBuilder.DropColumn(
                name: "StatementReviewJson",
                table: "PerformanceFeeProfiles");
        }
    }
}
