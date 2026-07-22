using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacyImportSnapshotProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ApprovedScanRunId",
                table: "LegacyImportRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSnapshotFileName",
                table: "LegacyImportRuns",
                type: "varchar(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSnapshotSha256",
                table: "LegacyImportRuns",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_LegacyImportRuns_SourceSnapshotSha256",
                table: "LegacyImportRuns",
                column: "SourceSnapshotSha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LegacyImportRuns_SourceSnapshotSha256",
                table: "LegacyImportRuns");

            migrationBuilder.DropColumn(
                name: "ApprovedScanRunId",
                table: "LegacyImportRuns");

            migrationBuilder.DropColumn(
                name: "SourceSnapshotFileName",
                table: "LegacyImportRuns");

            migrationBuilder.DropColumn(
                name: "SourceSnapshotSha256",
                table: "LegacyImportRuns");
        }
    }
}
