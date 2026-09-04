using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetainActorProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "actor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrdbActorId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    ProfileState = table.Column<string>(type: "TEXT", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    GenderLabel = table.Column<string>(type: "TEXT", nullable: true),
                    Birthday = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BirthdayPrecisionLabel = table.Column<string>(type: "TEXT", nullable: true),
                    Deathday = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Birthplace = table.Column<string>(type: "TEXT", nullable: true),
                    HaircolourLabel = table.Column<string>(type: "TEXT", nullable: true),
                    EyecolourLabel = table.Column<string>(type: "TEXT", nullable: true),
                    BreastTypeLabel = table.Column<string>(type: "TEXT", nullable: true),
                    HeightCentimetres = table.Column<int>(type: "INTEGER", nullable: true),
                    BraSizeLabel = table.Column<string>(type: "TEXT", nullable: true),
                    WaistCentimetres = table.Column<int>(type: "INTEGER", nullable: true),
                    HipCentimetres = table.Column<int>(type: "INTEGER", nullable: true),
                    NationalityLabel = table.Column<string>(type: "TEXT", nullable: true),
                    EthnicityLabel = table.Column<string>(type: "TEXT", nullable: true),
                    CareerStart = table.Column<int>(type: "INTEGER", nullable: true),
                    CareerEnd = table.Column<int>(type: "INTEGER", nullable: true),
                    Tattoos = table.Column<string>(type: "TEXT", nullable: true),
                    Piercings = table.Column<string>(type: "TEXT", nullable: true),
                    LinksJson = table.Column<string>(type: "TEXT", nullable: true),
                    BiosJson = table.Column<string>(type: "TEXT", nullable: true),
                    OfferedImageCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actor", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "actor_alias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", nullable: false),
                    PrdbSiteId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actor_alias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_actor_alias_actor_ActorId",
                        column: x => x.ActorId,
                        principalTable: "actor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "actor_image",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrdbImageId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    KindLabel = table.Column<string>(type: "TEXT", nullable: true),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    PublicImageId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", nullable: true),
                    RetainedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_actor_image", x => x.Id);
                    table.ForeignKey(
                        name: "FK_actor_image_actor_ActorId",
                        column: x => x.ActorId,
                        principalTable: "actor",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_actor_NormalizedName",
                table: "actor",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_actor_PrdbActorId",
                table: "actor",
                column: "PrdbActorId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_actor_ProfileState",
                table: "actor",
                column: "ProfileState");

            migrationBuilder.CreateIndex(
                name: "IX_actor_alias_ActorId_NormalizedName",
                table: "actor_alias",
                columns: new[] { "ActorId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_actor_alias_NormalizedName",
                table: "actor_alias",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_actor_image_ActorId_PrdbImageId",
                table: "actor_image",
                columns: new[] { "ActorId", "PrdbImageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_actor_image_PublicImageId",
                table: "actor_image",
                column: "PublicImageId");

            migrationBuilder.CreateIndex(
                name: "IX_actor_image_State",
                table: "actor_image",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "actor_alias");

            migrationBuilder.DropTable(
                name: "actor_image");

            migrationBuilder.DropTable(
                name: "actor");
        }
    }
}
