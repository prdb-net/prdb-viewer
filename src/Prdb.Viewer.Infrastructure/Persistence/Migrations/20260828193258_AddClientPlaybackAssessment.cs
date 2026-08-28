using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientPlaybackAssessment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_video_file_VideoId",
                table: "video_file");

            // The projected column changes meaning as well as name: it stopped being a client
            // conclusion the installation had no business drawing and became the best
            // Direct-Play Classification among a Video's Available occurrences. Its old values do
            // not translate, so the column is replaced and every projection rebuilt.
            migrationBuilder.DropColumn(
                name: "Readiness",
                table: "video");

            migrationBuilder.AddColumn<string>(
                name: "BestClassification",
                table: "video",
                type: "TEXT",
                nullable: false,
                defaultValue: "Unsupported");

            migrationBuilder.Sql("UPDATE video SET ProjectedAt = NULL;");

            migrationBuilder.DropIndex(
                name: "IX_video_SurvivingVideoId_Availability_Readiness_DisplayLabel",
                table: "video");

            migrationBuilder.DropIndex(
                name: "IX_video_SurvivingVideoId_Availability_Readiness_DiscoveryDate",
                table: "video");

            migrationBuilder.CreateIndex(
                name: "IX_video_SurvivingVideoId_Availability_BestClassification_DisplayLabel",
                table: "video",
                columns: new[] { "SurvivingVideoId", "Availability", "BestClassification", "DisplayLabel" });

            migrationBuilder.CreateIndex(
                name: "IX_video_SurvivingVideoId_Availability_BestClassification_DiscoveryDate",
                table: "video",
                columns: new[] { "SurvivingVideoId", "Availability", "BestClassification", "DiscoveryDate" });

            migrationBuilder.AddColumn<long>(
                name: "AudioBitrate",
                table: "video_file",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudioChannels",
                table: "video_file",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudioSampleRate",
                table: "video_file",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BitDepth",
                table: "video_file",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FrameRate",
                table: "video_file",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfileKey",
                table: "video_file",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "VideoBitrate",
                table: "video_file",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VideoLevel",
                table: "video_file",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VideoProfile",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "client_playback_assessment",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientContextKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProfileKey = table.Column<string>(type: "TEXT", nullable: false),
                    Verdict = table.Column<string>(type: "TEXT", nullable: false),
                    Smooth = table.Column<bool>(type: "INTEGER", nullable: true),
                    PowerEfficient = table.Column<bool>(type: "INTEGER", nullable: true),
                    Method = table.Column<string>(type: "TEXT", nullable: false),
                    AssessedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_client_playback_assessment", x => new { x.AccountId, x.ClientContextKey, x.ProfileKey });
                    table.ForeignKey(
                        name: "FK_client_playback_assessment_account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "observed_playback_outcome",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClientContextKey = table.Column<string>(type: "TEXT", nullable: false),
                    VideoFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ContentSha256 = table.Column<string>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    FailureCategory = table.Column<string>(type: "TEXT", nullable: true),
                    ObservedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_observed_playback_outcome", x => new { x.AccountId, x.ClientContextKey, x.VideoFileId });
                    table.ForeignKey(
                        name: "FK_observed_playback_outcome_account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_observed_playback_outcome_video_file_VideoFileId",
                        column: x => x.VideoFileId,
                        principalTable: "video_file",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_video_file_ProfileKey",
                table: "video_file",
                column: "ProfileKey");

            migrationBuilder.CreateIndex(
                name: "IX_video_file_VideoId_Availability_DirectPlayClassification_ProfileKey",
                table: "video_file",
                columns: new[] { "VideoId", "Availability", "DirectPlayClassification", "ProfileKey" });

            migrationBuilder.CreateIndex(
                name: "IX_observed_playback_outcome_VideoFileId",
                table: "observed_playback_outcome",
                column: "VideoFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "client_playback_assessment");

            migrationBuilder.DropTable(
                name: "observed_playback_outcome");

            migrationBuilder.DropIndex(
                name: "IX_video_file_ProfileKey",
                table: "video_file");

            migrationBuilder.DropIndex(
                name: "IX_video_file_VideoId_Availability_DirectPlayClassification_ProfileKey",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "AudioBitrate",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "AudioChannels",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "AudioSampleRate",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "BitDepth",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "FrameRate",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "ProfileKey",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "VideoBitrate",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "VideoLevel",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "VideoProfile",
                table: "video_file");

            migrationBuilder.RenameColumn(
                name: "BestClassification",
                table: "video",
                newName: "Readiness");

            migrationBuilder.RenameIndex(
                name: "IX_video_SurvivingVideoId_Availability_BestClassification_DisplayLabel",
                table: "video",
                newName: "IX_video_SurvivingVideoId_Availability_Readiness_DisplayLabel");

            migrationBuilder.RenameIndex(
                name: "IX_video_SurvivingVideoId_Availability_BestClassification_DiscoveryDate",
                table: "video",
                newName: "IX_video_SurvivingVideoId_Availability_Readiness_DiscoveryDate");

            migrationBuilder.CreateIndex(
                name: "IX_video_file_VideoId",
                table: "video_file",
                column: "VideoId");
        }
    }
}
