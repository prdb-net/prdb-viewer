using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetainTheRestOfTheWorkFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationFileCount",
                table: "video_metadata",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationSpreadMilliseconds",
                table: "video_metadata",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NetworkTitle",
                table: "video_metadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NetworkUrl",
                table: "video_metadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QualityOverviewJson",
                table: "video_metadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReleaseNamesJson",
                table: "video_metadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "video_image",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrdbImageId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    PublicImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", nullable: true),
                    RetainedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_image", x => x.Id);
                    table.ForeignKey(
                        name: "FK_video_image_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_video_image_PublicImageId",
                table: "video_image",
                column: "PublicImageId");

            migrationBuilder.CreateIndex(
                name: "IX_video_image_State",
                table: "video_image",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_video_image_VideoId_PrdbImageId",
                table: "video_image",
                columns: new[] { "VideoId", "PrdbImageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "video_image");

            migrationBuilder.DropColumn(
                name: "DurationFileCount",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "DurationSpreadMilliseconds",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "NetworkTitle",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "NetworkUrl",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "QualityOverviewJson",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "ReleaseNamesJson",
                table: "video_metadata");
        }
    }
}
