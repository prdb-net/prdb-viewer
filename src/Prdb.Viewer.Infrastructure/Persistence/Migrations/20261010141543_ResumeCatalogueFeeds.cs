using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResumeCatalogueFeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "site_directory_entry",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CatalogueFeedStates",
                columns: table => new
                {
                    Feed = table.Column<string>(type: "TEXT", nullable: false),
                    Since = table.Column<string>(type: "TEXT", nullable: false),
                    SinceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    BaselineComplete = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastAppliedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogueFeedStates", x => x.Feed);
                });

            migrationBuilder.CreateTable(
                name: "SiteDirectoryStaging",
                columns: table => new
                {
                    SiteKey = table.Column<string>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    FactsJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SiteDirectoryStaging", x => x.SiteKey);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogueFeedStates");

            migrationBuilder.DropTable(
                name: "SiteDirectoryStaging");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "site_directory_entry");
        }
    }
}
