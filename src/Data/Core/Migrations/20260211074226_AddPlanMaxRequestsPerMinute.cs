using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Core.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanMaxRequestsPerMinute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxRequestsPerMinute",
                table: "Plans",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxRequestsPerMinute",
                table: "Plans");
        }
    }
}
