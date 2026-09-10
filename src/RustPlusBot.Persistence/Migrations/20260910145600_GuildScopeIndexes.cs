using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustPlusBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GuildScopeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ServerMapSettings_GuildId",
                table: "ServerMapSettings",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_ServerCommandSettings_GuildId",
                table: "ServerCommandSettings",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionStates_GuildId",
                table: "ConnectionStates",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_ClanStates_GuildId",
                table: "ClanStates",
                column: "GuildId");

            migrationBuilder.CreateIndex(
                name: "IX_ClanPlayerNames_GuildId",
                table: "ClanPlayerNames",
                column: "GuildId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServerMapSettings_GuildId",
                table: "ServerMapSettings");

            migrationBuilder.DropIndex(
                name: "IX_ServerCommandSettings_GuildId",
                table: "ServerCommandSettings");

            migrationBuilder.DropIndex(
                name: "IX_ConnectionStates_GuildId",
                table: "ConnectionStates");

            migrationBuilder.DropIndex(
                name: "IX_ClanStates_GuildId",
                table: "ClanStates");

            migrationBuilder.DropIndex(
                name: "IX_ClanPlayerNames_GuildId",
                table: "ClanPlayerNames");
        }
    }
}
