using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientPassportDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PassportCountry",
                table: "ClientPersonalProfiles",
                type: "varchar(96)",
                maxLength: 96,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PassportExpiryDate",
                table: "ClientPersonalProfiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassportNumber",
                table: "ClientPersonalProfiles",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PassportCountry",
                table: "ClientPersonalProfiles");

            migrationBuilder.DropColumn(
                name: "PassportExpiryDate",
                table: "ClientPersonalProfiles");

            migrationBuilder.DropColumn(
                name: "PassportNumber",
                table: "ClientPersonalProfiles");
        }
    }
}
