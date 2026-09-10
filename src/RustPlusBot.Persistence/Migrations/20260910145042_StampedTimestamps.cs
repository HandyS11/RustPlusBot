using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustPlusBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StampedTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CreatedUtc",
                table: "VendingListingTracks",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "CreatedUtc",
                table: "VendingGridTracks",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "CreatedUtc",
                table: "SmartSwitches",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "CreatedUtc",
                table: "SmartStorageMonitors",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "CreatedUtc",
                table: "SmartAlarms",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "UpdatedUtc",
                table: "ClanPlayerNames",
                newName: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "VendingListingTracks",
                newName: "CreatedUtc");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "VendingGridTracks",
                newName: "CreatedUtc");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "SmartSwitches",
                newName: "CreatedUtc");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "SmartStorageMonitors",
                newName: "CreatedUtc");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "SmartAlarms",
                newName: "CreatedUtc");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "ClanPlayerNames",
                newName: "UpdatedUtc");
        }
    }
}
