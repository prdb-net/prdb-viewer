using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountPlaylists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "playlist",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playlist", x => x.Id);
                    table.ForeignKey(
                        name: "FK_playlist_account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "playlist_entry",
                columns: table => new
                {
                    PlaylistId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playlist_entry", x => new { x.PlaylistId, x.VideoId });
                    table.ForeignKey(
                        name: "FK_playlist_entry_playlist_PlaylistId",
                        column: x => x.PlaylistId,
                        principalTable: "playlist",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_playlist_entry_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_playlist_AccountId_Name",
                table: "playlist",
                columns: new[] { "AccountId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_playlist_entry_PlaylistId_Position",
                table: "playlist_entry",
                columns: new[] { "PlaylistId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_playlist_entry_VideoId",
                table: "playlist_entry",
                column: "VideoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "playlist_entry");

            migrationBuilder.DropTable(
                name: "playlist");
        }
    }
}
