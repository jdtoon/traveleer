using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Audit.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEntrySource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "AuditEntries",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Tenant");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_Source",
                table: "AuditEntries",
                column: "Source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditEntries_Source",
                table: "AuditEntries");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "AuditEntries");
        }
    }
}
