using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedClientImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientCode",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "SouthAfricanIdNumber",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "MobileNumber",
                table: "Clients");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Clients",
                type: "datetime(6)",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP(6)",
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)");

            migrationBuilder.AddColumn<string>(
                name: "ClientFolder",
                table: "Clients",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Clients",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "Clients",
                type: "varchar(220)",
                maxLength: 220,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "Clients",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Initials",
                table: "Clients",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KanaanId",
                table: "Clients",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Clients",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LegacyClientId",
                table: "Clients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SurnameOrEntityName",
                table: "Clients",
                type: "varchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ClientAddresses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    AddressType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    LinesRaw = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    LegacySourceField = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAddresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAddresses_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientContactPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    ContactType = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false),
                    Label = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    Value = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false),
                    IsPrimary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    LegacySourceField = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientContactPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientContactPoints_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientFinancialProfiles",
                columns: table => new
                {
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    Employer = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    Occupation = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    GrossMonthlySalary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    GrossAnnualSalary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MonthlyExpenses = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    YearlyBonus = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OtherIncome = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    RetirementAge = table.Column<int>(type: "int", nullable: true),
                    PensionFundName = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    EmployerPensionContributionAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    EmployerPensionContributionPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    CapitalRequirementPercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    MinimumRetirementIncomePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    ExpectedRetirementIncomePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    BankDetailRaw = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    WillDetailRaw = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    OtherGoalsRaw = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    OtherDetailsRaw = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientFinancialProfiles", x => x.ClientId);
                    table.ForeignKey(
                        name: "FK_ClientFinancialProfiles_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientLegacySnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    SourceTable = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientLegacySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientLegacySnapshots_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientPersonalProfiles",
                columns: table => new
                {
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    SouthAfricanIdNumber = table.Column<string>(type: "varchar(13)", maxLength: 13, nullable: true),
                    Gender = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    MaritalStatus = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    TaxOffice = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    TaxNumber = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    IsTaxClient = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    HighestQualification = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    Smoker = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    NumberOfDependents = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientPersonalProfiles", x => x.ClientId);
                    table.ForeignKey(
                        name: "FK_ClientPersonalProfiles_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientRelationships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    RelationshipType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false),
                    LegacyRelatedClientId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    Initials = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    Gender = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true),
                    BirthDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    SouthAfricanIdNumber = table.Column<string>(type: "varchar(13)", maxLength: 13, nullable: true),
                    Email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: true),
                    HomePhone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    WorkPhone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    MobilePhone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    Employer = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    Occupation = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true),
                    HighestQualification = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientRelationships_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_DisplayName",
                table: "Clients",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_KanaanId",
                table: "Clients",
                column: "KanaanId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_LegacyClientId",
                table: "Clients",
                column: "LegacyClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientAddresses_ClientId_AddressType",
                table: "ClientAddresses",
                columns: new[] { "ClientId", "AddressType" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientContactPoints_ClientId_ContactType_IsPrimary",
                table: "ClientContactPoints",
                columns: new[] { "ClientId", "ContactType", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientContactPoints_Value",
                table: "ClientContactPoints",
                column: "Value");

            migrationBuilder.CreateIndex(
                name: "IX_ClientLegacySnapshots_ClientId",
                table: "ClientLegacySnapshots",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientLegacySnapshots_SourceTable_SourceId",
                table: "ClientLegacySnapshots",
                columns: new[] { "SourceTable", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientPersonalProfiles_SouthAfricanIdNumber",
                table: "ClientPersonalProfiles",
                column: "SouthAfricanIdNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRelationships_ClientId_RelationshipType",
                table: "ClientRelationships",
                columns: new[] { "ClientId", "RelationshipType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientAddresses");

            migrationBuilder.DropTable(
                name: "ClientContactPoints");

            migrationBuilder.DropTable(
                name: "ClientFinancialProfiles");

            migrationBuilder.DropTable(
                name: "ClientLegacySnapshots");

            migrationBuilder.DropTable(
                name: "ClientPersonalProfiles");

            migrationBuilder.DropTable(
                name: "ClientRelationships");

            migrationBuilder.DropIndex(
                name: "IX_Clients_DisplayName",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Clients_KanaanId",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Clients_LegacyClientId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ClientFolder",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "Initials",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "KanaanId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "LegacyClientId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "SurnameOrEntityName",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Clients");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Clients",
                type: "datetime(6)",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime(6)",
                oldDefaultValueSql: "CURRENT_TIMESTAMP(6)");

            migrationBuilder.AddColumn<string>(
                name: "ClientCode",
                table: "Clients",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Clients",
                type: "varchar(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Clients",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Clients",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MobileNumber",
                table: "Clients",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SouthAfricanIdNumber",
                table: "Clients",
                type: "varchar(13)",
                maxLength: 13,
                nullable: true);
        }
    }
}
