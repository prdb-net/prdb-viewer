using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FavouriteActors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personal_actor_state",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrdbActorId = table.Column<string>(type: "TEXT", nullable: false),
                    FavouriteAddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personal_actor_state", x => new { x.AccountId, x.PrdbActorId });
                    table.ForeignKey(
                        name: "FK_personal_actor_state_account_AccountId",
                        column: x => x.AccountId,
                        principalTable: "account",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_personal_actor_state_AccountId",
                table: "personal_actor_state",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "personal_actor_state");
        }
    }
}
