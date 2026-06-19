using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustPlusBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FacepunchServerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FacepunchServerId",
                table: "RustServers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RustServers_FacepunchServerId",
                table: "RustServers",
                column: "FacepunchServerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RustServers_FacepunchServerId",
                table: "RustServers");

            migrationBuilder.DropColumn(
                name: "FacepunchServerId",
                table: "RustServers");
        }
    }
}
