using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReferenceDataClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvestmentAdministratorReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    LegacyLispId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    ShortName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    IsCurrent = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MonthlyUpload = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    OpenedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyOpenedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LegacyUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentAdministratorReferences", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InvestmentFundReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    LegacyFundNameId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    ShortName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Currency = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    IsCurrent = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MonthlyUpload = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LegacyMainClassId = table.Column<int>(type: "int", nullable: true),
                    LegacySubClassId = table.Column<int>(type: "int", nullable: true),
                    LegacyAdministratorId = table.Column<int>(type: "int", nullable: true),
                    OpenedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyOpenedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LegacyUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentFundReferences", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InvestmentProductTypeReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    LegacyCompanyProductId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    OpenedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyOpenedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LegacyUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvestmentProductTypeReferences", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "KycMainClassReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    LegacyMainClassId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    AfrikaansDescription = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    EnglishDescription = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    OpenedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyOpenedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LegacyUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KycMainClassReferences", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "MarketReferenceValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    LegacyMiscInfoId = table.Column<int>(type: "int", nullable: true),
                    PriceDate = table.Column<DateTime>(type: "date", nullable: true),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    OpenedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyOpenedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LegacyUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketReferenceValues", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "KycSubClassReferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    LegacySubClassId = table.Column<int>(type: "int", nullable: true),
                    KycMainClassReferenceId = table.Column<int>(type: "int", nullable: false),
                    LegacyMainClassId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    OpenedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyOpenedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LegacyUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KycSubClassReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KycSubClassReferences_KycMainClassReferences_KycMainClassRef~",
                        column: x => x.KycMainClassReferenceId,
                        principalTable: "KycMainClassReferences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentAdministratorReferences_IsCurrent_Name",
                table: "InvestmentAdministratorReferences",
                columns: new[] { "IsCurrent", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentAdministratorReferences_LegacyLispId",
                table: "InvestmentAdministratorReferences",
                column: "LegacyLispId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentAdministratorReferences_Name",
                table: "InvestmentAdministratorReferences",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentFundReferences_IsCurrent_Name",
                table: "InvestmentFundReferences",
                columns: new[] { "IsCurrent", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentFundReferences_LegacyAdministratorId_LegacyMainCla~",
                table: "InvestmentFundReferences",
                columns: new[] { "LegacyAdministratorId", "LegacyMainClassId", "LegacySubClassId" });

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentFundReferences_LegacyFundNameId",
                table: "InvestmentFundReferences",
                column: "LegacyFundNameId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentFundReferences_Name",
                table: "InvestmentFundReferences",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentFundReferences_ShortName",
                table: "InvestmentFundReferences",
                column: "ShortName");

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentProductTypeReferences_LegacyCompanyProductId",
                table: "InvestmentProductTypeReferences",
                column: "LegacyCompanyProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InvestmentProductTypeReferences_Name",
                table: "InvestmentProductTypeReferences",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_KycMainClassReferences_LegacyMainClassId",
                table: "KycMainClassReferences",
                column: "LegacyMainClassId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KycMainClassReferences_Name",
                table: "KycMainClassReferences",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_KycSubClassReferences_KycMainClassReferenceId_Name",
                table: "KycSubClassReferences",
                columns: new[] { "KycMainClassReferenceId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_KycSubClassReferences_LegacyMainClassId",
                table: "KycSubClassReferences",
                column: "LegacyMainClassId");

            migrationBuilder.CreateIndex(
                name: "IX_KycSubClassReferences_LegacySubClassId",
                table: "KycSubClassReferences",
                column: "LegacySubClassId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketReferenceValues_LegacyMiscInfoId",
                table: "MarketReferenceValues",
                column: "LegacyMiscInfoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MarketReferenceValues_Name",
                table: "MarketReferenceValues",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_MarketReferenceValues_PriceDate",
                table: "MarketReferenceValues",
                column: "PriceDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvestmentAdministratorReferences");

            migrationBuilder.DropTable(
                name: "InvestmentFundReferences");

            migrationBuilder.DropTable(
                name: "InvestmentProductTypeReferences");

            migrationBuilder.DropTable(
                name: "KycSubClassReferences");

            migrationBuilder.DropTable(
                name: "MarketReferenceValues");

            migrationBuilder.DropTable(
                name: "KycMainClassReferences");
        }
    }
}
