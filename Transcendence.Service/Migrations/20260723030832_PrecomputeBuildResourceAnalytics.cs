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
                name: "BuildResourceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsFullRebuild = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedMatchCount = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildResourceSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BuildResourcePopulationStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformRegion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildResourcePopulationStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildResourcePopulationStats_BuildResourceSnapshots_Snapsho~",
                        column: x => x.SnapshotId,
                        principalTable: "BuildResourceSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuildResourceProcessedMatches",
                columns: table => new
                {
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildResourceProcessedMatches", x => new { x.SnapshotId, x.MatchId });
                    table.ForeignKey(
                        name: "FK_BuildResourceProcessedMatches_BuildResourceSnapshots_Snapsh~",
                        column: x => x.SnapshotId,
                        principalTable: "BuildResourceSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BuildResourceStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformRegion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ResourceId = table.Column<int>(type: "integer", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildResourceStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BuildResourceStats_BuildResourceSnapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "BuildResourceSnapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildResourcePopulationStats_SnapshotId_PlatformRegion_Cham~",
                table: "BuildResourcePopulationStats",
                columns: new[] { "SnapshotId", "PlatformRegion", "ChampionId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildResourceProcessedMatches_MatchId",
                table: "BuildResourceProcessedMatches",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_BuildResourceSnapshots_Patch_IsActive",
                table: "BuildResourceSnapshots",
                columns: new[] { "Patch", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildResourceSnapshots_Patch_Status_CompletedAtUtc",
                table: "BuildResourceSnapshots",
                columns: new[] { "Patch", "Status", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildResourceStats_SnapshotId_PlatformRegion_ResourceType_R~",
                table: "BuildResourceStats",
                columns: new[] { "SnapshotId", "PlatformRegion", "ResourceType", "ResourceId", "ChampionId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildResourceStats_SnapshotId_ResourceType_ResourceId",
                table: "BuildResourceStats",
                columns: new[] { "SnapshotId", "ResourceType", "ResourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildResourcePopulationStats");

            migrationBuilder.DropTable(
                name: "BuildResourceProcessedMatches");

            migrationBuilder.DropTable(
                name: "BuildResourceStats");

            migrationBuilder.DropTable(
                name: "BuildResourceSnapshots");
        }
    }
}
