using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClientName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ClientEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    ClientPhone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    OutputCurrencyCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    MarkupPercentage = table.Column<decimal>(type: "TEXT", precision: 6, scale: 2, nullable: false),
                    GroupBy = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ValidUntil = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    TravelStartDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    TravelEndDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    FilterByTravelDates = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    InternalNotes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quotes_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "QuoteRateCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RateCardId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteRateCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteRateCards_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QuoteRateCards_RateCards_RateCardId",
                        column: x => x.RateCardId,
                        principalTable: "RateCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRateCards_QuoteId_RateCardId",
                table: "QuoteRateCards",
                columns: new[] { "QuoteId", "RateCardId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRateCards_QuoteId_SortOrder",
                table: "QuoteRateCards",
                columns: new[] { "QuoteId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_QuoteRateCards_RateCardId",
                table: "QuoteRateCards",
                column: "RateCardId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_ClientId",
                table: "Quotes",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_CreatedAt",
                table: "Quotes",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_ReferenceNumber",
                table: "Quotes",
                column: "ReferenceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_Status",
                table: "Quotes",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuoteRateCards");

            migrationBuilder.DropTable(
                name: "Quotes");
        }
    }
}
