using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddTftGameModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TftAugmentVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiName = table.Column<string>(type: "text", nullable: false),
                    SetNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    AssociatedTraits = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    IncompatibleTraits = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    Unique = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftAugmentVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TftItemVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiName = table.Column<string>(type: "text", nullable: false),
                    SetNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    RiotItemId = table.Column<int>(type: "integer", nullable: true),
                    AssociatedTraits = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    IncompatibleTraits = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    Composition = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    Unique = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftItemVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TftPatches",
                columns: table => new
                {
                    Version = table.Column<string>(type: "text", nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ActiveSetNumber = table.Column<int>(type: "integer", nullable: true),
                    ActiveSetCoreName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftPatches", x => x.Version);
                });

            migrationBuilder.CreateTable(
                name: "TftSets",
                columns: table => new
                {
                    Number = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Mutator = table.Column<string>(type: "text", nullable: true),
                    CoreName = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftSets", x => x.Number);
                });

            migrationBuilder.CreateTable(
                name: "TftSummoners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RiotSummonerId = table.Column<string>(type: "text", nullable: true),
                    ProfileIconId = table.Column<int>(type: "integer", nullable: false),
                    SummonerLevel = table.Column<long>(type: "bigint", nullable: false),
                    RevisionDate = table.Column<long>(type: "bigint", nullable: false),
                    Puuid = table.Column<string>(type: "text", nullable: true),
                    GameName = table.Column<string>(type: "text", nullable: true),
                    TagLine = table.Column<string>(type: "text", nullable: true),
                    GameNameNormalized = table.Column<string>(type: "text", nullable: true),
                    TagLineNormalized = table.Column<string>(type: "text", nullable: true),
                    AccountId = table.Column<string>(type: "text", nullable: true),
                    PlatformRegion = table.Column<string>(type: "text", nullable: false),
                    Region = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftSummoners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TftTraitVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiName = table.Column<string>(type: "text", nullable: false),
                    SetNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftTraitVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TftUnitVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiName = table.Column<string>(type: "text", nullable: false),
                    SetNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CharacterName = table.Column<string>(type: "text", nullable: false),
                    Cost = table.Column<int>(type: "integer", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    SquareIcon = table.Column<string>(type: "text", nullable: true),
                    TileIcon = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<string>(type: "text", nullable: true),
                    Traits = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftUnitVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TftMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<string>(type: "text", nullable: false),
                    MatchDate = table.Column<long>(type: "bigint", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Patch = table.Column<string>(type: "text", nullable: true),
                    SetNumber = table.Column<int>(type: "integer", nullable: true),
                    SetCoreName = table.Column<string>(type: "text", nullable: true),
                    TftGameType = table.Column<string>(type: "text", nullable: true),
                    GameVariation = table.Column<string>(type: "text", nullable: true),
                    QueueId = table.Column<string>(type: "text", nullable: true),
                    PlatformRegion = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TftMatches_TftSets_SetNumber",
                        column: x => x.SetNumber,
                        principalTable: "TftSets",
                        principalColumn: "Number");
                });

            migrationBuilder.CreateTable(
                name: "TftHistoricalRanks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    QueueType = table.Column<string>(type: "text", nullable: true),
                    Tier = table.Column<string>(type: "text", nullable: true),
                    RankNumber = table.Column<string>(type: "text", nullable: true),
                    LeaguePoints = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Losses = table.Column<int>(type: "integer", nullable: false),
                    DateRecorded = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftHistoricalRanks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TftHistoricalRanks_TftSummoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "TftSummoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TftRanks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<string>(type: "text", nullable: false),
                    RankNumber = table.Column<string>(type: "text", nullable: false),
                    LeaguePoints = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Losses = table.Column<int>(type: "integer", nullable: false),
                    QueueType = table.Column<string>(type: "text", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftRanks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TftRanks_TftSummoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "TftSummoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TftSummonerIngestionCursors",
                columns: table => new
                {
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    BackfillBeforeEpochSeconds = table.Column<long>(type: "bigint", nullable: true),
                    LastRunAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsecutiveNoopRuns = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftSummonerIngestionCursors", x => new { x.SummonerId, x.Scope });
                    table.ForeignKey(
                        name: "FK_TftSummonerIngestionCursors_TftSummoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "TftSummoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TftMatchParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Puuid = table.Column<string>(type: "text", nullable: false),
                    RiotIdGameName = table.Column<string>(type: "text", nullable: true),
                    RiotIdTagLine = table.Column<string>(type: "text", nullable: true),
                    Placement = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    LastRound = table.Column<int>(type: "integer", nullable: false),
                    PlayersEliminated = table.Column<int>(type: "integer", nullable: false),
                    GoldLeft = table.Column<int>(type: "integer", nullable: false),
                    TotalDamageToPlayers = table.Column<int>(type: "integer", nullable: false),
                    TimeEliminatedSeconds = table.Column<float>(type: "real", nullable: false),
                    Win = table.Column<bool>(type: "boolean", nullable: true),
                    Augments = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    CompanionItemId = table.Column<int>(type: "integer", nullable: true),
                    CompanionSkinId = table.Column<int>(type: "integer", nullable: true),
                    CompanionSpecies = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftMatchParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TftMatchParticipants_TftMatches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "TftMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TftMatchParticipants_TftSummoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "TftSummoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TftMatchParticipantAugments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotIndex = table.Column<int>(type: "integer", nullable: false),
                    ApiName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftMatchParticipantAugments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TftMatchParticipantAugments_TftMatchParticipants_Participan~",
                        column: x => x.ParticipantId,
                        principalTable: "TftMatchParticipants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TftMatchParticipantTraits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NumUnits = table.Column<int>(type: "integer", nullable: false),
                    Style = table.Column<int>(type: "integer", nullable: true),
                    TierCurrent = table.Column<int>(type: "integer", nullable: false),
                    TierTotal = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftMatchParticipantTraits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TftMatchParticipantTraits_TftMatchParticipants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "TftMatchParticipants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TftMatchParticipantUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CharacterId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Rarity = table.Column<int>(type: "integer", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    Chosen = table.Column<string>(type: "text", nullable: true),
                    Items = table.Column<int[]>(type: "integer[]", nullable: false, defaultValueSql: "'{}'::integer[]"),
                    ItemNames = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TftMatchParticipantUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TftMatchParticipantUnits_TftMatchParticipants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "TftMatchParticipants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TftAugmentVersions_SetNumber_ApiName",
                table: "TftAugmentVersions",
                columns: new[] { "SetNumber", "ApiName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TftHistoricalRanks_SummonerId",
                table: "TftHistoricalRanks",
                column: "SummonerId");

            migrationBuilder.CreateIndex(
                name: "IX_TftItemVersions_SetNumber_ApiName",
                table: "TftItemVersions",
                columns: new[] { "SetNumber", "ApiName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TftMatches_MatchDate",
                table: "TftMatches",
                column: "MatchDate");

            migrationBuilder.CreateIndex(
                name: "IX_TftMatches_MatchId",
                table: "TftMatches",
                column: "MatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TftMatches_PlatformRegion_Patch_SetNumber_Status",
                table: "TftMatches",
                columns: new[] { "PlatformRegion", "Patch", "SetNumber", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TftMatches_SetNumber",
                table: "TftMatches",
                column: "SetNumber");

            migrationBuilder.CreateIndex(
                name: "IX_TftMatchParticipantAugments_ApiName_ParticipantId",
                table: "TftMatchParticipantAugments",
                columns: new[] { "ApiName", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_TftMatchParticipantAugments_ParticipantId",
                table: "TftMatchParticipantAugments",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_TftMatchParticipants_MatchId_SummonerId",
                table: "TftMatchParticipants",
                columns: new[] { "MatchId", "SummonerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TftMatchParticipants_Placement",
                table: "TftMatchParticipants",
                column: "Placement");

            migrationBuilder.CreateIndex(
                name: "IX_TftMatchParticipants_SummonerId_Placement",
                table: "TftMatchParticipants",
                columns: new[] { "SummonerId", "Placement" });

            migrationBuilder.CreateIndex(
                name: "IX_TftMatchParticipantTraits_Name",
                table: "TftMatchParticipantTraits",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TftMatchParticipantTraits_ParticipantId",
                table: "TftMatchParticipantTraits",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_TftMatchParticipantUnits_CharacterId",
                table: "TftMatchParticipantUnits",
                column: "CharacterId");

            migrationBuilder.CreateIndex(
                name: "IX_TftMatchParticipantUnits_ParticipantId",
                table: "TftMatchParticipantUnits",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_TftRanks_SummonerId_QueueType",
                table: "TftRanks",
                columns: new[] { "SummonerId", "QueueType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TftSummoners_PlatformRegion_GameNameNormalized_TagLineNorma~",
                table: "TftSummoners",
                columns: new[] { "PlatformRegion", "GameNameNormalized", "TagLineNormalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TftSummoners_Puuid",
                table: "TftSummoners",
                column: "Puuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TftTraitVersions_SetNumber_ApiName",
                table: "TftTraitVersions",
                columns: new[] { "SetNumber", "ApiName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TftUnitVersions_SetNumber_ApiName",
                table: "TftUnitVersions",
                columns: new[] { "SetNumber", "ApiName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TftAugmentVersions");

            migrationBuilder.DropTable(
                name: "TftHistoricalRanks");

            migrationBuilder.DropTable(
                name: "TftItemVersions");

            migrationBuilder.DropTable(
                name: "TftMatchParticipantAugments");

            migrationBuilder.DropTable(
                name: "TftMatchParticipantTraits");

            migrationBuilder.DropTable(
                name: "TftMatchParticipantUnits");

            migrationBuilder.DropTable(
                name: "TftPatches");

            migrationBuilder.DropTable(
                name: "TftRanks");

            migrationBuilder.DropTable(
                name: "TftSummonerIngestionCursors");

            migrationBuilder.DropTable(
                name: "TftTraitVersions");

            migrationBuilder.DropTable(
                name: "TftUnitVersions");

            migrationBuilder.DropTable(
                name: "TftMatchParticipants");

            migrationBuilder.DropTable(
                name: "TftMatches");

            migrationBuilder.DropTable(
                name: "TftSummoners");

            migrationBuilder.DropTable(
                name: "TftSets");
        }
    }
}
