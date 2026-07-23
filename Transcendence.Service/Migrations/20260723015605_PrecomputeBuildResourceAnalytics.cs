using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class PrecomputeBuildResourceAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BuildResourceStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlatformRegion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ResourceId = table.Column<int>(type: "integer", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildResourceStats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildResourceStats_Patch_PlatformRegion_ResourceType_Resour~",
                table: "BuildResourceStats",
                columns: new[] { "Patch", "PlatformRegion", "ResourceType", "ResourceId", "ChampionId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildResourceStats_Patch_ResourceType_ResourceId",
                table: "BuildResourceStats",
                columns: new[] { "Patch", "ResourceType", "ResourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildResourceStats");
        }
    }
}
