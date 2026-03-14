using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DropoffLocation",
                table: "InventoryItems",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IncludesMeetAndGreet",
                table: "InventoryItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxPassengers",
                table: "InventoryItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupLocation",
                table: "InventoryItems",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TransferDurationMinutes",
                table: "InventoryItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleType",
                table: "InventoryItems",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DropoffLocation",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "IncludesMeetAndGreet",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "MaxPassengers",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "PickupLocation",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "TransferDurationMinutes",
                table: "InventoryItems");

            migrationBuilder.DropColumn(
                name: "VehicleType",
                table: "InventoryItems");
        }
    }
}
