using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefreshLocallyRetainedCatalogueWorks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CatalogueRepairGeneration",
                table: "video_metadata",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemoteDeletedAt",
                table: "video_metadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteMergedIntoId",
                table: "video_metadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemoteObservedAt",
                table: "video_metadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemoteState",
                table: "video_metadata",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemoteUpdatedAt",
                table: "video_metadata",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CatalogueRepairGeneration",
                table: "proposed_work",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemoteDeletedAt",
                table: "proposed_work",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemoteMergedIntoId",
                table: "proposed_work",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemoteObservedAt",
                table: "proposed_work",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemoteState",
                table: "proposed_work",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RemoteUpdatedAt",
                table: "proposed_work",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorityFingerprint",
                table: "CatalogueFeedStates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BurstPages",
                table: "CatalogueFeedStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "CatchUpPending",
                table: "CatalogueFeedStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "CatalogueFeedStates",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RepairGeneration",
                table: "CatalogueFeedStates",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ServerTime",
                table: "CatalogueFeedStates",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CatalogueRepairGeneration",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "RemoteDeletedAt",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "RemoteMergedIntoId",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "RemoteObservedAt",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "RemoteState",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "RemoteUpdatedAt",
                table: "video_metadata");

            migrationBuilder.DropColumn(
                name: "CatalogueRepairGeneration",
                table: "proposed_work");

            migrationBuilder.DropColumn(
                name: "RemoteDeletedAt",
                table: "proposed_work");

            migrationBuilder.DropColumn(
                name: "RemoteMergedIntoId",
                table: "proposed_work");

            migrationBuilder.DropColumn(
                name: "RemoteObservedAt",
                table: "proposed_work");

            migrationBuilder.DropColumn(
                name: "RemoteState",
                table: "proposed_work");

            migrationBuilder.DropColumn(
                name: "RemoteUpdatedAt",
                table: "proposed_work");

            migrationBuilder.DropColumn(
                name: "AuthorityFingerprint",
                table: "CatalogueFeedStates");

            migrationBuilder.DropColumn(
                name: "BurstPages",
                table: "CatalogueFeedStates");

            migrationBuilder.DropColumn(
                name: "CatchUpPending",
                table: "CatalogueFeedStates");

            migrationBuilder.DropColumn(
                name: "Phase",
                table: "CatalogueFeedStates");

            migrationBuilder.DropColumn(
                name: "RepairGeneration",
                table: "CatalogueFeedStates");

            migrationBuilder.DropColumn(
                name: "ServerTime",
                table: "CatalogueFeedStates");
        }
    }
}
