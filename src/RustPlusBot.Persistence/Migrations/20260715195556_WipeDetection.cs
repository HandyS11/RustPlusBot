using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustPlusBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WipeDetection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastMapSeed",
                table: "RustServers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastMapSize",
                table: "RustServers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastWipeTimeUtc",
                table: "RustServers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PingEveryoneOnWipe",
                table: "GuildSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastMapSeed",
                table: "RustServers");

            migrationBuilder.DropColumn(
                name: "LastMapSize",
                table: "RustServers");

            migrationBuilder.DropColumn(
                name: "LastWipeTimeUtc",
                table: "RustServers");

            migrationBuilder.DropColumn(
                name: "PingEveryoneOnWipe",
                table: "GuildSettings");
        }
    }
}
