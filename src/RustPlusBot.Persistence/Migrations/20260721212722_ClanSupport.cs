using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustPlusBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClanSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClanPlayerNames",
                columns: table => new
                {
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SteamId = table.Column<long>(type: "INTEGER", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UpdatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanPlayerNames", x => new { x.ServerId, x.SteamId });
                    table.ForeignKey(
                        name: "FK_ClanPlayerNames_RustServers_ServerId",
                        column: x => x.ServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClanStates",
                columns: table => new
                {
                    ServerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    ClanId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Created = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Creator = table.Column<long>(type: "INTEGER", nullable: false),
                    Motd = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    MotdTimestamp = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    MotdAuthor = table.Column<long>(type: "INTEGER", nullable: true),
                    LogoHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Color = table.Column<int>(type: "INTEGER", nullable: true),
                    MaxMemberCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Score = table.Column<long>(type: "INTEGER", nullable: true),
                    RolesJson = table.Column<string>(type: "TEXT", nullable: false),
                    MembersJson = table.Column<string>(type: "TEXT", nullable: false),
                    InvitesJson = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeenUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClanStates", x => x.ServerId);
                    table.ForeignKey(
                        name: "FK_ClanStates_RustServers_ServerId",
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
                name: "ClanPlayerNames");

            migrationBuilder.DropTable(
                name: "ClanStates");
        }
    }
}
