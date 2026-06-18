using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustPlusBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MapSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerMapSettings",
                columns: table => new
                {
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    ShowGrid = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowMarkers = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowMonuments = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowVendor = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowPlayers = table.Column<bool>(type: "INTEGER", nullable: false),
                    ShowRigs = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerMapSettings", x => x.ServerId);
                    table.ForeignKey(
                        name: "FK_ServerMapSettings_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerMapSettings");
        }
    }
}
