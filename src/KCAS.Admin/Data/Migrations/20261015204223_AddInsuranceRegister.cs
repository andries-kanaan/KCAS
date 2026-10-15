using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInsuranceRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InsurancePolicyId",
                table: "ComplianceTasks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InsurancePolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    TransferKey = table.Column<Guid>(type: "char(36)", nullable: false),
                    Version = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    InsuredName = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    PolicyNumber = table.Column<string>(type: "varchar(120)", maxLength: 120, nullable: false),
                    Insurer = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    Broker = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    BrokerContact = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    PrincipalAddress = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    BusinessDescription = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    StartDate = table.Column<DateTime>(type: "date", nullable: false),
                    EndDate = table.Column<DateTime>(type: "date", nullable: false),
                    RetroactiveDate = table.Column<DateTime>(type: "date", nullable: true),
                    Limit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CombinedLimit = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Excess = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PremiumExVat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Vat = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MonthlyInstalment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PaymentFrequency = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CoverSummary = table.Column<string>(type: "longtext", maxLength: 8000, nullable: false),
                    Exclusions = table.Column<string>(type: "longtext", maxLength: 8000, nullable: false),
                    Findings = table.Column<string>(type: "longtext", maxLength: 8000, nullable: false),
                    Owner = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false),
                    ReminderDays = table.Column<int>(type: "int", nullable: false),
                    FollowUpDays = table.Column<int>(type: "int", nullable: false),
                    UrgentDays = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    RequestedVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ReviewedVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsurancePolicies", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InsuranceDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    InsurancePolicyId = table.Column<int>(type: "int", nullable: false),
                    TransferKey = table.Column<Guid>(type: "char(36)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    RelativePath = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    Sha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    DocumentDate = table.Column<DateTime>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsuranceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsuranceDocuments_InsurancePolicies_InsurancePolicyId",
                        column: x => x.InsurancePolicyId,
                        principalTable: "InsurancePolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InsuranceEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    InsurancePolicyId = table.Column<int>(type: "int", nullable: false),
                    TransferKey = table.Column<Guid>(type: "char(36)", nullable: false),
                    Action = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Performer = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    RecordedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    OccurredOn = table.Column<DateTime>(type: "date", nullable: false),
                    Summary = table.Column<string>(type: "longtext", nullable: false),
                    DocumentKey = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsuranceEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InsuranceEvents_InsurancePolicies_InsurancePolicyId",
                        column: x => x.InsurancePolicyId,
                        principalTable: "InsurancePolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceTasks_InsurancePolicyId",
                table: "ComplianceTasks",
                column: "InsurancePolicyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsuranceDocuments_InsurancePolicyId",
                table: "InsuranceDocuments",
                column: "InsurancePolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_InsuranceDocuments_TransferKey",
                table: "InsuranceDocuments",
                column: "TransferKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsuranceEvents_InsurancePolicyId",
                table: "InsuranceEvents",
                column: "InsurancePolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_InsuranceEvents_TransferKey",
                table: "InsuranceEvents",
                column: "TransferKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsurancePolicies_PolicyNumber_StartDate",
                table: "InsurancePolicies",
                columns: new[] { "PolicyNumber", "StartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InsurancePolicies_TransferKey",
                table: "InsurancePolicies",
                column: "TransferKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ComplianceTasks_InsurancePolicies_InsurancePolicyId",
                table: "ComplianceTasks",
                column: "InsurancePolicyId",
                principalTable: "InsurancePolicies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ComplianceTasks_InsurancePolicies_InsurancePolicyId",
                table: "ComplianceTasks");

            migrationBuilder.DropTable(
                name: "InsuranceDocuments");

            migrationBuilder.DropTable(
                name: "InsuranceEvents");

            migrationBuilder.DropTable(
                name: "InsurancePolicies");

            migrationBuilder.DropIndex(
                name: "IX_ComplianceTasks_InsurancePolicyId",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "InsurancePolicyId",
                table: "ComplianceTasks");
        }
    }
}
