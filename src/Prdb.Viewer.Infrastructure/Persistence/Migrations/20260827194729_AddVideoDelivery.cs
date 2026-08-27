using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prdb.Viewer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DirectPlayClassification",
                table: "video_file",
                type: "TEXT",
                nullable: false,
                defaultValue: "Undetermined");

            migrationBuilder.AddColumn<Guid>(
                name: "PublicDeliveryId",
                table: "video_file",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "FirstPlayableVideoReachedAt",
                table: "installation_configuration",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "installation_configuration",
                keyColumn: "Id",
                keyValue: 1,
                column: "FirstPlayableVideoReachedAt",
                value: null);

            migrationBuilder.Sql(
                """
                UPDATE video_file
                SET PublicDeliveryId = lower(hex(randomblob(4))) || '-' ||
                    lower(hex(randomblob(2))) || '-' ||
                    lower(hex(randomblob(2))) || '-' ||
                    lower(hex(randomblob(2))) || '-' ||
                    lower(hex(randomblob(6)));

                UPDATE video_file
                SET DirectPlayClassification = 'BaselineCandidate'
                WHERE VideoCodec = 'h264'
                  AND (AudioCodec IS NULL OR AudioCodec IN ('aac', 'mp3'))
                  AND (instr(ContainerFormat, 'mp4') > 0 OR instr(ContainerFormat, 'mov') > 0);

                UPDATE installation_configuration
                SET FirstPlayableVideoReachedAt = (
                    SELECT min(InspectedAt)
                    FROM video_file
                    WHERE DirectPlayClassification = 'BaselineCandidate'
                )
                WHERE Id = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_video_file_PublicDeliveryId",
                table: "video_file",
                column: "PublicDeliveryId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_video_file_PublicDeliveryId",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "DirectPlayClassification",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "PublicDeliveryId",
                table: "video_file");

            migrationBuilder.DropColumn(
                name: "FirstPlayableVideoReachedAt",
                table: "installation_configuration");
        }
    }
}
