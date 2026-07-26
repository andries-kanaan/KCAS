using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientRiskReviewCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreviousAssessmentId",
                table: "ClientRiskAssessments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewTriggerReason",
                table: "ClientRiskAssessments",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewTriggerType",
                table: "ClientRiskAssessments",
                type: "varchar(48)",
                maxLength: 48,
                nullable: false,
                defaultValue: "Initial");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewTriggeredAtUtc",
                table: "ClientRiskAssessments",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewTriggeredBy",
                table: "ClientRiskAssessments",
                type: "varchar(191)",
                maxLength: 191,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAtUtc",
                table: "ClientRiskAssessmentResponses",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedBy",
                table: "ClientRiskAssessmentResponses",
                type: "varchar(191)",
                maxLength: 191,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientRiskAssessments_PreviousAssessmentId",
                table: "ClientRiskAssessments",
                column: "PreviousAssessmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClientRiskAssessments_ClientRiskAssessments_PreviousAssessme~",
                table: "ClientRiskAssessments",
                column: "PreviousAssessmentId",
                principalTable: "ClientRiskAssessments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClientRiskAssessments_ClientRiskAssessments_PreviousAssessme~",
                table: "ClientRiskAssessments");

            migrationBuilder.DropIndex(
                name: "IX_ClientRiskAssessments_PreviousAssessmentId",
                table: "ClientRiskAssessments");

            migrationBuilder.DropColumn(
                name: "PreviousAssessmentId",
                table: "ClientRiskAssessments");

            migrationBuilder.DropColumn(
                name: "ReviewTriggerReason",
                table: "ClientRiskAssessments");

            migrationBuilder.DropColumn(
                name: "ReviewTriggerType",
                table: "ClientRiskAssessments");

            migrationBuilder.DropColumn(
                name: "ReviewTriggeredAtUtc",
                table: "ClientRiskAssessments");

            migrationBuilder.DropColumn(
                name: "ReviewTriggeredBy",
                table: "ClientRiskAssessments");

            migrationBuilder.DropColumn(
                name: "ConfirmedAtUtc",
                table: "ClientRiskAssessmentResponses");

            migrationBuilder.DropColumn(
                name: "ConfirmedBy",
                table: "ClientRiskAssessmentResponses");
        }
    }
}
