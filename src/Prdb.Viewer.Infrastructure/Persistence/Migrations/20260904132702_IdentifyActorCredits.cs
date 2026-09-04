using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentifyActorCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrdbActorId",
                table: "video_actor",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_video_actor_PrdbActorId",
                table: "video_actor",
                column: "PrdbActorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_video_actor_PrdbActorId",
                table: "video_actor");

            migrationBuilder.DropColumn(
                name: "PrdbActorId",
                table: "video_actor");
        }
    }
}
