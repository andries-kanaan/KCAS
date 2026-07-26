using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientEntityOwnershipRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClientRelatedPartyId",
                table: "ClientEvidenceItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClientEntityProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    LegalForm = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    RegistrationNumber = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    RegistrationCountry = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: true),
                    EstablishmentDate = table.Column<DateTime>(type: "date", nullable: true),
                    NatureOfBusinessOrPurpose = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    OwnershipReviewStatus = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ControlConclusion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ControlConclusionReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    OwnershipReviewedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    OwnershipReviewedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    NextOwnershipReviewDate = table.Column<DateTime>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientEntityProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientEntityProfiles_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientRelatedParties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    PartyType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(240)", maxLength: 240, nullable: false),
                    SouthAfricanIdNumber = table.Column<string>(type: "varchar(13)", maxLength: 13, nullable: true),
                    PassportNumber = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    PassportCountry = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: true),
                    RegistrationNumber = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    BirthDate = table.Column<DateTime>(type: "date", nullable: true),
                    Nationality = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: true),
                    CountryOfResidence = table.Column<string>(type: "varchar(96)", maxLength: 96, nullable: true),
                    OwnershipPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    ControlBasis = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    AuthorityBasis = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientRelatedParties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientRelatedParties_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientRelatedPartyEvidenceLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientRelatedPartyId = table.Column<int>(type: "int", nullable: false),
                    ClientEvidenceItemId = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    LinkedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientRelatedPartyEvidenceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientRelatedPartyEvidenceLinks_ClientEvidenceItems_ClientEv~",
                        column: x => x.ClientEvidenceItemId,
                        principalTable: "ClientEvidenceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClientRelatedPartyEvidenceLinks_ClientRelatedParties_ClientR~",
                        column: x => x.ClientRelatedPartyId,
                        principalTable: "ClientRelatedParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ClientRelatedPartyRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientRelatedPartyId = table.Column<int>(type: "int", nullable: false),
                    RoleCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientRelatedPartyRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientRelatedPartyRoles_ClientRelatedParties_ClientRelatedPa~",
                        column: x => x.ClientRelatedPartyId,
                        principalTable: "ClientRelatedParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceItems_ClientRelatedPartyId",
                table: "ClientEvidenceItems",
                column: "ClientRelatedPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEntityProfiles_ClientId",
                table: "ClientEntityProfiles",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientEntityProfiles_OwnershipReviewStatus_NextOwnershipRevi~",
                table: "ClientEntityProfiles",
                columns: new[] { "OwnershipReviewStatus", "NextOwnershipReviewDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientRelatedParties_ClientId_IsActive",
                table: "ClientRelatedParties",
                columns: new[] { "ClientId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientRelatedParties_DisplayName",
                table: "ClientRelatedParties",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_ClientRelatedPartyEvidenceLinks_ClientEvidenceItemId_Purpose",
                table: "ClientRelatedPartyEvidenceLinks",
                columns: new[] { "ClientEvidenceItemId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientRelatedPartyEvidenceLinks_ClientRelatedPartyId_ClientE~",
                table: "ClientRelatedPartyEvidenceLinks",
                columns: new[] { "ClientRelatedPartyId", "ClientEvidenceItemId", "Purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientRelatedPartyRoles_ClientRelatedPartyId_RoleCode",
                table: "ClientRelatedPartyRoles",
                columns: new[] { "ClientRelatedPartyId", "RoleCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientRelatedPartyRoles_RoleCode",
                table: "ClientRelatedPartyRoles",
                column: "RoleCode");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientEvidenceItems_ClientRelatedParties_ClientRelatedPartyId",
                table: "ClientEvidenceItems",
                column: "ClientRelatedPartyId",
                principalTable: "ClientRelatedParties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientEvidenceItems_ClientRelatedParties_ClientRelatedPartyId",
                table: "ClientEvidenceItems");

            migrationBuilder.DropTable(
                name: "ClientEntityProfiles");

            migrationBuilder.DropTable(
                name: "ClientRelatedPartyEvidenceLinks");

            migrationBuilder.DropTable(
                name: "ClientRelatedPartyRoles");

            migrationBuilder.DropTable(
                name: "ClientRelatedParties");

            migrationBuilder.DropIndex(
                name: "IX_ClientEvidenceItems_ClientRelatedPartyId",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "ClientRelatedPartyId",
                table: "ClientEvidenceItems");
        }
    }
}
