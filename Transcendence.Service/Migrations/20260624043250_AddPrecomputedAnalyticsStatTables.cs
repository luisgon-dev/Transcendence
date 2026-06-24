using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddPrecomputedAnalyticsStatTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChampionBanScopeStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "text", nullable: false),
                    PlatformRegion = table.Column<string>(type: "text", nullable: false),
                    RankScope = table.Column<string>(type: "text", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    BannedMatches = table.Column<int>(type: "integer", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionBanScopeStats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChampionRoleTierStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "text", nullable: false),
                    PlatformRegion = table.Column<string>(type: "text", nullable: false),
                    RankTier = table.Column<string>(type: "text", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionRoleTierStats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScopeMatchCountStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "text", nullable: false),
                    PlatformRegion = table.Column<string>(type: "text", nullable: false),
                    RankScope = table.Column<string>(type: "text", nullable: false),
                    TotalMatches = table.Column<int>(type: "integer", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScopeMatchCountStats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionBanScopeStats_Patch_PlatformRegion_RankScope_Champi~",
                table: "ChampionBanScopeStats",
                columns: new[] { "Patch", "PlatformRegion", "RankScope", "ChampionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChampionRoleTierStats_Patch_ChampionId_Role",
                table: "ChampionRoleTierStats",
                columns: new[] { "Patch", "ChampionId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionRoleTierStats_Patch_PlatformRegion_RankTier_Champio~",
                table: "ChampionRoleTierStats",
                columns: new[] { "Patch", "PlatformRegion", "RankTier", "ChampionId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChampionRoleTierStats_Patch_Role_RankTier",
                table: "ChampionRoleTierStats",
                columns: new[] { "Patch", "Role", "RankTier" });

            migrationBuilder.CreateIndex(
                name: "IX_ScopeMatchCountStats_Patch_PlatformRegion_RankScope",
                table: "ScopeMatchCountStats",
                columns: new[] { "Patch", "PlatformRegion", "RankScope" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChampionBanScopeStats");

            migrationBuilder.DropTable(
                name: "ChampionRoleTierStats");

            migrationBuilder.DropTable(
                name: "ScopeMatchCountStats");
        }
    }
}
