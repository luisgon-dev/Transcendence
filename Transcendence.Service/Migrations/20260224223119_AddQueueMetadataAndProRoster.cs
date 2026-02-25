using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueMetadataAndProRoster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QueueFamily",
                table: "Matches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QueueId",
                table: "Matches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE "Matches"
                SET "QueueId" = CASE
                    WHEN "QueueType" ~ '^[0-9]+$' THEN CAST("QueueType" AS integer)
                    ELSE 0
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "Matches"
                SET "QueueFamily" = CASE
                    WHEN "QueueId" = 420 THEN 'RANKED_SOLO_DUO'
                    WHEN "QueueId" = 440 THEN 'RANKED_FLEX'
                    WHEN "QueueId" IN (400, 430, 490, 700) THEN 'NORMAL_SR'
                    WHEN "QueueId" = 450 THEN 'ARAM'
                    WHEN "QueueId" IN (76, 78, 83, 98, 100, 310, 313, 315, 317, 318, 325, 600, 610, 720, 900, 920, 940, 1020, 1300, 1400, 1700, 1710, 1810, 1820, 1830, 1840, 1900) THEN 'ROTATING'
                    ELSE 'OTHER'
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "Matches"
                SET "QueueType" = CASE
                    WHEN "QueueId" = 420 THEN 'Ranked Solo/Duo'
                    WHEN "QueueId" = 440 THEN 'Ranked Flex'
                    WHEN "QueueId" = 400 THEN 'Normal Draft'
                    WHEN "QueueId" = 430 THEN 'Normal Blind'
                    WHEN "QueueId" = 490 THEN 'Quickplay'
                    WHEN "QueueId" = 450 THEN 'ARAM'
                    ELSE COALESCE("QueueType", CAST("QueueId" AS text))
                END;
                """);

            migrationBuilder.CreateTable(
                name: "MatchBans",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    PickTurn = table.Column<int>(type: "integer", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchBans", x => new { x.MatchId, x.TeamId, x.PickTurn, x.ChampionId });
                    table.ForeignKey(
                        name: "FK_MatchBans_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackedProSummoners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Puuid = table.Column<string>(type: "text", nullable: false),
                    PlatformRegion = table.Column<string>(type: "text", nullable: false),
                    GameName = table.Column<string>(type: "text", nullable: true),
                    TagLine = table.Column<string>(type: "text", nullable: true),
                    ProName = table.Column<string>(type: "text", nullable: true),
                    TeamName = table.Column<string>(type: "text", nullable: true),
                    IsPro = table.Column<bool>(type: "boolean", nullable: false),
                    IsHighEloOtp = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedProSummoners", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Matches_QueueFamily",
                table: "Matches",
                column: "QueueFamily");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_QueueId",
                table: "Matches",
                column: "QueueId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchBans_ChampionId",
                table: "MatchBans",
                column: "ChampionId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchBans_ChampionId_MatchId",
                table: "MatchBans",
                columns: new[] { "ChampionId", "MatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedProSummoners_IsActive",
                table: "TrackedProSummoners",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedProSummoners_Puuid_PlatformRegion",
                table: "TrackedProSummoners",
                columns: new[] { "Puuid", "PlatformRegion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedProSummoners_UpdatedAtUtc",
                table: "TrackedProSummoners",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchBans");

            migrationBuilder.DropTable(
                name: "TrackedProSummoners");

            migrationBuilder.DropIndex(
                name: "IX_Matches_QueueFamily",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_QueueId",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "QueueFamily",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "QueueId",
                table: "Matches");
        }
    }
}
