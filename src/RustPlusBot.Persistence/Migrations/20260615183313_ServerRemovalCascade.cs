using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustPlusBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServerRemovalCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defensive: remove any ConnectionStates orphaned before this FK existed so adding the
            // constraint cannot fail. No server-removal path existed before 1b-iii, so this is normally a no-op.
            migrationBuilder.Sql(
                "DELETE FROM \"ConnectionStates\" WHERE \"RustServerId\" NOT IN (SELECT \"Id\" FROM \"RustServers\");");

            migrationBuilder.AddForeignKey(
                name: "FK_ConnectionStates_RustServers_RustServerId",
                table: "ConnectionStates",
                column: "RustServerId",
                principalTable: "RustServers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ConnectionStates_RustServers_RustServerId",
                table: "ConnectionStates");
        }
    }
}
