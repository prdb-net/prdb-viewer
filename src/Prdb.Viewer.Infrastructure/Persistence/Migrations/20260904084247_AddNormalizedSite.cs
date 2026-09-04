using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Established Site normalised for comparison, so searching a facet for a Site can
            // ignore case, diacritics and punctuation the way the Library's own search does. It
            // carries no index: the search asks whether a term appears anywhere in the name, and
            // no index answers that.
            migrationBuilder.AddColumn<string>(
                name: "NormalizedSite",
                table: "video",
                type: "TEXT",
                nullable: true);

            // Null is what the new column leaves behind, and it is also what an Unknown Site
            // stores, so an existing library cannot be told apart from one that has no Site. The
            // normalisation is C# rather than SQL — it folds diacritics — so every projection is
            // rebuilt on the next start instead of being filled in from here.
            migrationBuilder.Sql("UPDATE video SET ProjectedAt = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NormalizedSite",
                table: "video");
        }
    }
}
