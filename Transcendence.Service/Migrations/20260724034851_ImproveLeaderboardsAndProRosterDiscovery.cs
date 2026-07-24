using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class ImproveLeaderboardsAndProRosterDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastVerifiedAtUtc",
                table: "TrackedProSummoners",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OtpChampionId",
                table: "TrackedProSummoners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OtpEvaluatedAtUtc",
                table: "TrackedProSummoners",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OtpGames",
                table: "TrackedProSummoners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OtpSampleSize",
                table: "TrackedProSummoners",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "TrackedProSummoners",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<string>(
                name: "SourceExternalId",
                table: "TrackedProSummoners",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProPlayerDiscoveryCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TeamName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SoloQueueIds = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApprovedTrackedProSummonerId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProPlayerDiscoveryCandidates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedProSummoners_Source_SourceExternalId",
                table: "TrackedProSummoners",
                columns: new[] { "Source", "SourceExternalId" });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_Leaderboard",
                table: "Matches",
                columns: new[] { "PlatformRegion", "Status", "QueueId", "MatchDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ProPlayerDiscoveryCandidates_Source_ExternalId",
                table: "ProPlayerDiscoveryCandidates",
                columns: new[] { "Source", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProPlayerDiscoveryCandidates_Status_LastSeenAtUtc",
                table: "ProPlayerDiscoveryCandidates",
                columns: new[] { "Status", "LastSeenAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProPlayerDiscoveryCandidates");

            migrationBuilder.DropIndex(
                name: "IX_TrackedProSummoners_Source_SourceExternalId",
                table: "TrackedProSummoners");

            migrationBuilder.DropIndex(
                name: "IX_Matches_Leaderboard",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "LastVerifiedAtUtc",
                table: "TrackedProSummoners");

            migrationBuilder.DropColumn(
                name: "OtpChampionId",
                table: "TrackedProSummoners");

            migrationBuilder.DropColumn(
                name: "OtpEvaluatedAtUtc",
                table: "TrackedProSummoners");

            migrationBuilder.DropColumn(
                name: "OtpGames",
                table: "TrackedProSummoners");

            migrationBuilder.DropColumn(
                name: "OtpSampleSize",
                table: "TrackedProSummoners");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "TrackedProSummoners");

            migrationBuilder.DropColumn(
                name: "SourceExternalId",
                table: "TrackedProSummoners");
        }
    }
}
