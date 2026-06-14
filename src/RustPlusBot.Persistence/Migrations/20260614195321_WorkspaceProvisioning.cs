using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RustPlusBot.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WorkspaceProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelBindings");

            migrationBuilder.CreateTable(
                name: "ProvisionedCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    RustServerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DiscordCategoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisionedCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProvisionedCategories_RustServers_RustServerId",
                        column: x => x.RustServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProvisionedChannels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    RustServerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ChannelKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DiscordChannelId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisionedChannels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProvisionedChannels_RustServers_RustServerId",
                        column: x => x.RustServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProvisionedMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false),
                    RustServerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MessageKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DiscordChannelId = table.Column<long>(type: "INTEGER", nullable: false),
                    DiscordMessageId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisionedMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProvisionedMessages_RustServers_RustServerId",
                        column: x => x.RustServerId,
                        principalTable: "RustServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProvisionedCategories_GuildId_RustServerId",
                table: "ProvisionedCategories",
                columns: new[] { "GuildId", "RustServerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProvisionedCategories_RustServerId",
                table: "ProvisionedCategories",
                column: "RustServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisionedChannels_GuildId_RustServerId_ChannelKey",
                table: "ProvisionedChannels",
                columns: new[] { "GuildId", "RustServerId", "ChannelKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProvisionedChannels_RustServerId",
                table: "ProvisionedChannels",
                column: "RustServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisionedMessages_GuildId_RustServerId_MessageKey",
                table: "ProvisionedMessages",
                columns: new[] { "GuildId", "RustServerId", "MessageKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProvisionedMessages_RustServerId",
                table: "ProvisionedMessages",
                column: "RustServerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProvisionedCategories");

            migrationBuilder.DropTable(
                name: "ProvisionedChannels");

            migrationBuilder.DropTable(
                name: "ProvisionedMessages");

            migrationBuilder.CreateTable(
                name: "ChannelBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChannelId = table.Column<long>(type: "INTEGER", nullable: false),
                    Feature = table.Column<int>(type: "INTEGER", nullable: false),
                    GuildId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelBindings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelBindings_GuildId_Feature",
                table: "ChannelBindings",
                columns: new[] { "GuildId", "Feature" },
                unique: true);
        }
    }
}
