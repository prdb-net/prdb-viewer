using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personal_video_state",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaybackProgressMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    ProgressVideoFileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AccumulatedWatchDurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    PlayCount = table.Column<int>(type: "INTEGER", nullable: false),
                    HasViewingCompletion = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastCompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlayState = table.Column<string>(type: "TEXT", nullable: false),
                    PlayStateChangedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastQualifiedActivityAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ContinueWatchingDismissedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FavouriteAddedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WatchLaterAddedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PersonalRating = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_video_state", x => new { x.AccountId, x.VideoId });
                    table.CheckConstraint("CK_personal_video_state_PersonalRating", "\"PersonalRating\" IS NULL OR \"PersonalRating\" BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_personal_video_state_account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_personal_video_state_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "playback_attempt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ViewingSessionBeganAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastActivityAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastReportSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    LastPositionMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    ActiveWatchDurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    Qualified = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompletionRecorded = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playback_attempt", x => x.Id);
                    table.ForeignKey(
                        name: "FK_playback_attempt_account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_playback_attempt_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "playback_attempt_video_file",
                columns: table => new
                {
                    PlaybackAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoFileId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playback_attempt_video_file", x => new { x.PlaybackAttemptId, x.VideoFileId });
                    table.ForeignKey(
                        name: "FK_playback_attempt_video_file_playback_attempt_PlaybackAttemptId",
                        column: x => x.PlaybackAttemptId,
                        principalTable: "playback_attempt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_playback_attempt_video_file_video_file_VideoFileId",
                        column: x => x.VideoFileId,
                        principalTable: "video_file",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "playback_report",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlaybackAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    PositionMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    ActiveWatchingMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    ActivityStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActivityEndedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playback_report", x => x.Id);
                    table.ForeignKey(
                        name: "FK_playback_report_playback_attempt_PlaybackAttemptId",
                        column: x => x.PlaybackAttemptId,
                        principalTable: "playback_attempt",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_personal_video_state_AccountId_FavouriteAddedAt",
                table: "personal_video_state",
                columns: new[] { "AccountId", "FavouriteAddedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_personal_video_state_AccountId_LastQualifiedActivityAt",
                table: "personal_video_state",
                columns: new[] { "AccountId", "LastQualifiedActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_personal_video_state_AccountId_WatchLaterAddedAt",
                table: "personal_video_state",
                columns: new[] { "AccountId", "WatchLaterAddedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_personal_video_state_VideoId",
                table: "personal_video_state",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_playback_attempt_AccountId_EndedAt_LastActivityAt",
                table: "playback_attempt",
                columns: new[] { "AccountId", "EndedAt", "LastActivityAt" });

            migrationBuilder.CreateIndex(
                name: "IX_playback_attempt_AccountId_VideoId_AttemptedAt",
                table: "playback_attempt",
                columns: new[] { "AccountId", "VideoId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_playback_attempt_VideoId",
                table: "playback_attempt",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_playback_attempt_video_file_VideoFileId",
                table: "playback_attempt_video_file",
                column: "VideoFileId");

            migrationBuilder.CreateIndex(
                name: "IX_playback_report_ActivityStartedAt_ActivityEndedAt",
                table: "playback_report",
                columns: new[] { "ActivityStartedAt", "ActivityEndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_playback_report_PlaybackAttemptId_Sequence",
                table: "playback_report",
                columns: new[] { "PlaybackAttemptId", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "personal_video_state");

            migrationBuilder.DropTable(
                name: "playback_attempt_video_file");

            migrationBuilder.DropTable(
                name: "playback_report");

            migrationBuilder.DropTable(
                name: "playback_attempt");
        }
    }
}
