using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetainProposedWorkFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProposedWorkId",
                table: "identification_candidate",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "proposed_work",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrdbVideoId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    SiteTitle = table.Column<string>(type: "TEXT", nullable: true),
                    SiteUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ActorsJson = table.Column<string>(type: "TEXT", nullable: true),
                    ArtworkUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ArtworkState = table.Column<string>(type: "TEXT", nullable: false),
                    PublicArtworkId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ArtworkRelativePath = table.Column<string>(type: "TEXT", nullable: true),
                    ArtworkContentType = table.Column<string>(type: "TEXT", nullable: true),
                    ArtworkRetainedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proposed_work", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_identification_candidate_ProposedWorkId",
                table: "identification_candidate",
                column: "ProposedWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_proposed_work_ArtworkState",
                table: "proposed_work",
                column: "ArtworkState");

            migrationBuilder.CreateIndex(
                name: "IX_proposed_work_PrdbVideoId",
                table: "proposed_work",
                column: "PrdbVideoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_proposed_work_PublicArtworkId",
                table: "proposed_work",
                column: "PublicArtworkId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_identification_candidate_proposed_work_ProposedWorkId",
                table: "identification_candidate",
                column: "ProposedWorkId",
                principalTable: "proposed_work",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_identification_candidate_proposed_work_ProposedWorkId",
                table: "identification_candidate");

            migrationBuilder.DropTable(
                name: "proposed_work");

            migrationBuilder.DropIndex(
                name: "IX_identification_candidate_ProposedWorkId",
                table: "identification_candidate");

            migrationBuilder.DropColumn(
                name: "ProposedWorkId",
                table: "identification_candidate");
        }
    }
}
