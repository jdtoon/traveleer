using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingQuoteLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "QuoteId",
                table: "Bookings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_QuoteId",
                table: "Bookings",
                column: "QuoteId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Quotes_QuoteId",
                table: "Bookings",
                column: "QuoteId",
                principalTable: "Quotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Quotes_QuoteId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_QuoteId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "QuoteId",
                table: "Bookings");
        }
    }
}
