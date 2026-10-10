using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMd5Identification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Md5",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Md5AttemptedSha256",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Md5FailureReason",
                table: "video_file",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Md5",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "Md5AttemptedSha256",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "Md5FailureReason",
                table: "video_file");
        }
    }
}
