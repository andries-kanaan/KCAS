using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComplianceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComplianceApprovals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    TargetEntityType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    TargetEntityId = table.Column<int>(type: "int", nullable: false),
                    Decision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Approver = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceApprovals", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ComplianceAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EntityType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    OldValueJson = table.Column<string>(type: "longtext", nullable: true),
                    NewValueJson = table.Column<string>(type: "longtext", nullable: true),
                    UserName = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceAuditEvents", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ComplianceEvidence",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    EvidenceType = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    Title = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    Source = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    Location = table.Column<string>(type: "longtext", nullable: true),
                    ReceivedDate = table.Column<DateTime>(type: "date", nullable: true),
                    VerifiedDate = table.Column<DateTime>(type: "date", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "date", nullable: true),
                    Reviewer = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    Notes = table.Column<string>(type: "longtext", nullable: true),
                    LinkedEntityType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    LinkedEntityId = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceEvidence", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ComplianceProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    LegalName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    TradingName = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    FspNumber = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    AccountableInstitutionNumber = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    PrimaryContactName = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    PrimaryContactEmail = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    PrimaryContactPhone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    RegisteredAddress = table.Column<string>(type: "longtext", nullable: true),
                    OperatingAddress = table.Column<string>(type: "longtext", nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceProfiles", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ComplianceReferenceValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Category = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    Code = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    Name = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceReferenceValues", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ComplianceTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Title = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    Owner = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    DueDate = table.Column<DateTime>(type: "date", nullable: true),
                    Priority = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    LinkedEntityType = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    LinkedEntityId = table.Column<int>(type: "int", nullable: true),
                    ClosureNotes = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceTasks", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ControlledDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    DocumentType = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    Title = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    Owner = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    VersionReference = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "date", nullable: true),
                    NextReviewDate = table.Column<DateTime>(type: "date", nullable: true),
                    Location = table.Column<string>(type: "longtext", nullable: true),
                    Notes = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ControlledDocuments", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GovernanceRoleAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    RoleType = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    PersonName = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Email = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    Phone = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    ResponsibilitySummary = table.Column<string>(type: "longtext", nullable: true),
                    StartDate = table.Column<DateTime>(type: "date", nullable: true),
                    EndDate = table.Column<DateTime>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GovernanceRoleAssignments", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RiskMethodologyVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    VersionLabel = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    Summary = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskMethodologyVersions", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RiskBands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    RiskMethodologyVersionId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    MinimumScore = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MaximumScore = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskBands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiskBands_RiskMethodologyVersions_RiskMethodologyVersionId",
                        column: x => x.RiskMethodologyVersionId,
                        principalTable: "RiskMethodologyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RiskFactorDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    RiskMethodologyVersionId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    Name = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    IsMandatoryHighRiskTrigger = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskFactorDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiskFactorDefinitions_RiskMethodologyVersions_RiskMethodolog~",
                        column: x => x.RiskMethodologyVersionId,
                        principalTable: "RiskMethodologyVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RiskFactorOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    RiskFactorDefinitionId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: false),
                    Label = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    TriggersHighRisk = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskFactorOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RiskFactorOptions_RiskFactorDefinitions_RiskFactorDefinition~",
                        column: x => x.RiskFactorDefinitionId,
                        principalTable: "RiskFactorDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceApprovals_TargetEntityType_TargetEntityId",
                table: "ComplianceApprovals",
                columns: new[] { "TargetEntityType", "TargetEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceAuditEvents_EntityType_EntityId",
                table: "ComplianceAuditEvents",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceAuditEvents_TimestampUtc",
                table: "ComplianceAuditEvents",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceEvidence_EvidenceType_ExpiryDate",
                table: "ComplianceEvidence",
                columns: new[] { "EvidenceType", "ExpiryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceEvidence_LinkedEntityType_LinkedEntityId",
                table: "ComplianceEvidence",
                columns: new[] { "LinkedEntityType", "LinkedEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceProfiles_Status",
                table: "ComplianceProfiles",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceReferenceValues_Category_Code_IsActive",
                table: "ComplianceReferenceValues",
                columns: new[] { "Category", "Code", "IsActive" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceReferenceValues_Category_SortOrder",
                table: "ComplianceReferenceValues",
                columns: new[] { "Category", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceTasks_LinkedEntityType_LinkedEntityId",
                table: "ComplianceTasks",
                columns: new[] { "LinkedEntityType", "LinkedEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceTasks_Status_DueDate",
                table: "ComplianceTasks",
                columns: new[] { "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ControlledDocuments_DocumentType_Status",
                table: "ControlledDocuments",
                columns: new[] { "DocumentType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ControlledDocuments_NextReviewDate",
                table: "ControlledDocuments",
                column: "NextReviewDate");

            migrationBuilder.CreateIndex(
                name: "IX_GovernanceRoleAssignments_RoleType_IsActive",
                table: "GovernanceRoleAssignments",
                columns: new[] { "RoleType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskBands_RiskMethodologyVersionId_Name",
                table: "RiskBands",
                columns: new[] { "RiskMethodologyVersionId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskFactorDefinitions_RiskMethodologyVersionId_Code",
                table: "RiskFactorDefinitions",
                columns: new[] { "RiskMethodologyVersionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskFactorOptions_RiskFactorDefinitionId_Code",
                table: "RiskFactorOptions",
                columns: new[] { "RiskFactorDefinitionId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RiskMethodologyVersions_EffectiveFrom",
                table: "RiskMethodologyVersions",
                column: "EffectiveFrom");

            migrationBuilder.CreateIndex(
                name: "IX_RiskMethodologyVersions_Status",
                table: "RiskMethodologyVersions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComplianceApprovals");

            migrationBuilder.DropTable(
                name: "ComplianceAuditEvents");

            migrationBuilder.DropTable(
                name: "ComplianceEvidence");

            migrationBuilder.DropTable(
                name: "ComplianceProfiles");

            migrationBuilder.DropTable(
                name: "ComplianceReferenceValues");

            migrationBuilder.DropTable(
                name: "ComplianceTasks");

            migrationBuilder.DropTable(
                name: "ControlledDocuments");

            migrationBuilder.DropTable(
                name: "GovernanceRoleAssignments");

            migrationBuilder.DropTable(
                name: "RiskBands");

            migrationBuilder.DropTable(
                name: "RiskFactorOptions");

            migrationBuilder.DropTable(
                name: "RiskFactorDefinitions");

            migrationBuilder.DropTable(
                name: "RiskMethodologyVersions");
        }
    }
}
