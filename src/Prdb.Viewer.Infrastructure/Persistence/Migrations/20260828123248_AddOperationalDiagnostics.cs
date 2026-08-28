using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Work Issues are operational detail derived from durable state, and this migration
            // gives every one of them a stable reference, an aggregation key, and a message
            // contract that an existing row cannot supply. They are dropped rather than filled
            // with placeholders; the lanes re-derive whichever obstacles still apply.
            migrationBuilder.Sql("DELETE FROM work_issue;");

            migrationBuilder.AddColumn<int>(
                name: "AffectedItemCount",
                table: "work_issue",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AggregationKey",
                table: "work_issue",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AttemptedRetries",
                table: "work_issue",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "work_issue",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ConfigurationGeneration",
                table: "work_issue",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ContainerPath",
                table: "work_issue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Detail",
                table: "work_issue",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExpectedResolutionEvidence",
                table: "work_issue",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstOccurredAt",
                table: "work_issue",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "LastOccurredAt",
                table: "work_issue",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "LibraryDirectoryId",
                table: "work_issue",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "work_issue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OccurrenceCount",
                table: "work_issue",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "work_issue",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousOccurrenceId",
                table: "work_issue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                table: "work_issue",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResolutionEvidence",
                table: "work_issue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetryDisposition",
                table: "work_issue",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SafeCause",
                table: "work_issue",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Summary",
                table: "work_issue",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "work_issue",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "VideoFileId",
                table: "work_issue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VideoId",
                table: "work_issue",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BackgroundWorkPaused",
                table: "installation_configuration",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "BackgroundWorkPausedAt",
                table: "installation_configuration",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CancellationRequested",
                table: "background_work",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivityAt",
                table: "background_work",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "background_work",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SkippedItemCount",
                table: "background_work",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StateBeforePause",
                table: "background_work",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Trigger",
                table: "background_work",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "work_issue_item",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkIssueId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    ContainerPath = table.Column<string>(type: "TEXT", nullable: true),
                    VideoFileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OccurrenceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstOccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastOccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_issue_item", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_issue_item_work_issue_WorkIssueId",
                        column: x => x.WorkIssueId,
                        principalTable: "work_issue",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "installation_configuration",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "BackgroundWorkPaused", "BackgroundWorkPausedAt" },
                values: new object[] { false, null });

            migrationBuilder.CreateIndex(
                name: "IX_work_issue_AggregationKey_ResolvedAt",
                table: "work_issue",
                columns: new[] { "AggregationKey", "ResolvedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_work_issue_Reference",
                table: "work_issue",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_issue_ResolvedAt_Severity_LastOccurredAt",
                table: "work_issue",
                columns: new[] { "ResolvedAt", "Severity", "LastOccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_work_issue_item_WorkIssueId_Scope",
                table: "work_issue_item",
                columns: new[] { "WorkIssueId", "Scope" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_issue_item");

            migrationBuilder.DropIndex(
                name: "IX_work_issue_AggregationKey_ResolvedAt",
                table: "work_issue");

            migrationBuilder.DropIndex(
                name: "IX_work_issue_Reference",
                table: "work_issue");

            migrationBuilder.DropIndex(
                name: "IX_work_issue_ResolvedAt_Severity_LastOccurredAt",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "AffectedItemCount",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "AggregationKey",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "AttemptedRetries",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "ConfigurationGeneration",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "ContainerPath",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "Detail",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "ExpectedResolutionEvidence",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "FirstOccurredAt",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "LastOccurredAt",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "LibraryDirectoryId",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "OccurrenceCount",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "PreviousOccurrenceId",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "Reference",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "ResolutionEvidence",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "RetryDisposition",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "SafeCause",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "Summary",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "VideoFileId",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "VideoId",
                table: "work_issue");

            migrationBuilder.DropColumn(
                name: "BackgroundWorkPaused",
                table: "installation_configuration");

            migrationBuilder.DropColumn(
                name: "BackgroundWorkPausedAt",
                table: "installation_configuration");

            migrationBuilder.DropColumn(
                name: "CancellationRequested",
                table: "background_work");

            migrationBuilder.DropColumn(
                name: "LastActivityAt",
                table: "background_work");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "background_work");

            migrationBuilder.DropColumn(
                name: "SkippedItemCount",
                table: "background_work");

            migrationBuilder.DropColumn(
                name: "StateBeforePause",
                table: "background_work");

            migrationBuilder.DropColumn(
                name: "Trigger",
                table: "background_work");
        }
    }
}
