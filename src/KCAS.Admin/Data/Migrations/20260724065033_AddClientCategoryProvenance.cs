using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientCategoryProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClientCategoryReason",
                table: "Clients",
                type: "varchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientCategorySource",
                table: "Clients",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<DateTime>(
                name: "ClientCategoryUpdatedAtUtc",
                table: "Clients",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClientCategoryUpdatedBy",
                table: "Clients",
                type: "varchar(191)",
                maxLength: 191,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientCategoryReason",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ClientCategorySource",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ClientCategoryUpdatedAtUtc",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "ClientCategoryUpdatedBy",
                table: "Clients");
        }
    }
}
