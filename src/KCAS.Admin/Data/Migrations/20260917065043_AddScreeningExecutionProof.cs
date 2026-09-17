using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScreeningExecutionProof : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScreeningPerformedBy",
                table: "ClientEvidenceItems",
                type: "varchar(191)",
                maxLength: 191,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScreeningReviewedAtUtc",
                table: "ClientEvidenceItems",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScreeningSources",
                table: "ClientEvidenceItems",
                type: "varchar(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE `ClientEvidenceItems`
                SET `ScreeningReviewedAtUtc` = COALESCE(`UpdatedAtUtc`, `CreatedAtUtc`),
                    `ScreeningPerformedBy` = 'Codex',
                    `ScreeningSources` = CASE `EvidenceType`
                        WHEN 'SanctionsTfs' THEN 'FIC targeted financial sanctions list; UN Security Council consolidated sanctions list; targeted exact-name public search'
                        WHEN 'PepPip' THEN 'Targeted PEP/PIP public-register and open-source search; exact-name and known-role search'
                        WHEN 'AdverseInformation' THEN 'Targeted public news, court, regulatory and adverse-information search'
                        ELSE 'Sources recorded in the screening review notes'
                    END
                WHERE `ScreeningReviewDate` IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScreeningPerformedBy",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "ScreeningReviewedAtUtc",
                table: "ClientEvidenceItems");

            migrationBuilder.DropColumn(
                name: "ScreeningSources",
                table: "ClientEvidenceItems");
        }
    }
}
