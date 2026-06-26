using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustPlusBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SmartStorageMonitors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmartStorageMonitors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MessageId = table.Column<long>(type: "INTEGER", nullable: true),
                    PairedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartStorageMonitors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmartStorageMonitors_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmartStorageMonitors_GuildId_ServerId_EntityId",
                table: "SmartStorageMonitors",
                columns: new[] { "GuildId", "ServerId", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmartStorageMonitors_ServerId",
                table: "SmartStorageMonitors",
                column: "ServerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmartStorageMonitors");
        }
    }
}
