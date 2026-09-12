using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FindPerceptualNeighbourhoods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NeighbourhoodComparedAt",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NeighbourhoodComparedHash",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "perceptual_neighbourhood",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeftVideoFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RightVideoFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Distance = table.Column<int>(type: "INTEGER", nullable: false),
                    LeftPerceptualHash = table.Column<string>(type: "TEXT", nullable: false),
                    RightPerceptualHash = table.Column<string>(type: "TEXT", nullable: false),
                    LeftDurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    RightDurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationsAgree = table.Column<bool>(type: "INTEGER", nullable: false),
                    EstablishedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_perceptual_neighbourhood", x => x.Id);
                    table.ForeignKey(
                        name: "FK_perceptual_neighbourhood_video_file_LeftVideoFileId",
                        column: x => x.LeftVideoFileId,
                        principalTable: "video_file",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_perceptual_neighbourhood_video_file_RightVideoFileId",
                        column: x => x.RightVideoFileId,
                        principalTable: "video_file",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_video_file_LibraryDirectoryId_Availability_NeighbourhoodComparedHash",
                table: "video_file",
                columns: new[] { "LibraryDirectoryId", "Availability", "NeighbourhoodComparedHash" });

            migrationBuilder.CreateIndex(
                name: "IX_perceptual_neighbourhood_LeftVideoFileId_Distance",
                table: "perceptual_neighbourhood",
                columns: new[] { "LeftVideoFileId", "Distance" });

            migrationBuilder.CreateIndex(
                name: "IX_perceptual_neighbourhood_LeftVideoFileId_RightVideoFileId",
                table: "perceptual_neighbourhood",
                columns: new[] { "LeftVideoFileId", "RightVideoFileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_perceptual_neighbourhood_RightVideoFileId_Distance",
                table: "perceptual_neighbourhood",
                columns: new[] { "RightVideoFileId", "Distance" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "perceptual_neighbourhood");

            migrationBuilder.DropIndex(
                name: "IX_video_file_LibraryDirectoryId_Availability_NeighbourhoodComparedHash",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "NeighbourhoodComparedAt",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "NeighbourhoodComparedHash",
                table: "video_file");
        }
    }
}
