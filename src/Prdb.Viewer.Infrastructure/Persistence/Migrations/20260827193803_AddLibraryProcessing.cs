using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "background_work",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    LibraryDirectoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConfigurationGeneration = table.Column<int>(type: "INTEGER", nullable: false),
                    LibraryScanId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PendingDirectoriesJson = table.Column<string>(type: "TEXT", nullable: true),
                    CoverageComplete = table.Column<bool>(type: "INTEGER", nullable: false),
                    FollowUpRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    DiscoveredCandidateCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    IssueCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_background_work", x => x.Id);
                    table.ForeignKey(
                        name: "FK_background_work_library_directory_LibraryDirectoryId",
                        column: x => x.LibraryDirectoryId,
                        principalTable: "library_directory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "video",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiscoveryDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "video_file_candidate",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryScanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryDirectoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    ObservedSize = table.Column<long>(type: "INTEGER", nullable: false),
                    ObservedLastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_file_candidate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "work_issue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BackgroundWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", nullable: false),
                    Cause = table.Column<string>(type: "TEXT", nullable: false),
                    RemediationOwner = table.Column<string>(type: "TEXT", nullable: false),
                    AffectedScope = table.Column<string>(type: "TEXT", nullable: false),
                    Impact = table.Column<string>(type: "TEXT", nullable: false),
                    RequiredAction = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_issue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_issue_background_work_BackgroundWorkId",
                        column: x => x.BackgroundWorkId,
                        principalTable: "background_work",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "video_file",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryDirectoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    LastWriteTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: false),
                    ContainerFormat = table.Column<string>(type: "TEXT", nullable: false),
                    VideoCodec = table.Column<string>(type: "TEXT", nullable: false),
                    AudioCodec = table.Column<string>(type: "TEXT", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    Availability = table.Column<string>(type: "TEXT", nullable: false),
                    LastObservedScanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConsecutiveCompleteAbsences = table.Column<int>(type: "INTEGER", nullable: false),
                    InspectedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_file", x => x.Id);
                    table.ForeignKey(
                        name: "FK_video_file_library_directory_LibraryDirectoryId",
                        column: x => x.LibraryDirectoryId,
                        principalTable: "library_directory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_video_file_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_background_work_Category_State_RequestedAt",
                table: "background_work",
                columns: new[] { "Category", "State", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_background_work_LibraryDirectoryId_Category_ConfigurationGeneration",
                table: "background_work",
                columns: new[] { "LibraryDirectoryId", "Category", "ConfigurationGeneration" });

            migrationBuilder.CreateIndex(
                name: "IX_video_DiscoveryDate",
                table: "video",
                column: "DiscoveryDate");

            migrationBuilder.CreateIndex(
                name: "IX_video_file_LibraryDirectoryId_RelativePath",
                table: "video_file",
                columns: new[] { "LibraryDirectoryId", "RelativePath" });

            migrationBuilder.CreateIndex(
                name: "IX_video_file_LibraryDirectoryId_Sha256",
                table: "video_file",
                columns: new[] { "LibraryDirectoryId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_video_file_VideoId",
                table: "video_file",
                column: "VideoId");

            migrationBuilder.CreateIndex(
                name: "IX_video_file_candidate_LibraryScanId_RelativePath",
                table: "video_file_candidate",
                columns: new[] { "LibraryScanId", "RelativePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_video_file_candidate_LibraryScanId_State",
                table: "video_file_candidate",
                columns: new[] { "LibraryScanId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_work_issue_BackgroundWorkId_ResolvedAt",
                table: "work_issue",
                columns: new[] { "BackgroundWorkId", "ResolvedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "video_file");

            migrationBuilder.DropTable(
                name: "video_file_candidate");

            migrationBuilder.DropTable(
                name: "work_issue");

            migrationBuilder.DropTable(
                name: "video");

            migrationBuilder.DropTable(
                name: "background_work");
        }
    }
}
