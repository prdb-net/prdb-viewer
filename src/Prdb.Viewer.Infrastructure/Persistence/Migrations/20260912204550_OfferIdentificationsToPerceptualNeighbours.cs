using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfferIdentificationsToPerceptualNeighbours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NeighbourDistance",
                table: "identification_candidate",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeighbourDurationsAgree",
                table: "identification_candidate",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NeighbourVideoFileId",
                table: "identification_candidate",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NeighbourDistance",
                table: "identification_candidate");

            migrationBuilder.DropColumn(
                name: "NeighbourDurationsAgree",
                table: "identification_candidate");

            migrationBuilder.DropColumn(
                name: "NeighbourVideoFileId",
                table: "identification_candidate");
        }
    }
}
