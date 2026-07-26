using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientOperationalVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DuplicateOfClientId",
                table: "Clients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LifecycleReason",
                table: "Clients",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LifecycleReviewedAtUtc",
                table: "Clients",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LifecycleReviewedBy",
                table: "Clients",
                type: "varchar(191)",
                maxLength: 191,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LifecycleStatus",
                table: "Clients",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unreviewed");

            migrationBuilder.CreateTable(
                name: "ClientVerificationItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    FieldCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    FieldLabel = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    ChangeType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    ExistingValue = table.Column<string>(type: "longtext", nullable: true),
                    ProposedValue = table.Column<string>(type: "longtext", nullable: true),
                    SourceReference = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    Recommendation = table.Column<string>(type: "longtext", nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    IsBlocking = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DecidedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true),
                    DecisionReason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    AppliedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AppliedBy = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientVerificationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientVerificationItems_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_DuplicateOfClientId",
                table: "Clients",
                column: "DuplicateOfClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Clients_LifecycleStatus",
                table: "Clients",
                column: "LifecycleStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ClientVerificationItems_ClientId_Status_IsBlocking",
                table: "ClientVerificationItems",
                columns: new[] { "ClientId", "Status", "IsBlocking" });

            migrationBuilder.AddForeignKey(
                name: "FK_Clients_Clients_DuplicateOfClientId",
                table: "Clients",
                column: "DuplicateOfClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clients_Clients_DuplicateOfClientId",
                table: "Clients");

            migrationBuilder.DropTable(
                name: "ClientVerificationItems");

            migrationBuilder.DropIndex(
                name: "IX_Clients_DuplicateOfClientId",
                table: "Clients");

            migrationBuilder.DropIndex(
                name: "IX_Clients_LifecycleStatus",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "DuplicateOfClientId",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "LifecycleReason",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "LifecycleReviewedAtUtc",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "LifecycleReviewedBy",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "LifecycleStatus",
                table: "Clients");
        }
    }
}
