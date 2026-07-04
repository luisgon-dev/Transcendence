using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddFullHistoryBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GameEndedInEarlySurrender",
                table: "MatchParticipants",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GameEndedInSurrender",
                table: "MatchParticipants",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TeamEarlySurrendered",
                table: "MatchParticipants",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RankedSeasons",
                columns: table => new
                {
                    SeasonKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RankedSeasons", x => x.SeasonKey);
                });

            migrationBuilder.CreateTable(
                name: "SummonerFullHistoryBackfills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedByUserAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CursorEndEpochSeconds = table.Column<long>(type: "bigint", nullable: true),
                    PagesScanned = table.Column<int>(type: "integer", nullable: false),
                    MatchIdsDiscovered = table.Column<int>(type: "integer", nullable: false),
                    FactsPersisted = table.Column<int>(type: "integer", nullable: false),
                    SkippedExistingFacts = table.Column<int>(type: "integer", nullable: false),
                    DetailFetchFailures = table.Column<int>(type: "integer", nullable: false),
                    LastErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SummonerFullHistoryBackfills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SummonerFullHistoryBackfills_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SummonerMatchFactFetchFailures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlatformRegion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    RegionalRoute = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastErrorMessage = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    FirstAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SummonerMatchFactFetchFailures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SummonerMatchFactFetchFailures_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SummonerMatchFacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Puuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PlatformRegion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    RegionalRoute = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    MatchDate = table.Column<long>(type: "bigint", nullable: false),
                    SeasonKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    QueueType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    QueueFamily = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    EndOfGameResult = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    TeamPosition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IndividualPosition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Win = table.Column<bool>(type: "boolean", nullable: false),
                    Kills = table.Column<int>(type: "integer", nullable: false),
                    Deaths = table.Column<int>(type: "integer", nullable: false),
                    Assists = table.Column<int>(type: "integer", nullable: false),
                    VisionScore = table.Column<int>(type: "integer", nullable: false),
                    TotalDamageDealtToChampions = table.Column<int>(type: "integer", nullable: false),
                    TotalMinionsKilled = table.Column<int>(type: "integer", nullable: false),
                    NeutralMinionsKilled = table.Column<int>(type: "integer", nullable: false),
                    SummonerSpell1Id = table.Column<int>(type: "integer", nullable: false),
                    SummonerSpell2Id = table.Column<int>(type: "integer", nullable: false),
                    GameEndedInEarlySurrender = table.Column<bool>(type: "boolean", nullable: true),
                    GameEndedInSurrender = table.Column<bool>(type: "boolean", nullable: true),
                    TeamEarlySurrendered = table.Column<bool>(type: "boolean", nullable: true),
                    CountsTowardRankedTotal = table.Column<bool>(type: "boolean", nullable: false),
                    RankedCountClassifierVersion = table.Column<int>(type: "integer", nullable: false),
                    RankedCountExclusionReason = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FetchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SummonerMatchFacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SummonerMatchFacts_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SummonerSeasonChampionStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QueueScope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Losses = table.Column<int>(type: "integer", nullable: false),
                    TotalKills = table.Column<long>(type: "bigint", nullable: false),
                    TotalDeaths = table.Column<long>(type: "bigint", nullable: false),
                    TotalAssists = table.Column<long>(type: "bigint", nullable: false),
                    TotalVisionScore = table.Column<long>(type: "bigint", nullable: false),
                    TotalDamageToChamps = table.Column<long>(type: "bigint", nullable: false),
                    TotalCs = table.Column<long>(type: "bigint", nullable: false),
                    TotalDurationSeconds = table.Column<long>(type: "bigint", nullable: false),
                    AggregationVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SummonerSeasonChampionStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SummonerSeasonChampionStats_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SummonerSeasonCoverages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QueueScope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BackfillStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CompletedMatchCount = table.Column<int>(type: "integer", nullable: false),
                    RiotWins = table.Column<int>(type: "integer", nullable: true),
                    RiotLosses = table.Column<int>(type: "integer", nullable: true),
                    RiotTotal = table.Column<int>(type: "integer", nullable: true),
                    RankedCountDelta = table.Column<int>(type: "integer", nullable: true),
                    CoverageStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ClassifierVersion = table.Column<int>(type: "integer", nullable: false),
                    LastComparedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastBackfilledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SummonerSeasonCoverages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SummonerSeasonCoverages_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SummonerSeasonOverviewStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    QueueScope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalMatches = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Losses = table.Column<int>(type: "integer", nullable: false),
                    TotalKills = table.Column<long>(type: "bigint", nullable: false),
                    TotalDeaths = table.Column<long>(type: "bigint", nullable: false),
                    TotalAssists = table.Column<long>(type: "bigint", nullable: false),
                    TotalVisionScore = table.Column<long>(type: "bigint", nullable: false),
                    TotalDamageToChamps = table.Column<long>(type: "bigint", nullable: false),
                    TotalCs = table.Column<long>(type: "bigint", nullable: false),
                    TotalDurationSeconds = table.Column<long>(type: "bigint", nullable: false),
                    AggregationVersion = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SummonerSeasonOverviewStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SummonerSeasonOverviewStats_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RankedSeasons_IsActive",
                table: "RankedSeasons",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_RankedSeasons_StartUtc_EndUtc",
                table: "RankedSeasons",
                columns: new[] { "StartUtc", "EndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SummonerFullHistoryBackfills_Status_UpdatedAtUtc",
                table: "SummonerFullHistoryBackfills",
                columns: new[] { "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SummonerFullHistoryBackfills_SummonerId_Scope",
                table: "SummonerFullHistoryBackfills",
                columns: new[] { "SummonerId", "Scope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SummonerMatchFactFetchFailures_ResolvedAtUtc_LastAttemptAtU~",
                table: "SummonerMatchFactFetchFailures",
                columns: new[] { "ResolvedAtUtc", "LastAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SummonerMatchFactFetchFailures_SummonerId_MatchId",
                table: "SummonerMatchFactFetchFailures",
                columns: new[] { "SummonerId", "MatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SummonerMatchFacts_MatchDate_QueueId",
                table: "SummonerMatchFacts",
                columns: new[] { "MatchDate", "QueueId" });

            migrationBuilder.CreateIndex(
                name: "IX_SummonerMatchFacts_SummonerId_MatchId",
                table: "SummonerMatchFacts",
                columns: new[] { "SummonerId", "MatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SummonerMatchFacts_SummonerId_SeasonKey_QueueFamily",
                table: "SummonerMatchFacts",
                columns: new[] { "SummonerId", "SeasonKey", "QueueFamily" });

            migrationBuilder.CreateIndex(
                name: "IX_SummonerMatchFacts_SummonerId_SeasonKey_QueueId_CountsTowar~",
                table: "SummonerMatchFacts",
                columns: new[] { "SummonerId", "SeasonKey", "QueueId", "CountsTowardRankedTotal" });

            migrationBuilder.CreateIndex(
                name: "IX_SummonerSeasonChampionStats_SummonerId_SeasonKey_QueueScop~1",
                table: "SummonerSeasonChampionStats",
                columns: new[] { "SummonerId", "SeasonKey", "QueueScope", "Games" });

            migrationBuilder.CreateIndex(
                name: "IX_SummonerSeasonChampionStats_SummonerId_SeasonKey_QueueScope~",
                table: "SummonerSeasonChampionStats",
                columns: new[] { "SummonerId", "SeasonKey", "QueueScope", "ChampionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SummonerSeasonCoverages_SummonerId_SeasonKey_QueueScope",
                table: "SummonerSeasonCoverages",
                columns: new[] { "SummonerId", "SeasonKey", "QueueScope" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SummonerSeasonOverviewStats_SummonerId_SeasonKey_QueueScope",
                table: "SummonerSeasonOverviewStats",
                columns: new[] { "SummonerId", "SeasonKey", "QueueScope" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RankedSeasons");

            migrationBuilder.DropTable(
                name: "SummonerFullHistoryBackfills");

            migrationBuilder.DropTable(
                name: "SummonerMatchFactFetchFailures");

            migrationBuilder.DropTable(
                name: "SummonerMatchFacts");

            migrationBuilder.DropTable(
                name: "SummonerSeasonChampionStats");

            migrationBuilder.DropTable(
                name: "SummonerSeasonCoverages");

            migrationBuilder.DropTable(
                name: "SummonerSeasonOverviewStats");

            migrationBuilder.DropColumn(
                name: "GameEndedInEarlySurrender",
                table: "MatchParticipants");

            migrationBuilder.DropColumn(
                name: "GameEndedInSurrender",
                table: "MatchParticipants");

            migrationBuilder.DropColumn(
                name: "TeamEarlySurrendered",
                table: "MatchParticipants");
        }
    }
}
