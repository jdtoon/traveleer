using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class AddRateCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RateCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefaultMealPlanId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ContractCurrencyCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ValidTo = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RateCards_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RateCards_MealPlans_DefaultMealPlanId",
                        column: x => x.DefaultMealPlanId,
                        principalTable: "MealPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RateSeasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RateCardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsBlackout = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateSeasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RateSeasons_RateCards_RateCardId",
                        column: x => x.RateCardId,
                        principalTable: "RateCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoomRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RateSeasonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoomTypeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WeekdayRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    WeekendRate = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    IsIncluded = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoomRates_RateSeasons_RateSeasonId",
                        column: x => x.RateSeasonId,
                        principalTable: "RateSeasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoomRates_RoomTypes_RoomTypeId",
                        column: x => x.RoomTypeId,
                        principalTable: "RoomTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RateCards_DefaultMealPlanId",
                table: "RateCards",
                column: "DefaultMealPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_RateCards_InventoryItemId_Status",
                table: "RateCards",
                columns: new[] { "InventoryItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RateSeasons_RateCardId_SortOrder",
                table: "RateSeasons",
                columns: new[] { "RateCardId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RateSeasons_RateCardId_StartDate_EndDate",
                table: "RateSeasons",
                columns: new[] { "RateCardId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomRates_RateSeasonId_RoomTypeId",
                table: "RoomRates",
                columns: new[] { "RateSeasonId", "RoomTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomRates_RoomTypeId",
                table: "RoomRates",
                column: "RoomTypeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoomRates");

            migrationBuilder.DropTable(
                name: "RateSeasons");

            migrationBuilder.DropTable(
                name: "RateCards");
        }
    }
}
