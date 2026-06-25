using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionMatchupStat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChampionMatchupStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "text", nullable: false),
                    RankTier = table.Column<string>(type: "text", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    OpponentChampionId = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    TimelineGames = table.Column<int>(type: "integer", nullable: false),
                    SumGoldDiffAt15 = table.Column<long>(type: "bigint", nullable: false),
                    SumXpDiffAt15 = table.Column<long>(type: "bigint", nullable: false),
                    LatestTimelineAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ComputedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionMatchupStats", x => x.Id);
                });

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChampionMatchupStats");
        }
    }
}
