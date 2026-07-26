using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComplianceWorkRegister : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BusinessRiskAssessmentId",
                table: "ComplianceTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClientId",
                table: "ComplianceTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClientRiskAssessmentId",
                table: "ComplianceTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosedBy",
                table: "ComplianceTasks",
                type: "varchar(191)",
                maxLength: 191,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureReason",
                table: "ComplianceTasks",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosureRequestedAtUtc",
                table: "ComplianceTasks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosureRequestedBy",
                table: "ComplianceTasks",
                type: "varchar(191)",
                maxLength: 191,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EscalatedAtUtc",
                table: "ComplianceTasks",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscalatedBy",
                table: "ComplianceTasks",
                type: "varchar(191)",
                maxLength: 191,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceSummary",
                table: "ComplianceTasks",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "ComplianceTasks",
                type: "longtext",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RmcpControlId",
                table: "ComplianceTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RmcpVersionId",
                table: "ComplianceTasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaskType",
                table: "ComplianceTasks",
                type: "varchar(48)",
                maxLength: 48,
                nullable: false,
                defaultValue: "Remediation");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceTasks_BusinessRiskAssessmentId",
                table: "ComplianceTasks",
                column: "BusinessRiskAssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceTasks_ClientId",
                table: "ComplianceTasks",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceTasks_ClientRiskAssessmentId",
                table: "ComplianceTasks",
                column: "ClientRiskAssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceTasks_RmcpControlId",
                table: "ComplianceTasks",
                column: "RmcpControlId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceTasks_RmcpVersionId",
                table: "ComplianceTasks",
                column: "RmcpVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceTasks_TaskType_Status_DueDate",
                table: "ComplianceTasks",
                columns: new[] { "TaskType", "Status", "DueDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_ComplianceTasks_BusinessRiskAssessments_BusinessRiskAssessme~",
                table: "ComplianceTasks",
                column: "BusinessRiskAssessmentId",
                principalTable: "BusinessRiskAssessments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ComplianceTasks_ClientRiskAssessments_ClientRiskAssessmentId",
                table: "ComplianceTasks",
                column: "ClientRiskAssessmentId",
                principalTable: "ClientRiskAssessments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ComplianceTasks_Clients_ClientId",
                table: "ComplianceTasks",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ComplianceTasks_RmcpControls_RmcpControlId",
                table: "ComplianceTasks",
                column: "RmcpControlId",
                principalTable: "RmcpControls",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ComplianceTasks_RmcpVersions_RmcpVersionId",
                table: "ComplianceTasks",
                column: "RmcpVersionId",
                principalTable: "RmcpVersions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ComplianceTasks_BusinessRiskAssessments_BusinessRiskAssessme~",
                table: "ComplianceTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_ComplianceTasks_ClientRiskAssessments_ClientRiskAssessmentId",
                table: "ComplianceTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_ComplianceTasks_Clients_ClientId",
                table: "ComplianceTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_ComplianceTasks_RmcpControls_RmcpControlId",
                table: "ComplianceTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_ComplianceTasks_RmcpVersions_RmcpVersionId",
                table: "ComplianceTasks");

            migrationBuilder.DropIndex(
                name: "IX_ComplianceTasks_BusinessRiskAssessmentId",
                table: "ComplianceTasks");

            migrationBuilder.DropIndex(
                name: "IX_ComplianceTasks_ClientId",
                table: "ComplianceTasks");

            migrationBuilder.DropIndex(
                name: "IX_ComplianceTasks_ClientRiskAssessmentId",
                table: "ComplianceTasks");

            migrationBuilder.DropIndex(
                name: "IX_ComplianceTasks_RmcpControlId",
                table: "ComplianceTasks");

            migrationBuilder.DropIndex(
                name: "IX_ComplianceTasks_RmcpVersionId",
                table: "ComplianceTasks");

            migrationBuilder.DropIndex(
                name: "IX_ComplianceTasks_TaskType_Status_DueDate",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "BusinessRiskAssessmentId",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "ClientRiskAssessmentId",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "ClosedBy",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "ClosureReason",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "ClosureRequestedAtUtc",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "ClosureRequestedBy",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "EscalatedAtUtc",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "EscalatedBy",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "EvidenceSummary",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "RmcpControlId",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "RmcpVersionId",
                table: "ComplianceTasks");

            migrationBuilder.DropColumn(
                name: "TaskType",
                table: "ComplianceTasks");
        }
    }
}
