using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Core.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCustomDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CustomDomain",
                table: "Tenants");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomDomain",
                table: "Tenants",
                type: "TEXT",
                nullable: true);
        }
    }
}
