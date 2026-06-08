using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchDamageBreakdownAndTeamObjectives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MagicDamageDealtToChampions",
                table: "MatchParticipants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PhysicalDamageDealtToChampions",
                table: "MatchParticipants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TrueDamageDealtToChampions",
                table: "MatchParticipants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MatchTeamObjectives",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    FirstBlood = table.Column<bool>(type: "boolean", nullable: false),
                    BaronKills = table.Column<int>(type: "integer", nullable: false),
                    BaronFirst = table.Column<bool>(type: "boolean", nullable: false),
                    DragonKills = table.Column<int>(type: "integer", nullable: false),
                    DragonFirst = table.Column<bool>(type: "boolean", nullable: false),
                    RiftHeraldKills = table.Column<int>(type: "integer", nullable: false),
                    RiftHeraldFirst = table.Column<bool>(type: "boolean", nullable: false),
                    HordeKills = table.Column<int>(type: "integer", nullable: false),
                    HordeFirst = table.Column<bool>(type: "boolean", nullable: false),
                    TowerKills = table.Column<int>(type: "integer", nullable: false),
                    TowerFirst = table.Column<bool>(type: "boolean", nullable: false),
                    InhibitorKills = table.Column<int>(type: "integer", nullable: false),
                    InhibitorFirst = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchTeamObjectives", x => new { x.MatchId, x.TeamId });
                    table.ForeignKey(
                        name: "FK_MatchTeamObjectives_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchTeamObjectives");

            migrationBuilder.DropColumn(
                name: "MagicDamageDealtToChampions",
                table: "MatchParticipants");

            migrationBuilder.DropColumn(
                name: "PhysicalDamageDealtToChampions",
                table: "MatchParticipants");

            migrationBuilder.DropColumn(
                name: "TrueDamageDealtToChampions",
                table: "MatchParticipants");
        }
    }
}
