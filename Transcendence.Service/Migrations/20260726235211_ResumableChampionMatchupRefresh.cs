using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class ResumableChampionMatchupRefresh : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChampionMatchupStats_Patch_ChampionId_Role",
                table: "ChampionMatchupStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionMatchupStats_Patch_RankTier_ChampionId_Role_Opponen~",
                table: "ChampionMatchupStats");

            migrationBuilder.AddColumn<Guid>(
                name: "SnapshotId",
                table: "ChampionMatchupStats",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChampionMatchupFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionParticipantId = table.Column<int>(type: "integer", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OpponentChampionId = table.Column<int>(type: "integer", nullable: false),
                    Win = table.Column<bool>(type: "boolean", nullable: false),
                    HasTimeline = table.Column<bool>(type: "boolean", nullable: false),
                    GoldDiffAt15 = table.Column<int>(type: "integer", nullable: false),
                    XpDiffAt15 = table.Column<int>(type: "integer", nullable: false),
                    TimelineDerivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionMatchupFacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChampionMatchupSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceCutoffUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    SourceFactCount = table.Column<int>(type: "integer", nullable: false),
                    TotalChampionCount = table.Column<int>(type: "integer", nullable: false),
                    ProcessedChampionCount = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionMatchupSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChampionMatchupSourceMatches",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParticipantCount = table.Column<int>(type: "integer", nullable: false),
                    TimelineSnapshotCount = table.Column<int>(type: "integer", nullable: false),
                    LatestTimelineDerivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionMatchupSourceMatches", x => x.MatchId);
                });

            migrationBuilder.CreateTable(
                name: "ChampionMatchupRankSnapshots",
                columns: table => new
                {
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RankTier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionMatchupRankSnapshots", x => new { x.SnapshotId, x.SummonerId });
                    table.ForeignKey(
                        name: "FK_ChampionMatchupRankSnapshots_ChampionMatchupSnapshots_Snaps~",
                        column: x => x.SnapshotId,
                        principalTable: "ChampionMatchupSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionMatchupStats_SnapshotId_Patch_ChampionId_Role",
                table: "ChampionMatchupStats",
                columns: new[] { "SnapshotId", "Patch", "ChampionId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionMatchupStats_SnapshotId_Patch_RankTier_ChampionId_R~",
                table: "ChampionMatchupStats",
                columns: new[] { "SnapshotId", "Patch", "RankTier", "ChampionId", "Role", "OpponentChampionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChampionMatchupFacts_MatchId_ChampionParticipantId",
                table: "ChampionMatchupFacts",
                columns: new[] { "MatchId", "ChampionParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChampionMatchupFacts_Patch_ChampionId_UpdatedAtUtc",
                table: "ChampionMatchupFacts",
                columns: new[] { "Patch", "ChampionId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionMatchupFacts_Patch_SummonerId",
                table: "ChampionMatchupFacts",
                columns: new[] { "Patch", "SummonerId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionMatchupSnapshots_Patch",
                table: "ChampionMatchupSnapshots",
                column: "Patch",
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_ChampionMatchupSnapshots_Patch_Status_CompletedAtUtc",
                table: "ChampionMatchupSnapshots",
                columns: new[] { "Patch", "Status", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionMatchupSourceMatches_Patch_ProcessedAtUtc",
                table: "ChampionMatchupSourceMatches",
                columns: new[] { "Patch", "ProcessedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_ChampionMatchupStats_ChampionMatchupSnapshots_SnapshotId",
                table: "ChampionMatchupStats",
                column: "SnapshotId",
                principalTable: "ChampionMatchupSnapshots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChampionMatchupStats_ChampionMatchupSnapshots_SnapshotId",
                table: "ChampionMatchupStats");

            migrationBuilder.DropTable(
                name: "ChampionMatchupFacts");

            migrationBuilder.DropTable(
                name: "ChampionMatchupRankSnapshots");

            migrationBuilder.DropTable(
                name: "ChampionMatchupSourceMatches");

            migrationBuilder.DropTable(
                name: "ChampionMatchupSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_ChampionMatchupStats_SnapshotId_Patch_ChampionId_Role",
                table: "ChampionMatchupStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionMatchupStats_SnapshotId_Patch_RankTier_ChampionId_R~",
                table: "ChampionMatchupStats");

            migrationBuilder.DropColumn(
                name: "SnapshotId",
                table: "ChampionMatchupStats");

            migrationBuilder.CreateIndex(
                name: "IX_ChampionMatchupStats_Patch_ChampionId_Role",
                table: "ChampionMatchupStats",
                columns: new[] { "Patch", "ChampionId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionMatchupStats_Patch_RankTier_ChampionId_Role_Opponen~",
                table: "ChampionMatchupStats",
                columns: new[] { "Patch", "RankTier", "ChampionId", "Role", "OpponentChampionId" },
                unique: true);
        }
    }
}
