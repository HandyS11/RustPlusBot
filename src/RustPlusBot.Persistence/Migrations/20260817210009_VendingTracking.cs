using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustPlusBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VendingTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VendingGridTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Grid = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false),
                    RegisteredBySteamId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendingGridTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendingGridTracks_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendingListingTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemIsBlueprint = table.Column<bool>(type: "INTEGER", nullable: false),
                    CurrencyId = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrencyIsBlueprint = table.Column<bool>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                    CostPerOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    RegisteredByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendingListingTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendingListingTracks_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendingNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemIsBlueprint = table.Column<bool>(type: "INTEGER", nullable: false),
                    CurrencyId = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrencyIsBlueprint = table.Column<bool>(type: "INTEGER", nullable: false),
                    MessageId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReferenceQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceCostPerOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    PostedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendingNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendingNotifications_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendingStockNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MachineId = table.Column<long>(type: "INTEGER", nullable: false),
                    MessageId = table.Column<long>(type: "INTEGER", nullable: false),
                    SoldOutSignature = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    PostedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendingStockNotifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendingStockNotifications_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VendingGridTracks_GuildId_ServerId_Grid",
                table: "VendingGridTracks",
                columns: new[] { "GuildId", "ServerId", "Grid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendingGridTracks_ServerId",
                table: "VendingGridTracks",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_VendingListingTracks_GuildId_ServerId_ItemId_ItemIsBlueprint_CurrencyId_CurrencyIsBlueprint",
                table: "VendingListingTracks",
                columns: new[] { "GuildId", "ServerId", "ItemId", "ItemIsBlueprint", "CurrencyId", "CurrencyIsBlueprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendingListingTracks_ServerId",
                table: "VendingListingTracks",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_VendingNotifications_GuildId_ServerId_ItemId_ItemIsBlueprint_CurrencyId_CurrencyIsBlueprint",
                table: "VendingNotifications",
                columns: new[] { "GuildId", "ServerId", "ItemId", "ItemIsBlueprint", "CurrencyId", "CurrencyIsBlueprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendingNotifications_ServerId",
                table: "VendingNotifications",
                column: "ServerId");

            migrationBuilder.CreateIndex(
                name: "IX_VendingStockNotifications_GuildId_ServerId_MachineId",
                table: "VendingStockNotifications",
                columns: new[] { "GuildId", "ServerId", "MachineId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VendingStockNotifications_ServerId",
                table: "VendingStockNotifications",
                column: "ServerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VendingGridTracks");

            migrationBuilder.DropTable(
                name: "VendingListingTracks");

            migrationBuilder.DropTable(
                name: "VendingNotifications");

            migrationBuilder.DropTable(
                name: "VendingStockNotifications");
        }
    }
}
