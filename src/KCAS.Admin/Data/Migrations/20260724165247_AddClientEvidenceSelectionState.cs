using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientEvidenceSelectionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SelectedAtUtc",
                table: "ClientEvidenceItems",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectedBy",
                table: "ClientEvidenceItems",
                type: "varchar(191)",
                maxLength: 191,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SelectionConfidence",
                table: "ClientEvidenceItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectionReason",
                table: "ClientEvidenceItems",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectionStatus",
                table: "ClientEvidenceItems",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Candidate");

            migrationBuilder.AddColumn<int>(
                name: "SupersededByClientEvidenceItemId",
                table: "ClientEvidenceItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationPolicy",
                table: "ClientEvidenceItems",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "ManualRequired");

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceItems_ClientId_EvidenceType_SelectionStatus",
                table: "ClientEvidenceItems",
                columns: new[] { "ClientId", "EvidenceType", "SelectionStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceItems_SupersededByClientEvidenceItemId",
                table: "ClientEvidenceItems",
                column: "SupersededByClientEvidenceItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_CEI_SupersededBy",
                table: "ClientEvidenceItems",
                column: "SupersededByClientEvidenceItemId",
                principalTable: "ClientEvidenceItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CEI_SupersededBy",
                table: "ClientEvidenceItems");

            migrationBuilder.DropIndex(
                name: "IX_ClientEvidenceItems_ClientId_EvidenceType_SelectionStatus",
                table: "ClientEvidenceItems");

            migrationBuilder.DropIndex(
                name: "IX_ClientEvidenceItems_SupersededByClientEvidenceItemId",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "SelectedAtUtc",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "SelectedBy",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "SelectionConfidence",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "SelectionReason",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "SelectionStatus",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "SupersededByClientEvidenceItemId",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "VerificationPolicy",
                table: "ClientEvidenceItems");
        }
    }
}
