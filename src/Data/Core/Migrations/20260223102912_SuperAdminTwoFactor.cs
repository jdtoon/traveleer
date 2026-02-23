using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Core.Migrations
{
    /// <inheritdoc />
    public partial class SuperAdminTwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTwoFactorEnabled",
                table: "SuperAdmins",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorRecoveryCodes",
                table: "SuperAdmins",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorSecret",
                table: "SuperAdmins",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTwoFactorEnabled",
                table: "SuperAdmins");

            migrationBuilder.DropColumn(
                name: "TwoFactorRecoveryCodes",
                table: "SuperAdmins");

            migrationBuilder.DropColumn(
                name: "TwoFactorSecret",
                table: "SuperAdmins");
        }
    }
}
