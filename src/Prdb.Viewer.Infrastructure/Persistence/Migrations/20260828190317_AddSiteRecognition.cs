using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteRecognition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SiteRecognisedAt",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteRecognisedPath",
                table: "video_file",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SiteDirectoryFetchedAt",
                table: "installation_configuration",
                type: "TEXT",
                nullable: true);

            // Every candidate that exists before this migration was proposed by the remote ladder,
            // which is the only source that could have created one.
            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "identification_candidate",
                type: "TEXT",
                nullable: false,
                defaultValue: "PrdbIdentification");

            migrationBuilder.CreateTable(
                name: "site_directory_entry",
                columns: table => new
                {
                    SiteKey = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_directory_entry", x => x.SiteKey);
                });

            migrationBuilder.UpdateData(
                table: "installation_configuration",
                keyColumn: "Id",
                keyValue: 1,
                column: "SiteDirectoryFetchedAt",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_video_file_LibraryDirectoryId_Availability_SiteRecognisedPath",
                table: "video_file",
                columns: new[] { "LibraryDirectoryId", "Availability", "SiteRecognisedPath" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "site_directory_entry");

            migrationBuilder.DropIndex(
                name: "IX_video_file_LibraryDirectoryId_Availability_SiteRecognisedPath",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "SiteRecognisedAt",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "SiteRecognisedPath",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "SiteDirectoryFetchedAt",
                table: "installation_configuration");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "identification_candidate");
        }
    }
}
