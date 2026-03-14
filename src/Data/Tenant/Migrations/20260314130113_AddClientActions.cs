using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace saas.Data.Tenant.Migrations
{
    /// <inheritdoc />
    public partial class AddClientActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PortalSessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionType = table.Column<int>(type: "INTEGER", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    AcknowledgedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    AcknowledgedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientActions_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClientActions_PortalSessions_PortalSessionId",
                        column: x => x.PortalSessionId,
                        principalTable: "PortalSessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientActions_ClientId",
                table: "ClientActions",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientActions_PortalSessionId",
                table: "ClientActions",
                column: "PortalSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientActions");
        }
    }
}
