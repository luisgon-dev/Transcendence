using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionScopeGradeStat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChampionScopeGradeStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "text", nullable: false),
                    PlatformRegion = table.Column<string>(type: "text", nullable: false),
                    RankScope = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    PrimaryRole = table.Column<string>(type: "text", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    StrengthScore = table.Column<double>(type: "double precision", nullable: false),
                    WinRate = table.Column<double>(type: "double precision", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    PickRate = table.Column<double>(type: "double precision", nullable: false),
                    BanRate = table.Column<double>(type: "double precision", nullable: false),
                    ContestedScore = table.Column<double>(type: "double precision", nullable: false),
                    RoleBaseline = table.Column<double>(type: "double precision", nullable: false),
                    PriorStrength = table.Column<double>(type: "double precision", nullable: false),
                    IsLowSample = table.Column<bool>(type: "boolean", nullable: false),
                    Movement = table.Column<int>(type: "integer", nullable: true),
                    PreviousTier = table.Column<int>(type: "integer", nullable: true),
                    ComputedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionScopeGradeStats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionScopeGradeStats_Patch_PlatformRegion_RankScope_Role",
                table: "ChampionScopeGradeStats",
                columns: new[] { "Patch", "PlatformRegion", "RankScope", "Role" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionScopeGradeStats_Patch_PlatformRegion_RankScope_Role~",
                table: "ChampionScopeGradeStats",
                columns: new[] { "Patch", "PlatformRegion", "RankScope", "Role", "ChampionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChampionScopeGradeStats");
        }
    }
}
