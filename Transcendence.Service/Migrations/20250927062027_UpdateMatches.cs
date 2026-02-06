using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class UpdateMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Puuid = table.Column<string>(type: "text", nullable: true),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    TeamPosition = table.Column<string>(type: "text", nullable: true),
                    Win = table.Column<bool>(type: "boolean", nullable: false),
                    Kills = table.Column<int>(type: "integer", nullable: false),
                    Deaths = table.Column<int>(type: "integer", nullable: false),
                    Assists = table.Column<int>(type: "integer", nullable: false),
                    ChampLevel = table.Column<int>(type: "integer", nullable: false),
                    GoldEarned = table.Column<int>(type: "integer", nullable: false),
                    TotalDamageDealtToChampions = table.Column<int>(type: "integer", nullable: false),
                    VisionScore = table.Column<int>(type: "integer", nullable: false),
                    TotalMinionsKilled = table.Column<int>(type: "integer", nullable: false),
                    NeutralMinionsKilled = table.Column<int>(type: "integer", nullable: false),
                    SummonerSpell1Id = table.Column<int>(type: "integer", nullable: false),
                    SummonerSpell2Id = table.Column<int>(type: "integer", nullable: false),
                    Item0 = table.Column<int>(type: "integer", nullable: false),
                    Item1 = table.Column<int>(type: "integer", nullable: false),
                    Item2 = table.Column<int>(type: "integer", nullable: false),
                    Item3 = table.Column<int>(type: "integer", nullable: false),
                    Item4 = table.Column<int>(type: "integer", nullable: false),
                    Item5 = table.Column<int>(type: "integer", nullable: false),
                    Item6 = table.Column<int>(type: "integer", nullable: false),
                    TrinketItem = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchParticipants_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchParticipants_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Summoners_Puuid",
                table: "Summoners",
                column: "Puuid");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_MatchDate",
                table: "Matches",
                column: "MatchDate");

            migrationBuilder.CreateIndex(
                name: "IX_Matches_QueueType",
                table: "Matches",
                column: "QueueType");

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipants_ChampionId",
                table: "MatchParticipants",
                column: "ChampionId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipants_ChampionId_TeamPosition",
                table: "MatchParticipants",
                columns: new[] { "ChampionId", "TeamPosition" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipants_MatchId",
                table: "MatchParticipants",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipants_MatchId_SummonerId",
                table: "MatchParticipants",
                columns: new[] { "MatchId", "SummonerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipants_SummonerId",
                table: "MatchParticipants",
                column: "SummonerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchParticipants");

            migrationBuilder.DropIndex(
                name: "IX_Summoners_Puuid",
                table: "Summoners");

            migrationBuilder.DropIndex(
                name: "IX_Matches_MatchDate",
                table: "Matches");

            migrationBuilder.DropIndex(
                name: "IX_Matches_QueueType",
                table: "Matches");
        }
    }
}
