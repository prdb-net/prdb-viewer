using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Drops every one-to-five Personal Rating and puts a Personal Reaction in its place. The data
    /// loss is deliberate and approved (ADR 0022): there is no mapping from stars to reactions, and
    /// inventing one would outlive the decision that made it. Every other Personal State column is
    /// untouched.
    /// </summary>
    public partial class ReplaceStarsWithReactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_personal_video_state_PersonalRating",
                table: "personal_video_state");

            migrationBuilder.DropColumn(
                name: "PersonalRating",
                table: "personal_video_state");

            migrationBuilder.AddColumn<string>(
                name: "Reaction",
                table: "personal_video_state",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reaction",
                table: "personal_video_state");

            migrationBuilder.AddColumn<int>(
                name: "PersonalRating",
                table: "personal_video_state",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_personal_video_state_PersonalRating",
                table: "personal_video_state",
                sql: "\"PersonalRating\" IS NULL OR \"PersonalRating\" BETWEEN 1 AND 5");
        }
    }
}
