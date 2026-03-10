using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class AddBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrandingSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SingletonKey = table.Column<int>(type: "INTEGER", nullable: false),
                    AgencyName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PublicContactEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    ContactPhone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Website = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LogoUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PrimaryColor = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SecondaryColor = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    QuotePrefix = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    QuoteNumberFormat = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    NextQuoteSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    QuoteResetSequenceYearly = table.Column<bool>(type: "INTEGER", nullable: false),
                    QuoteSequenceLastResetYear = table.Column<int>(type: "INTEGER", nullable: true),
                    DefaultQuoteValidityDays = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultQuoteMarkupPercentage = table.Column<decimal>(type: "TEXT", precision: 6, scale: 2, nullable: true),
                    PdfFooterText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandingSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrandingSettings_SingletonKey",
                table: "BrandingSettings",
                column: "SingletonKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrandingSettings");
        }
    }
}
