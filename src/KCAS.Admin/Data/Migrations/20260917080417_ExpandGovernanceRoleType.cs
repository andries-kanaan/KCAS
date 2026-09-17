using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KCAS.Admin.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandGovernanceRoleType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RoleType",
                table: "GovernanceRoleAssignments",
                type: "varchar(191)",
                maxLength: 191,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(96)",
                oldMaxLength: 96);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RoleType",
                table: "GovernanceRoleAssignments",
                type: "varchar(96)",
                maxLength: 96,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(191)",
                oldMaxLength: 191);
        }
    }
}
