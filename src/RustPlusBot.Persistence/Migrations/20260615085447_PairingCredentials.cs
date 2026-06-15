using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustPlusBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PairingCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerCredentials_GuildId_RustServerId",
                table: "PlayerCredentials");

            migrationBuilder.DropColumn(
                name: "ProtectedFcmCredentials",
                table: "PlayerCredentials");

            migrationBuilder.CreateTable(
                name: "FcmRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    OwnerUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ProtectedFcmCredentials = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FcmRegistrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RustServers_GuildId_Ip_Port",
                table: "RustServers",
                columns: new[] { "GuildId", "Ip", "Port" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCredentials_GuildId_RustServerId_OwnerUserId",
                table: "PlayerCredentials",
                columns: new[] { "GuildId", "RustServerId", "OwnerUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCredentials_RustServerId",
                table: "PlayerCredentials",
                column: "RustServerId");

            migrationBuilder.CreateIndex(
                name: "IX_FcmRegistrations_GuildId_OwnerUserId",
                table: "FcmRegistrations",
                columns: new[] { "GuildId", "OwnerUserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerCredentials_RustServers_RustServerId",
                table: "PlayerCredentials",
                column: "RustServerId",
                principalTable: "RustServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerCredentials_RustServers_RustServerId",
                table: "PlayerCredentials");

            migrationBuilder.DropTable(
                name: "FcmRegistrations");

            migrationBuilder.DropIndex(
                name: "IX_RustServers_GuildId_Ip_Port",
                table: "RustServers");

            migrationBuilder.DropIndex(
                name: "IX_PlayerCredentials_GuildId_RustServerId_OwnerUserId",
                table: "PlayerCredentials");

            migrationBuilder.DropIndex(
                name: "IX_PlayerCredentials_RustServerId",
                table: "PlayerCredentials");

            migrationBuilder.AddColumn<string>(
                name: "ProtectedFcmCredentials",
                table: "PlayerCredentials",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerCredentials_GuildId_RustServerId",
                table: "PlayerCredentials",
                columns: new[] { "GuildId", "RustServerId" });
        }
    }
}
