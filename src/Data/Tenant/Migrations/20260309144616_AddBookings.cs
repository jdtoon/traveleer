using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class AddBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookingRef = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientReference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TravelStartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    TravelEndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Pax = table.Column<int>(type: "INTEGER", nullable: false),
                    LeadGuestName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LeadGuestNationality = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TotalCost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TotalSelling = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TotalProfit = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CostCurrencyCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SellingCurrencyCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    InternalNotes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    SpecialRequests = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookings_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BookingItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BookingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SupplierId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ServiceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ServiceKind = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ServiceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Nights = table.Column<int>(type: "INTEGER", nullable: true),
                    CostPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    SellingPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CostCurrencyCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SellingCurrencyCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Pax = table.Column<int>(type: "INTEGER", nullable: false),
                    SupplierStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SupplierReference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SupplierNotes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookingItems_Bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "Bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BookingItems_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_BookingItems_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingItems_BookingId",
                table: "BookingItems",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingItems_InventoryItemId",
                table: "BookingItems",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingItems_SupplierId",
                table: "BookingItems",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingItems_SupplierStatus",
                table: "BookingItems",
                column: "SupplierStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_BookingRef",
                table: "Bookings",
                column: "BookingRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ClientId",
                table: "Bookings",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Status",
                table: "Bookings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_TravelStartDate",
                table: "Bookings",
                column: "TravelStartDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookingItems");

            migrationBuilder.DropTable(
                name: "Bookings");
        }
    }
}
