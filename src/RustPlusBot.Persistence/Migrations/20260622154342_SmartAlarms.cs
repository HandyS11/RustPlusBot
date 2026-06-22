using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustPlusBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SmartAlarms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmartAlarms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    MessageId = table.Column<long>(type: "INTEGER", nullable: true),
                    PairedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    PingEveryone = table.Column<bool>(type: "INTEGER", nullable: false),
                    RelayToTeamChat = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastTitle = table.Column<string>(type: "TEXT", nullable: true),
                    LastMessage = table.Column<string>(type: "TEXT", nullable: true),
                    LastFiredUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartAlarms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmartAlarms_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmartAlarms_GuildId_ServerId_EntityId",
                table: "SmartAlarms",
                columns: new[] { "GuildId", "ServerId", "EntityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SmartAlarms_ServerId",
                table: "SmartAlarms",
                column: "ServerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmartAlarms");
        }
    }
}
