using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Availability",
                table: "video",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DisplayLabel",
                table: "video",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EstablishedSite",
                table: "video",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasEstablishedWork",
                table: "video",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProjectedAt",
                table: "video",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Readiness",
                table: "video",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ReviewNeeded",
                table: "video",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "video",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "video_actor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VideoId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_actor", x => x.Id);
                    table.ForeignKey(
                        name: "FK_video_actor_video_VideoId",
                        column: x => x.VideoId,
                        principalTable: "video",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_video_ProjectedAt",
                table: "video",
                column: "ProjectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_video_SurvivingVideoId_Availability_Readiness_DiscoveryDate",
                table: "video",
                columns: new[] { "SurvivingVideoId", "Availability", "Readiness", "DiscoveryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_video_SurvivingVideoId_Availability_Readiness_DisplayLabel",
                table: "video",
                columns: new[] { "SurvivingVideoId", "Availability", "Readiness", "DisplayLabel" });

            migrationBuilder.CreateIndex(
                name: "IX_video_actor_NormalizedName",
                table: "video_actor",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_video_actor_VideoId_NormalizedName",
                table: "video_actor",
                columns: new[] { "VideoId", "NormalizedName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "video_actor");

            migrationBuilder.DropIndex(
                name: "IX_video_ProjectedAt",
                table: "video");

            migrationBuilder.DropIndex(
                name: "IX_video_SurvivingVideoId_Availability_Readiness_DiscoveryDate",
                table: "video");

            migrationBuilder.DropIndex(
                name: "IX_video_SurvivingVideoId_Availability_Readiness_DisplayLabel",
                table: "video");

            migrationBuilder.DropColumn(
                name: "Availability",
                table: "video");

            migrationBuilder.DropColumn(
                name: "DisplayLabel",
                table: "video");

            migrationBuilder.DropColumn(
                name: "EstablishedSite",
                table: "video");

            migrationBuilder.DropColumn(
                name: "HasEstablishedWork",
                table: "video");

            migrationBuilder.DropColumn(
                name: "ProjectedAt",
                table: "video");

            migrationBuilder.DropColumn(
                name: "Readiness",
                table: "video");

            migrationBuilder.DropColumn(
                name: "ReviewNeeded",
                table: "video");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "video");
        }
    }
}
