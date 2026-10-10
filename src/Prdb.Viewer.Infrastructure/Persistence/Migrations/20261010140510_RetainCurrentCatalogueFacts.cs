using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetainCurrentCatalogueFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "video_metadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteFactsJson",
                table: "video_metadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StashdbSceneId",
                table: "video_metadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountHandle",
                table: "site_directory_entry",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinksJson",
                table: "site_directory_entry",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlatformId",
                table: "site_directory_entry",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlatformKey",
                table: "site_directory_entry",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlatformTitle",
                table: "site_directory_entry",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "proposed_work",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SiteFactsJson",
                table: "proposed_work",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StashdbSceneId",
                table: "proposed_work",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "SiteFactsJson",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "StashdbSceneId",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "AccountHandle",
                table: "site_directory_entry");

            migrationBuilder.DropColumn(
                name: "LinksJson",
                table: "site_directory_entry");

            migrationBuilder.DropColumn(
                name: "PlatformId",
                table: "site_directory_entry");

            migrationBuilder.DropColumn(
                name: "PlatformKey",
                table: "site_directory_entry");

            migrationBuilder.DropColumn(
                name: "PlatformTitle",
                table: "site_directory_entry");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "proposed_work");

            migrationBuilder.DropColumn(
                name: "SiteFactsJson",
                table: "proposed_work");

            migrationBuilder.DropColumn(
                name: "StashdbSceneId",
                table: "proposed_work");
        }
    }
}
