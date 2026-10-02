using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TransferKey",
                table: "EmployeeProfiles",
                type: "varchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE `EmployeeProfiles` SET `TransferKey` = REPLACE(UUID(), '-', '') WHERE `TransferKey` = '';");

            migrationBuilder.CreateTable(
                name: "EmployeeTransferRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    PackageId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Direction = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    EmployeeKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    SourceSystemKey = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    EmployeeProfileId = table.Column<int>(type: "int", nullable: false),
                    SourceDigest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    LocalDigest = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    MappingJson = table.Column<string>(type: "longtext", nullable: false),
                    ActorNamesJson = table.Column<string>(type: "longtext", nullable: false),
                    StoragePath = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    FileName = table.Column<string>(type: "varchar(191)", maxLength: 191, nullable: false),
                    UserId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false),
                    PackageCreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeTransferRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeTransferRecords_EmployeeProfiles_EmployeeProfileId",
                        column: x => x.EmployeeProfileId,
                        principalTable: "EmployeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeProfiles_TransferKey",
                table: "EmployeeProfiles",
                column: "TransferKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTransferRecords_EmployeeProfileId",
                table: "EmployeeTransferRecords",
                column: "EmployeeProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeTransferRecords_PackageId_Direction_EmployeeKey",
                table: "EmployeeTransferRecords",
                columns: new[] { "PackageId", "Direction", "EmployeeKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeTransferRecords");

            migrationBuilder.DropIndex(
                name: "IX_EmployeeProfiles_TransferKey",
                table: "EmployeeProfiles");

            migrationBuilder.DropColumn(
                name: "TransferKey",
                table: "EmployeeProfiles");
        }
    }
}
