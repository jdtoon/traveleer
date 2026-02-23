using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Core.Migrations
{
    /// <inheritdoc />
    public partial class ProductionReadinessFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookEvents_PaystackEventType_PaystackReference",
                table: "WebhookEvents");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "TenantCredits",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "Subscriptions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyStamp",
                table: "Discounts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_PaystackEventType_PaystackReference",
                table: "WebhookEvents",
                columns: new[] { "PaystackEventType", "PaystackReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PaystackReference",
                table: "Payments",
                column: "PaystackReference",
                unique: true,
                filter: "PaystackReference IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WebhookEvents_PaystackEventType_PaystackReference",
                table: "WebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_Payments_PaystackReference",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "TenantCredits");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "Discounts");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookEvents_PaystackEventType_PaystackReference",
                table: "WebhookEvents",
                columns: new[] { "PaystackEventType", "PaystackReference" });
        }
    }
}
