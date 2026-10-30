using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Khylo.Database.Providers.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistPlaybackStatesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlaylistPlaybackStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaylistId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrentItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PlaybackPositionTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    QueueStateJson = table.Column<string>(type: "TEXT", maxLength: 65536, nullable: true),
                    LastActive = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaylistPlaybackStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistPlaybackStates_PlaylistId_UserId",
                table: "PlaylistPlaybackStates",
                columns: new[] { "PlaylistId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistPlaybackStates_LastActive",
                table: "PlaylistPlaybackStates",
                column: "LastActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaylistPlaybackStates");
        }
    }
}
