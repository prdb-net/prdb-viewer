using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodicLibraryScans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // When a Library Directory is next due a Library Scan nobody asked for. Null means
            // none is scheduled, which is what a Removed Library Directory keeps.
            migrationBuilder.AddColumn<DateTime>(
                name: "NextScanDueAt",
                table: "library_directory",
                type: "TEXT",
                nullable: true);

            // An installation that upgrades into periodic scanning has a history of Scans but no
            // schedule, and leaving the column null would mean it never scans again on its own.
            // The period is counted from the last Scan that actually finished, so a library
            // scanned an hour ago waits out the rest of its period while one last scanned a week
            // ago is due immediately. The interval is written as a literal because a migration
            // records what the rule was when it ran, not what the application decides today.
            migrationBuilder.Sql(
                """
                UPDATE library_directory
                SET NextScanDueAt = datetime(
                        COALESCE(
                            (SELECT MAX(scan.FinishedAt)
                             FROM background_work scan
                             WHERE scan.LibraryDirectoryId = library_directory.Id
                               AND scan.Category = 'LibraryScan'),
                            ActivatedAt),
                        '+6 hours')
                WHERE State = 'Active';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextScanDueAt",
                table: "library_directory");
        }
    }
}
