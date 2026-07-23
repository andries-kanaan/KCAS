using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientEvidenceScreeningReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EscalationRequired",
                table: "ClientEvidenceItems",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ScreeningOutcome",
                table: "ClientEvidenceItems",
                type: "varchar(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScreeningReviewDate",
                table: "ClientEvidenceItems",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScreeningRiskSignal",
                table: "ClientEvidenceItems",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScreeningSubjectName",
                table: "ClientEvidenceItems",
                type: "varchar(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScreeningSubjectType",
                table: "ClientEvidenceItems",
                type: "varchar(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceItems_ClientId_EvidenceType_ScreeningRiskSignal",
                table: "ClientEvidenceItems",
                columns: new[] { "ClientId", "EvidenceType", "ScreeningRiskSignal" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientEvidenceItems_EscalationRequired",
                table: "ClientEvidenceItems",
                column: "EscalationRequired");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClientEvidenceItems_ClientId_EvidenceType_ScreeningRiskSignal",
                table: "ClientEvidenceItems");

            migrationBuilder.DropIndex(
                name: "IX_ClientEvidenceItems_EscalationRequired",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "EscalationRequired",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "ScreeningOutcome",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "ScreeningReviewDate",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "ScreeningRiskSignal",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "ScreeningSubjectName",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "ScreeningSubjectType",
                table: "ClientEvidenceItems");
        }
    }
}
