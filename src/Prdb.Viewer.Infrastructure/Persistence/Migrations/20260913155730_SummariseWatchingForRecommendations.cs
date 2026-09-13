using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the two facts the recommendation rules need and the application did not keep: the
    /// longest Uninterrupted Run within a Viewing Session, and how a session ended where anything
    /// was observed about it. Beside them go a summarised last-watched moment on the Personal Video
    /// State and the Browsing Visit's marks.
    ///
    /// The last-watched moment is backfilled from the Playback Attempts that are already retained,
    /// which is summarising evidence rather than inventing it. The runs and the departures are not
    /// backfilled and never will be: nothing recorded them, and a guess dressed as an observation
    /// is exactly what ADR 0022 forbids.
    /// </summary>
    public partial class SummariseWatchingForRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CurrentRunEndPositionMilliseconds",
                table: "playback_attempt",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CurrentRunMilliseconds",
                table: "playback_attempt",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "CurrentRunVideoFileId",
                table: "playback_attempt",
                type: "TEXT",
                nullable: true);

            // Every retained session ends up saying what is true of it: nothing was observed.
            migrationBuilder.AddColumn<string>(
                name: "Departure",
                table: "playback_attempt",
                type: "TEXT",
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<long>(
                name: "LongestUninterruptedRunMilliseconds",
                table: "playback_attempt",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastWatchedAt",
                table: "personal_video_state",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "browsing_visit_watch",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientContextKey = table.Column<string>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WatchedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_browsing_visit_watch", x => new { x.AccountId, x.ClientContextKey, x.VideoId });
                    table.ForeignKey(
                        name: "FK_browsing_visit_watch_account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_browsing_visit_watch_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_personal_video_state_AccountId_LastWatchedAt",
                table: "personal_video_state",
                columns: new[] { "AccountId", "LastWatchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_browsing_visit_watch_VideoId",
                table: "browsing_visit_watch",
                column: "VideoId");

            // The moment each Account last watched each Video, taken from the sessions that are
            // still there. An Account whose sessions predate this schema keeps a null, and the
            // recommender reads that as "prior watching happened, at a moment nobody recorded"
            // rather than as never watched.
            migrationBuilder.Sql(
                """
                UPDATE personal_video_state
                SET LastWatchedAt = (
                    SELECT MAX(LastActivityAt)
                    FROM playback_attempt
                    WHERE playback_attempt.AccountId = personal_video_state.AccountId
                      AND playback_attempt.VideoId = personal_video_state.VideoId
                      AND playback_attempt.LastActivityAt IS NOT NULL)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "browsing_visit_watch");

            migrationBuilder.DropIndex(
                name: "IX_personal_video_state_AccountId_LastWatchedAt",
                table: "personal_video_state");

            migrationBuilder.DropColumn(
                name: "CurrentRunEndPositionMilliseconds",
                table: "playback_attempt");

            migrationBuilder.DropColumn(
                name: "CurrentRunMilliseconds",
                table: "playback_attempt");

            migrationBuilder.DropColumn(
                name: "CurrentRunVideoFileId",
                table: "playback_attempt");

            migrationBuilder.DropColumn(
                name: "Departure",
                table: "playback_attempt");

            migrationBuilder.DropColumn(
                name: "LongestUninterruptedRunMilliseconds",
                table: "playback_attempt");

            migrationBuilder.DropColumn(
                name: "LastWatchedAt",
                table: "personal_video_state");
        }
    }
}
