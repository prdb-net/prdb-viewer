using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssociateVideosFromLocalSimilarity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "work_association",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OtherVideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OtherVideoFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Distance = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationsAgree = table.Column<bool>(type: "INTEGER", nullable: false),
                    DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    OtherDurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    DecidedByAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EstablishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_association", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_association_video_OtherVideoId",
                        column: x => x.OtherVideoId,
                        principalTable: "video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_association_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_work_association_OtherVideoId_Status",
                table: "work_association",
                columns: new[] { "OtherVideoId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_work_association_Status",
                table: "work_association",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_work_association_VideoFileId_OtherVideoFileId",
                table: "work_association",
                columns: new[] { "VideoFileId", "OtherVideoFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_association_VideoId_Status",
                table: "work_association",
                columns: new[] { "VideoId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_association");
        }
    }
}
