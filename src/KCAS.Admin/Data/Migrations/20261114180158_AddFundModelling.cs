using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFundModelling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ModelEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    FundId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    Actor = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    AtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    DetailJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelEvents", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModelFunds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    Archived = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CashValue = table.Column<double>(type: "double", nullable: false),
                    HoldingsJson = table.Column<string>(type: "longtext", nullable: false),
                    ScenariosJson = table.Column<string>(type: "longtext", nullable: false),
                    Revision = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelFunds", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModelSecurities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Key = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    Currency = table.Column<string>(type: "varchar(3)", maxLength: 3, nullable: false),
                    ProviderSymbol = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    SeriesJson = table.Column<string>(type: "longtext", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelSecurities", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ModelRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    FundId = table.Column<int>(type: "int", nullable: false),
                    Fingerprint = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Progress = table.Column<string>(type: "varchar(400)", maxLength: 400, nullable: false),
                    InputJson = table.Column<string>(type: "longtext", nullable: false),
                    ResultJson = table.Column<string>(type: "longtext", nullable: false),
                    Actor = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    FinishedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CancelRequested = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelRuns_ModelFunds_FundId",
                        column: x => x.FundId,
                        principalTable: "ModelFunds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ModelFunds_Key",
                table: "ModelFunds",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelRuns_FundId_Fingerprint",
                table: "ModelRuns",
                columns: new[] { "FundId", "Fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_ModelRuns_Key",
                table: "ModelRuns",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelSecurities_Key",
                table: "ModelSecurities",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ModelEvents");

            migrationBuilder.DropTable(
                name: "ModelRuns");

            migrationBuilder.DropTable(
                name: "ModelSecurities");

            migrationBuilder.DropTable(
                name: "ModelFunds");
        }
    }
}
