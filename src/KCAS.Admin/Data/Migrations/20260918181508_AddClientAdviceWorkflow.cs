using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientAdviceWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientAdviceCases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    PreviousAdviceCaseId = table.Column<int>(type: "int", nullable: true),
                    AdviceType = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    AdviceDate = table.Column<DateTime>(type: "date", nullable: false),
                    AdviserName = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    PreparedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AdviceScope = table.Column<string>(type: "longtext", nullable: false),
                    MeetingSummary = table.Column<string>(type: "longtext", nullable: false),
                    NeedsAndObjectives = table.Column<string>(type: "longtext", nullable: false),
                    FinancialSituation = table.Column<string>(type: "longtext", nullable: false),
                    AdviceLimitations = table.Column<string>(type: "longtext", nullable: false),
                    ProductKnowledgeSummary = table.Column<string>(type: "longtext", nullable: false),
                    InvestmentAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    InvestmentPortfolioPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    DomesticPreferencePercent = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    OffshorePreferencePercent = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CalculatedRiskScore = table.Column<int>(type: "int", nullable: true),
                    CalculatedRiskLevel = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: true),
                    FinalRiskLevel = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: true),
                    RiskOverrideReason = table.Column<string>(type: "longtext", nullable: true),
                    RecommendationSummary = table.Column<string>(type: "longtext", nullable: false),
                    RecommendationRationale = table.Column<string>(type: "longtext", nullable: false),
                    CostsAndFees = table.Column<string>(type: "longtext", nullable: false),
                    TaxConsequences = table.Column<string>(type: "longtext", nullable: false),
                    LiquidityAndRestrictions = table.Column<string>(type: "longtext", nullable: false),
                    MaterialRisks = table.Column<string>(type: "longtext", nullable: false),
                    IsReplacement = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ReplacementConsequences = table.Column<string>(type: "longtext", nullable: false),
                    ClientDeparture = table.Column<string>(type: "longtext", nullable: false),
                    WarningsGiven = table.Column<string>(type: "longtext", nullable: false),
                    FrozenSnapshotJson = table.Column<string>(type: "longtext", nullable: true),
                    FrozenSnapshotSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAdviceCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAdviceCases_ClientAdviceCases_PreviousAdviceCaseId",
                        column: x => x.PreviousAdviceCaseId,
                        principalTable: "ClientAdviceCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientAdviceCases_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientAdviceApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientAdviceCaseId = table.Column<int>(type: "int", nullable: false),
                    Reviewer = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Decision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAdviceApprovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAdviceApprovals_ClientAdviceCases_ClientAdviceCaseId",
                        column: x => x.ClientAdviceCaseId,
                        principalTable: "ClientAdviceCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientAdviceDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientAdviceCaseId = table.Column<int>(type: "int", nullable: true),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                    FileName = table.Column<string>(type: "varchar(260)", maxLength: 260, nullable: false),
                    SourcePath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    FileSha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    FileLastWriteTimeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RecordedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAdviceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAdviceDocuments_ClientAdviceCases_ClientAdviceCaseId",
                        column: x => x.ClientAdviceCaseId,
                        principalTable: "ClientAdviceCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientAdviceDocuments_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientAdviceFactSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientAdviceCaseId = table.Column<int>(type: "int", nullable: false),
                    FactName = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    SourceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DocumentPath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAdviceFactSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAdviceFactSources_ClientAdviceCases_ClientAdviceCaseId",
                        column: x => x.ClientAdviceCaseId,
                        principalTable: "ClientAdviceCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientAdviceInvestmentLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientAdviceCaseId = table.Column<int>(type: "int", nullable: false),
                    ClientInvestmentAccountId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAdviceInvestmentLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAdviceInvestmentLinks_ClientAdviceCases_ClientAdviceCa~",
                        column: x => x.ClientAdviceCaseId,
                        principalTable: "ClientAdviceCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientAdviceInvestmentLinks_ClientInvestmentAccounts_ClientI~",
                        column: x => x.ClientInvestmentAccountId,
                        principalTable: "ClientInvestmentAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientAdviceParticipants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientAdviceCaseId = table.Column<int>(type: "int", nullable: false),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAdviceParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAdviceParticipants_ClientAdviceCases_ClientAdviceCaseId",
                        column: x => x.ClientAdviceCaseId,
                        principalTable: "ClientAdviceCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientAdviceParticipants_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientAdviceProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientAdviceCaseId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Provider = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    ProductType = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: true),
                    IsRecommended = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    AllocationPercent = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Motivation = table.Column<string>(type: "longtext", nullable: true),
                    SupportingDocumentPath = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAdviceProducts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAdviceProducts_ClientAdviceCases_ClientAdviceCaseId",
                        column: x => x.ClientAdviceCaseId,
                        principalTable: "ClientAdviceCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientAdviceReviewFindings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientAdviceCaseId = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Category = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    AffectedField = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: true),
                    Finding = table.Column<string>(type: "longtext", nullable: false),
                    EvidenceReference = table.Column<string>(type: "longtext", nullable: true),
                    RecommendedCorrection = table.Column<string>(type: "longtext", nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Resolution = table.Column<string>(type: "longtext", nullable: true),
                    PerformedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    PerformedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResolvedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAdviceReviewFindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAdviceReviewFindings_ClientAdviceCases_ClientAdviceCas~",
                        column: x => x.ClientAdviceCaseId,
                        principalTable: "ClientAdviceCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientAdviceRiskResponses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientAdviceCaseId = table.Column<int>(type: "int", nullable: false),
                    QuestionCode = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                    AnswerCode = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Explanation = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientAdviceRiskResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientAdviceRiskResponses_ClientAdviceCases_ClientAdviceCase~",
                        column: x => x.ClientAdviceCaseId,
                        principalTable: "ClientAdviceCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceApprovals_ClientAdviceCaseId_Reviewer",
                table: "ClientAdviceApprovals",
                columns: new[] { "ClientAdviceCaseId", "Reviewer" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceCases_ClientId_Status_AdviceDate",
                table: "ClientAdviceCases",
                columns: new[] { "ClientId", "Status", "AdviceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceCases_PreviousAdviceCaseId",
                table: "ClientAdviceCases",
                column: "PreviousAdviceCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceDocuments_ClientAdviceCaseId_DocumentType",
                table: "ClientAdviceDocuments",
                columns: new[] { "ClientAdviceCaseId", "DocumentType" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceDocuments_ClientId_DocumentType_FileSha256",
                table: "ClientAdviceDocuments",
                columns: new[] { "ClientId", "DocumentType", "FileSha256" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceFactSources_ClientAdviceCaseId_FactName",
                table: "ClientAdviceFactSources",
                columns: new[] { "ClientAdviceCaseId", "FactName" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceInvestmentLinks_ClientAdviceCaseId_ClientInvestm~",
                table: "ClientAdviceInvestmentLinks",
                columns: new[] { "ClientAdviceCaseId", "ClientInvestmentAccountId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceInvestmentLinks_ClientInvestmentAccountId",
                table: "ClientAdviceInvestmentLinks",
                column: "ClientInvestmentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceParticipants_ClientAdviceCaseId_ClientId",
                table: "ClientAdviceParticipants",
                columns: new[] { "ClientAdviceCaseId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceParticipants_ClientId",
                table: "ClientAdviceParticipants",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceProducts_ClientAdviceCaseId_ProductName",
                table: "ClientAdviceProducts",
                columns: new[] { "ClientAdviceCaseId", "ProductName" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceReviewFindings_ClientAdviceCaseId_Status",
                table: "ClientAdviceReviewFindings",
                columns: new[] { "ClientAdviceCaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientAdviceRiskResponses_ClientAdviceCaseId_QuestionCode",
                table: "ClientAdviceRiskResponses",
                columns: new[] { "ClientAdviceCaseId", "QuestionCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientAdviceApprovals");

            migrationBuilder.DropTable(
                name: "ClientAdviceDocuments");

            migrationBuilder.DropTable(
                name: "ClientAdviceFactSources");

            migrationBuilder.DropTable(
                name: "ClientAdviceInvestmentLinks");

            migrationBuilder.DropTable(
                name: "ClientAdviceParticipants");

            migrationBuilder.DropTable(
                name: "ClientAdviceProducts");

            migrationBuilder.DropTable(
                name: "ClientAdviceReviewFindings");

            migrationBuilder.DropTable(
                name: "ClientAdviceRiskResponses");

            migrationBuilder.DropTable(
                name: "ClientAdviceCases");
        }
    }
}
