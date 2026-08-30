using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Video Quality: the highest band among a Video's Available occurrences. It is stored
            // as its ordinal rather than as a name, because discovery orders by it.
            migrationBuilder.AddColumn<int>(
                name: "Quality",
                table: "video",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Unknown is what the default leaves behind, and it is also a real answer, so an
            // existing library cannot be told apart from one that was never inspected. Every
            // projection is therefore rebuilt on the next start.
            migrationBuilder.Sql("UPDATE video SET ProjectedAt = NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_video_SurvivingVideoId_Availability_BestClassification_Quality",
                table: "video",
                columns: new[] { "SurvivingVideoId", "Availability", "BestClassification", "Quality" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_video_SurvivingVideoId_Availability_BestClassification_Quality",
                table: "video");

            migrationBuilder.DropColumn(
                name: "Quality",
                table: "video");
        }
    }
}
