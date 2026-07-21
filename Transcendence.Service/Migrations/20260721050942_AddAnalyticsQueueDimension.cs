using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyticsQueueDimension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScopeMatchCountStats_Patch_PlatformRegion_RankScope",
                table: "ScopeMatchCountStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionScopeGradeStats_Patch_PlatformRegion_RankScope_Role",
                table: "ChampionScopeGradeStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionScopeGradeStats_Patch_PlatformRegion_RankScope_Role~",
                table: "ChampionScopeGradeStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionRoleTierStats_Patch_ChampionId_Role",
                table: "ChampionRoleTierStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionRoleTierStats_Patch_PlatformRegion_RankTier_Champio~",
                table: "ChampionRoleTierStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionRoleTierStats_Patch_Role_RankTier",
                table: "ChampionRoleTierStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionBanScopeStats_Patch_PlatformRegion_RankScope_Champi~",
                table: "ChampionBanScopeStats");

            migrationBuilder.AddColumn<string>(
                name: "QueueFamily",
                table: "ScopeMatchCountStats",
                type: "text",
                nullable: false,
                defaultValue: "RANKED_SOLO_DUO");

            migrationBuilder.AddColumn<string>(
                name: "QueueFamily",
                table: "ChampionScopeGradeStats",
                type: "text",
                nullable: false,
                defaultValue: "RANKED_SOLO_DUO");

            migrationBuilder.AddColumn<string>(
                name: "QueueFamily",
                table: "ChampionRoleTierStats",
                type: "text",
                nullable: false,
                defaultValue: "RANKED_SOLO_DUO");

            migrationBuilder.AddColumn<string>(
                name: "QueueFamily",
                table: "ChampionBanScopeStats",
                type: "text",
                nullable: false,
                defaultValue: "RANKED_SOLO_DUO");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeMatchCountStats_Patch_QueueFamily_PlatformRegion_RankS~",
                table: "ScopeMatchCountStats",
                columns: new[] { "Patch", "QueueFamily", "PlatformRegion", "RankScope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChampionScopeGradeStats_Patch_QueueFamily_PlatformRegion_R~1",
                table: "ChampionScopeGradeStats",
                columns: new[] { "Patch", "QueueFamily", "PlatformRegion", "RankScope", "Role", "ChampionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChampionScopeGradeStats_Patch_QueueFamily_PlatformRegion_Ra~",
                table: "ChampionScopeGradeStats",
                columns: new[] { "Patch", "QueueFamily", "PlatformRegion", "RankScope", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionRoleTierStats_Patch_QueueFamily_ChampionId_Role",
                table: "ChampionRoleTierStats",
                columns: new[] { "Patch", "QueueFamily", "ChampionId", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionRoleTierStats_Patch_QueueFamily_PlatformRegion_Rank~",
                table: "ChampionRoleTierStats",
                columns: new[] { "Patch", "QueueFamily", "PlatformRegion", "RankTier", "ChampionId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChampionRoleTierStats_Patch_QueueFamily_Role_RankTier",
                table: "ChampionRoleTierStats",
                columns: new[] { "Patch", "QueueFamily", "Role", "RankTier" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionBanScopeStats_Patch_QueueFamily_PlatformRegion_Rank~",
                table: "ChampionBanScopeStats",
                columns: new[] { "Patch", "QueueFamily", "PlatformRegion", "RankScope", "ChampionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScopeMatchCountStats_Patch_QueueFamily_PlatformRegion_RankS~",
                table: "ScopeMatchCountStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionScopeGradeStats_Patch_QueueFamily_PlatformRegion_R~1",
                table: "ChampionScopeGradeStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionScopeGradeStats_Patch_QueueFamily_PlatformRegion_Ra~",
                table: "ChampionScopeGradeStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionRoleTierStats_Patch_QueueFamily_ChampionId_Role",
                table: "ChampionRoleTierStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionRoleTierStats_Patch_QueueFamily_PlatformRegion_Rank~",
                table: "ChampionRoleTierStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionRoleTierStats_Patch_QueueFamily_Role_RankTier",
                table: "ChampionRoleTierStats");

            migrationBuilder.DropIndex(
                name: "IX_ChampionBanScopeStats_Patch_QueueFamily_PlatformRegion_Rank~",
                table: "ChampionBanScopeStats");

            migrationBuilder.DropColumn(
                name: "QueueFamily",
                table: "ScopeMatchCountStats");

            migrationBuilder.DropColumn(
                name: "QueueFamily",
                table: "ChampionScopeGradeStats");

            migrationBuilder.DropColumn(
                name: "QueueFamily",
                table: "ChampionRoleTierStats");

            migrationBuilder.DropColumn(
                name: "QueueFamily",
                table: "ChampionBanScopeStats");

            migrationBuilder.CreateIndex(
                name: "IX_ScopeMatchCountStats_Patch_PlatformRegion_RankScope",
                table: "ScopeMatchCountStats",
                columns: new[] { "Patch", "PlatformRegion", "RankScope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChampionScopeGradeStats_Patch_PlatformRegion_RankScope_Role",
                table: "ChampionScopeGradeStats",
                columns: new[] { "Patch", "PlatformRegion", "RankScope", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionScopeGradeStats_Patch_PlatformRegion_RankScope_Role~",
                table: "ChampionScopeGradeStats",
                columns: new[] { "Patch", "PlatformRegion", "RankScope", "Role", "ChampionId" },
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
                name: "IX_ChampionBanScopeStats_Patch_PlatformRegion_RankScope_Champi~",
                table: "ChampionBanScopeStats",
                columns: new[] { "Patch", "PlatformRegion", "RankScope", "ChampionId" },
                unique: true);
        }
    }
}
