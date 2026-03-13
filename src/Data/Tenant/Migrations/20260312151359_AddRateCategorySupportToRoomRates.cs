using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class AddRateCategorySupportToRoomRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoomRates_RateSeasonId_RoomTypeId",
                table: "RoomRates");

            migrationBuilder.AlterColumn<Guid>(
                name: "RoomTypeId",
                table: "RoomRates",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<Guid>(
                name: "RateCategoryId",
                table: "RoomRates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomRates_RateCategoryId",
                table: "RoomRates",
                column: "RateCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomRates_RateSeasonId_RateCategoryId",
                table: "RoomRates",
                columns: new[] { "RateSeasonId", "RateCategoryId" },
                unique: true,
                filter: "\"RateCategoryId\" IS NOT NULL AND \"RoomTypeId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RoomRates_RateSeasonId_RoomTypeId",
                table: "RoomRates",
                columns: new[] { "RateSeasonId", "RoomTypeId" },
                unique: true,
                filter: "\"RoomTypeId\" IS NOT NULL AND \"RateCategoryId\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_RoomRates_RateCategories_RateCategoryId",
                table: "RoomRates",
                column: "RateCategoryId",
                principalTable: "RateCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoomRates_RateCategories_RateCategoryId",
                table: "RoomRates");

            migrationBuilder.DropIndex(
                name: "IX_RoomRates_RateCategoryId",
                table: "RoomRates");

            migrationBuilder.DropIndex(
                name: "IX_RoomRates_RateSeasonId_RateCategoryId",
                table: "RoomRates");

            migrationBuilder.DropIndex(
                name: "IX_RoomRates_RateSeasonId_RoomTypeId",
                table: "RoomRates");

            migrationBuilder.DropColumn(
                name: "RateCategoryId",
                table: "RoomRates");

            migrationBuilder.AlterColumn<Guid>(
                name: "RoomTypeId",
                table: "RoomRates",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomRates_RateSeasonId_RoomTypeId",
                table: "RoomRates",
                columns: new[] { "RateSeasonId", "RoomTypeId" },
                unique: true);
        }
    }
}
