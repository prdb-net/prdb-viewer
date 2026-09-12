using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SettleAGroupWithOneDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupDecisionId",
                table: "identification_decision",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_identification_decision_GroupDecisionId",
                table: "identification_decision",
                column: "GroupDecisionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_identification_decision_GroupDecisionId",
                table: "identification_decision");

            migrationBuilder.DropColumn(
                name: "GroupDecisionId",
                table: "identification_decision");
        }
    }
}
