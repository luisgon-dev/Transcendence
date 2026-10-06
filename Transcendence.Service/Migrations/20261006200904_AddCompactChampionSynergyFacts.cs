using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddCompactChampionSynergyFacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChampionSynergyFacts",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    PartnerParticipantId = table.Column<int>(type: "integer", nullable: false),
                    QueueFamily = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlatformRegion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Win = table.Column<bool>(type: "boolean", nullable: false),
                    PartnerChampionId = table.Column<int>(type: "integer", nullable: false),
                    PartnerRole = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionSynergyFacts", x => new { x.MatchId, x.QueueFamily, x.ParticipantId, x.PartnerParticipantId });
                });

            migrationBuilder.CreateTable(
                name: "ChampionSynergySourceMatches",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MaterializedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionSynergySourceMatches", x => x.MatchId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionSynergyFacts_Patch_QueueFamily_ChampionId_Role_Plat~",
                table: "ChampionSynergyFacts",
                columns: new[] { "Patch", "QueueFamily", "ChampionId", "Role", "PlatformRegion" })
                .Annotation("Npgsql:IndexInclude", new[] { "SummonerId", "Win", "PartnerParticipantId", "PartnerChampionId", "PartnerRole" });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionSynergySourceMatches_Patch",
                table: "ChampionSynergySourceMatches",
                column: "Patch");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChampionSynergyFacts");

            migrationBuilder.DropTable(
                name: "ChampionSynergySourceMatches");
        }
    }
}
