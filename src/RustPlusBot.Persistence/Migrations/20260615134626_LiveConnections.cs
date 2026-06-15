using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustPlusBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LiveConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsHealthy",
                table: "ConnectionStates",
                newName: "Status");

            migrationBuilder.AddColumn<int>(
                name: "PlayerCount",
                table: "ConnectionStates",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlayerCount",
                table: "ConnectionStates");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "ConnectionStates",
                newName: "IsHealthy");
        }
    }
}
