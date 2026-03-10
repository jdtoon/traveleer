using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VoucherGenerated",
                table: "BookingItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "VoucherGeneratedAt",
                table: "BookingItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VoucherNumber",
                table: "BookingItems",
                type: "TEXT",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VoucherGenerated",
                table: "BookingItems");

            migrationBuilder.DropColumn(
                name: "VoucherGeneratedAt",
                table: "BookingItems");

            migrationBuilder.DropColumn(
                name: "VoucherNumber",
                table: "BookingItems");
        }
    }
}
