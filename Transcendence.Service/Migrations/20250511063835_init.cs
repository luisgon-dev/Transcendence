using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CurrentChampionLoadouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionName = table.Column<string>(type: "text", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Lane = table.Column<string>(type: "text", nullable: false),
                    Rank = table.Column<string>(type: "text", nullable: false),
                    Patch = table.Column<string>(type: "text", nullable: false),
                    QueueType = table.Column<string>(type: "text", nullable: false),
                    Region = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrentChampionLoadouts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CurrentDataParameters",
                columns: table => new
                {
                    CurrentDataParametersId = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "text", nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrentDataParameters", x => x.CurrentDataParametersId);
                });

            migrationBuilder.CreateTable(
                name: "Matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<string>(type: "text", nullable: true),
                    MatchDate = table.Column<long>(type: "bigint", nullable: false),
                    Duration = table.Column<int>(type: "integer", nullable: false),
                    Patch = table.Column<string>(type: "text", nullable: true),
                    QueueType = table.Column<string>(type: "text", nullable: true),
                    EndOfGameResult = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Matches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Runes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryStyle = table.Column<int>(type: "integer", nullable: false),
                    SubStyle = table.Column<int>(type: "integer", nullable: false),
                    Perk0 = table.Column<int>(type: "integer", nullable: false),
                    Perk1 = table.Column<int>(type: "integer", nullable: false),
                    Perk2 = table.Column<int>(type: "integer", nullable: false),
                    Perk3 = table.Column<int>(type: "integer", nullable: false),
                    Perk4 = table.Column<int>(type: "integer", nullable: false),
                    Perk5 = table.Column<int>(type: "integer", nullable: false),
                    RuneVars0 = table.Column<int[]>(type: "integer[]", nullable: false),
                    RuneVars1 = table.Column<int[]>(type: "integer[]", nullable: false),
                    RuneVars2 = table.Column<int[]>(type: "integer[]", nullable: false),
                    RuneVars3 = table.Column<int[]>(type: "integer[]", nullable: false),
                    RuneVars4 = table.Column<int[]>(type: "integer[]", nullable: false),
                    RuneVars5 = table.Column<int[]>(type: "integer[]", nullable: false),
                    StatDefense = table.Column<int>(type: "integer", nullable: false),
                    StatFlex = table.Column<int>(type: "integer", nullable: false),
                    StatOffense = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Summoners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RiotSummonerId = table.Column<string>(type: "text", nullable: true),
                    SummonerName = table.Column<string>(type: "text", nullable: true),
                    ProfileIconId = table.Column<int>(type: "integer", nullable: false),
                    SummonerLevel = table.Column<long>(type: "bigint", nullable: false),
                    RevisionDate = table.Column<long>(type: "bigint", nullable: false),
                    Puuid = table.Column<string>(type: "text", nullable: true),
                    GameName = table.Column<string>(type: "text", nullable: true),
                    TagLine = table.Column<string>(type: "text", nullable: true),
                    AccountId = table.Column<string>(type: "text", nullable: true),
                    PlatformRegion = table.Column<string>(type: "text", nullable: true),
                    Region = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Summoners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UnitWinPercent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NumberOfGames = table.Column<int>(type: "integer", nullable: false),
                    WinRate = table.Column<float>(type: "real", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    CurrentChampionLoadoutId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnitWinPercent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnitWinPercent_CurrentChampionLoadouts_CurrentChampionLoado~",
                        column: x => x.CurrentChampionLoadoutId,
                        principalTable: "CurrentChampionLoadouts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HistoricalRanks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QueueType = table.Column<string>(type: "text", nullable: true),
                    Tier = table.Column<string>(type: "text", nullable: true),
                    RankNumber = table.Column<string>(type: "text", nullable: true),
                    LeaguePoints = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Losses = table.Column<int>(type: "integer", nullable: false),
                    DateRecorded = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalRanks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricalRanks_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kills = table.Column<int>(type: "integer", nullable: false),
                    Deaths = table.Column<int>(type: "integer", nullable: false),
                    Assists = table.Column<int>(type: "integer", nullable: false),
                    Win = table.Column<bool>(type: "boolean", nullable: false),
                    Lane = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    SummonerSpell1 = table.Column<int>(type: "integer", nullable: false),
                    SummonerSpell2 = table.Column<int>(type: "integer", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    ChampionName = table.Column<string>(type: "text", nullable: false),
                    Items = table.Column<List<int>>(type: "integer[]", nullable: false),
                    RunesId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchDetails_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchDetails_Runes_RunesId",
                        column: x => x.RunesId,
                        principalTable: "Runes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchDetails_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchSummoners",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchSummoners", x => new { x.MatchId, x.SummonerId });
                    table.ForeignKey(
                        name: "FK_MatchSummoners_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchSummoners_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Ranks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<string>(type: "text", nullable: false),
                    RankNumber = table.Column<string>(type: "text", nullable: false),
                    LeaguePoints = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Losses = table.Column<int>(type: "integer", nullable: false),
                    QueueType = table.Column<string>(type: "text", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ranks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ranks_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalRanks_SummonerId",
                table: "HistoricalRanks",
                column: "SummonerId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchDetails_MatchId",
                table: "MatchDetails",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchDetails_RunesId",
                table: "MatchDetails",
                column: "RunesId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchDetails_SummonerId",
                table: "MatchDetails",
                column: "SummonerId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchSummoners_SummonerId",
                table: "MatchSummoners",
                column: "SummonerId");

            migrationBuilder.CreateIndex(
                name: "IX_Ranks_SummonerId",
                table: "Ranks",
                column: "SummonerId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitWinPercent_CurrentChampionLoadoutId",
                table: "UnitWinPercent",
                column: "CurrentChampionLoadoutId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurrentDataParameters");

            migrationBuilder.DropTable(
                name: "HistoricalRanks");

            migrationBuilder.DropTable(
                name: "MatchDetails");

            migrationBuilder.DropTable(
                name: "MatchSummoners");

            migrationBuilder.DropTable(
                name: "Ranks");

            migrationBuilder.DropTable(
                name: "UnitWinPercent");

            migrationBuilder.DropTable(
                name: "Runes");

            migrationBuilder.DropTable(
                name: "Matches");

            migrationBuilder.DropTable(
                name: "Summoners");

            migrationBuilder.DropTable(
                name: "CurrentChampionLoadouts");
        }
    }
}
