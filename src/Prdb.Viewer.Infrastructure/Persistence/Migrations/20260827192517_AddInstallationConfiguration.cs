using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallationConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "installation_configuration",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    ActivePrdbCredential = table.Column<string>(type: "TEXT", nullable: true),
                    PendingPrdbCredential = table.Column<string>(type: "TEXT", nullable: true),
                    PendingCredentialRevision = table.Column<Guid>(type: "TEXT", nullable: true),
                    PrdbConnectionStatus = table.Column<string>(type: "TEXT", nullable: false),
                    LastConnectionAttemptAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastConnectionVerifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastConnectionIssue = table.Column<string>(type: "TEXT", nullable: true),
                    ConfiguredAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_installation_configuration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "library_directory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ContainerPath = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Health = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigurationGeneration = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    InitialProcessingStartedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_directory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "library_directory_stage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ContainerPath = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_directory_stage", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "installation_configuration",
                columns: new[] { "Id", "ActivePrdbCredential", "ConfiguredAt", "LastConnectionAttemptAt", "LastConnectionIssue", "LastConnectionVerifiedAt", "PendingCredentialRevision", "PendingPrdbCredential", "PrdbConnectionStatus" },
                values: new object[] { 1, null, null, null, null, null, null, null, "Missing" });

            migrationBuilder.CreateIndex(
                name: "IX_library_directory_ContainerPath",
                table: "library_directory",
                column: "ContainerPath",
                unique: true,
                filter: "\"State\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_library_directory_State",
                table: "library_directory",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_library_directory_stage_ExpiresAt",
                table: "library_directory_stage",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "installation_configuration");

            migrationBuilder.DropTable(
                name: "library_directory");

            migrationBuilder.DropTable(
                name: "library_directory_stage");
        }
    }
}
