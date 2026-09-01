using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineKillEventScalars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "KillerId",
                table: "MatchTimelineEventPayloads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KillerTeamId",
                table: "MatchTimelineEventPayloads",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "MatchTimelineEventPayloads",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KillerId",
                table: "MatchTimelineEventPayloads");

            migrationBuilder.DropColumn(
                name: "KillerTeamId",
                table: "MatchTimelineEventPayloads");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "MatchTimelineEventPayloads");
        }
    }
}
