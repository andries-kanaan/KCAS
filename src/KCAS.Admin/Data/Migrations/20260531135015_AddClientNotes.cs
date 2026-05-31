using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    LegacyClientNoteId = table.Column<int>(type: "int", nullable: false),
                    NoteDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Title = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Details = table.Column<string>(type: "longtext", nullable: true),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsFinal = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    OpenedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LegacyOpenedByUserId = table.Column<int>(type: "int", nullable: true),
                    LegacyUpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    LegacyOpenedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LegacyUpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PayloadJson = table.Column<string>(type: "longtext", nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientNotes_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ClientNotes_ClientId_NoteDate",
                table: "ClientNotes",
                columns: new[] { "ClientId", "NoteDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientNotes_LegacyClientNoteId",
                table: "ClientNotes",
                column: "LegacyClientNoteId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientNotes_Title",
                table: "ClientNotes",
                column: "Title");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientNotes");
        }
    }
}
