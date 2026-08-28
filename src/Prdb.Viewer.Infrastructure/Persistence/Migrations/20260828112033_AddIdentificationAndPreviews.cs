using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentificationAndPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HashFailureReason",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HashState",
                table: "video_file",
                type: "TEXT",
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTime>(
                name: "HashedAt",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HashedSha256",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "IdentifiedAt",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentifiedSha256",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsHash",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PerceptualHash",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PreviewGeneratedAt",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewRelativePath",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewSha256",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviewState",
                table: "video_file",
                type: "TEXT",
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousVideoId",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PublicPreviewId",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CaseVersion",
                table: "video",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "MergedAt",
                table: "video",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SurvivingVideoId",
                table: "video",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "background_work",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaitingReason",
                table: "background_work",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "identification_candidate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Dimension = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TargetKey = table.Column<string>(type: "TEXT", nullable: false),
                    TargetTitle = table.Column<string>(type: "TEXT", nullable: false),
                    TargetUrl = table.Column<string>(type: "TEXT", nullable: true),
                    EvidenceClass = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    MatchedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Confidence = table.Column<string>(type: "TEXT", nullable: true),
                    EvidenceKey = table.Column<string>(type: "TEXT", nullable: false),
                    SupportingVideoFileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PriorRejectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identification_candidate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_identification_candidate_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identification_claim",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Dimension = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TargetKey = table.Column<string>(type: "TEXT", nullable: false),
                    TargetTitle = table.Column<string>(type: "TEXT", nullable: false),
                    TargetUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    EvidenceClass = table.Column<string>(type: "TEXT", nullable: false),
                    MatchedBy = table.Column<string>(type: "TEXT", nullable: true),
                    IsAdministrativeOverride = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportingVideoFileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    EstablishedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identification_claim", x => x.Id);
                    table.ForeignKey(
                        name: "FK_identification_claim_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identification_decision",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Dimension = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    DecidedByAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CandidateId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetKey = table.Column<string>(type: "TEXT", nullable: true),
                    PriorState = table.Column<string>(type: "TEXT", nullable: false),
                    ResultingState = table.Column<string>(type: "TEXT", nullable: false),
                    MergedAnotherVideo = table.Column<bool>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_identification_decision", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "video_metadata",
                columns: table => new
                {
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrdbVideoId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    SiteId = table.Column<string>(type: "TEXT", nullable: true),
                    SiteTitle = table.Column<string>(type: "TEXT", nullable: true),
                    SiteUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ActorsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ArtworkUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_metadata", x => x.VideoId);
                    table.ForeignKey(
                        name: "FK_video_metadata_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_video_file_LibraryDirectoryId_Availability_HashState",
                table: "video_file",
                columns: new[] { "LibraryDirectoryId", "Availability", "HashState" });

            migrationBuilder.CreateIndex(
                name: "IX_video_file_LibraryDirectoryId_Availability_PreviewState",
                table: "video_file",
                columns: new[] { "LibraryDirectoryId", "Availability", "PreviewState" });

            migrationBuilder.CreateIndex(
                name: "IX_video_file_PublicPreviewId",
                table: "video_file",
                column: "PublicPreviewId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_video_SurvivingVideoId",
                table: "video",
                column: "SurvivingVideoId");

            migrationBuilder.CreateIndex(
                name: "IX_identification_candidate_Status_EvidenceClass_CreatedAt",
                table: "identification_candidate",
                columns: new[] { "Status", "EvidenceClass", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_identification_candidate_VideoId_Dimension_Status",
                table: "identification_candidate",
                columns: new[] { "VideoId", "Dimension", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_identification_candidate_VideoId_Dimension_TargetKey_EvidenceKey",
                table: "identification_candidate",
                columns: new[] { "VideoId", "Dimension", "TargetKey", "EvidenceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_identification_claim_Dimension_TargetKey_Status",
                table: "identification_claim",
                columns: new[] { "Dimension", "TargetKey", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_identification_claim_VideoId_Dimension_Status",
                table: "identification_claim",
                columns: new[] { "VideoId", "Dimension", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_identification_decision_VideoId_CreatedAt",
                table: "identification_decision",
                columns: new[] { "VideoId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_video_metadata_PrdbVideoId",
                table: "video_metadata",
                column: "PrdbVideoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identification_candidate");

            migrationBuilder.DropTable(
                name: "identification_claim");

            migrationBuilder.DropTable(
                name: "identification_decision");

            migrationBuilder.DropTable(
                name: "video_metadata");

            migrationBuilder.DropIndex(
                name: "IX_video_file_LibraryDirectoryId_Availability_HashState",
                table: "video_file");

            migrationBuilder.DropIndex(
                name: "IX_video_file_LibraryDirectoryId_Availability_PreviewState",
                table: "video_file");

            migrationBuilder.DropIndex(
                name: "IX_video_file_PublicPreviewId",
                table: "video_file");

            migrationBuilder.DropIndex(
                name: "IX_video_SurvivingVideoId",
                table: "video");

            migrationBuilder.DropColumn(
                name: "HashFailureReason",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "HashState",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "HashedAt",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "HashedSha256",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "IdentifiedAt",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "IdentifiedSha256",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "OsHash",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "PerceptualHash",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "PreviewGeneratedAt",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "PreviewRelativePath",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "PreviewSha256",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "PreviewState",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "PreviousVideoId",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "PublicPreviewId",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "CaseVersion",
                table: "video");

            migrationBuilder.DropColumn(
                name: "MergedAt",
                table: "video");

            migrationBuilder.DropColumn(
                name: "SurvivingVideoId",
                table: "video");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "background_work");

            migrationBuilder.DropColumn(
                name: "WaitingReason",
                table: "background_work");
        }
    }
}
