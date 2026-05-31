using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientInvestments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientInvestmentAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    LegacyInvestmentAccountId = table.Column<int>(type: "int", nullable: false),
                    LegacyClientId = table.Column<int>(type: "int", nullable: true),
                    InvestmentDate = table.Column<DateTime>(type: "date", nullable: true),
                    SurrenderDate = table.Column<DateTime>(type: "date", nullable: true),
                    Administrator = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyAdministratorId = table.Column<int>(type: "int", nullable: true),
                    AccountNumber = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ProductName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyProductId = table.Column<int>(type: "int", nullable: true),
                    ProductType = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyProductTypeId = table.Column<int>(type: "int", nullable: true),
                    FundName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyFundId = table.Column<int>(type: "int", nullable: true),
                    IsLinkedHead = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LegacyLinkedAccountId = table.Column<int>(type: "int", nullable: true),
                    IsFinal = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    OpenedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyOpenedByUserId = table.Column<int>(type: "int", nullable: true),
                    LegacyUpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    LegacyOpenedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LegacyUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientInvestmentAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientInvestmentAccounts_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientInvestmentTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientInvestmentAccountId = table.Column<int>(type: "int", nullable: false),
                    LegacyInvestmentHistoryId = table.Column<int>(type: "int", nullable: false),
                    LegacyInvestmentAccountId = table.Column<int>(type: "int", nullable: true),
                    TransactionDate = table.Column<DateTime>(type: "date", nullable: true),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    ExchangeRate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    InvestmentAmountForeign = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    InvestmentAmountZar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    WithdrawalAmountForeign = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    WithdrawalAmountZar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    InvestmentFrequency = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    AnnualIncreasePercent = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    BalanceForeign = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    BalanceZar = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsFinal = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    OpenedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyOpenedByUserId = table.Column<int>(type: "int", nullable: true),
                    LegacyUpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    LegacyOpenedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LegacyUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientInvestmentTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientInvestmentTransactions_ClientInvestmentAccounts_Client~",
                        column: x => x.ClientInvestmentAccountId,
                        principalTable: "ClientInvestmentAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientInvestmentAccounts_AccountNumber",
                table: "ClientInvestmentAccounts",
                column: "AccountNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ClientInvestmentAccounts_ClientId",
                table: "ClientInvestmentAccounts",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientInvestmentAccounts_LegacyClientId",
                table: "ClientInvestmentAccounts",
                column: "LegacyClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientInvestmentAccounts_LegacyInvestmentAccountId",
                table: "ClientInvestmentAccounts",
                column: "LegacyInvestmentAccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientInvestmentAccounts_LegacyLinkedAccountId",
                table: "ClientInvestmentAccounts",
                column: "LegacyLinkedAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientInvestmentTransactions_ClientInvestmentAccountId",
                table: "ClientInvestmentTransactions",
                column: "ClientInvestmentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientInvestmentTransactions_LegacyInvestmentAccountId",
                table: "ClientInvestmentTransactions",
                column: "LegacyInvestmentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientInvestmentTransactions_LegacyInvestmentHistoryId",
                table: "ClientInvestmentTransactions",
                column: "LegacyInvestmentHistoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientInvestmentTransactions_TransactionDate",
                table: "ClientInvestmentTransactions",
                column: "TransactionDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientInvestmentTransactions");

            migrationBuilder.DropTable(
                name: "ClientInvestmentAccounts");
        }
    }
}
