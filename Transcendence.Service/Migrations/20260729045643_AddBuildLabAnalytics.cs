using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildLabAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentGold",
                table: "MatchParticipantTimelineSnapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "JungleCs",
                table: "MatchParticipantTimelineSnapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LaneCs",
                table: "MatchParticipantTimelineSnapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "BuildLabGenerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RankScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DatasetVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StaticDataVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CodeRevision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IncludedPatchesJson = table.Column<string>(type: "jsonb", nullable: false),
                    IncludedRegionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SourceCutoffUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MatchCount = table.Column<long>(type: "bigint", nullable: false),
                    ArtifactUri = table.Column<string>(type: "text", nullable: true),
                    ArtifactSha256 = table.Column<string>(type: "text", nullable: true),
                    ArtifactManifestJson = table.Column<string>(type: "text", nullable: false),
                    ValidationMetricsJson = table.Column<string>(type: "jsonb", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LeaseAcquiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HeartbeatAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PromotionHistoryJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PromotedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildLabGenerations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatchParticipantItemEvents",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    EventIndex = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    TimestampMs = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: true),
                    BeforeId = table.Column<int>(type: "integer", nullable: true),
                    AfterId = table.Column<int>(type: "integer", nullable: true),
                    IsBuildRelevant = table.Column<bool>(type: "boolean", nullable: false),
                    BuildCategory = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchParticipantItemEvents", x => new { x.MatchId, x.ParticipantId, x.EventIndex });
                    table.ForeignKey(
                        name: "FK_MatchParticipantItemEvents_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchParticipantRankContexts",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    Tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Division = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    LeaguePoints = table.Column<int>(type: "integer", nullable: true),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ObservationOffsetSeconds = table.Column<long>(type: "bigint", nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchParticipantRankContexts", x => new { x.MatchId, x.ParticipantId });
                    table.ForeignKey(
                        name: "FK_MatchParticipantRankContexts_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchTimelineEventPayloads",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventIndex = table.Column<int>(type: "integer", nullable: false),
                    TimestampMs = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchTimelineEventPayloads", x => new { x.MatchId, x.EventIndex });
                    table.ForeignKey(
                        name: "FK_MatchTimelineEventPayloads_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSavedBuilds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OpponentChampionId = table.Column<int>(type: "integer", nullable: true),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RankingMode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    ItemPathJson = table.Column<string>(type: "jsonb", nullable: false),
                    RuneSelectionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Spell1Id = table.Column<int>(type: "integer", nullable: true),
                    Spell2Id = table.Column<int>(type: "integer", nullable: true),
                    SourceGenerationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceIsPublishable = table.Column<bool>(type: "boolean", nullable: true),
                    SourceAdjustedLift = table.Column<double>(type: "double precision", nullable: true),
                    ShareId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSavedBuilds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSavedBuilds_UserAccounts_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdjustedActionEstimates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OpponentChampionId = table.Column<int>(type: "integer", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RegionScope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DecisionFamily = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    PathPrefixHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PathPrefixJson = table.Column<string>(type: "jsonb", nullable: false),
                    ActionKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActionIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    AdjustedWpa = table.Column<double>(type: "double precision", nullable: true),
                    ConfidenceLow = table.Column<double>(type: "double precision", nullable: true),
                    ConfidenceHigh = table.Column<double>(type: "double precision", nullable: true),
                    RawWinRate = table.Column<double>(type: "double precision", nullable: false),
                    PickRate = table.Column<double>(type: "double precision", nullable: false),
                    ObservedCount = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveSampleSize = table.Column<double>(type: "double precision", nullable: false),
                    AverageTimingMinutes = table.Column<double>(type: "double precision", nullable: true),
                    PropensityOverlap = table.Column<double>(type: "double precision", nullable: false),
                    CovariateBalance = table.Column<double>(type: "double precision", nullable: false),
                    StableAcrossFolds = table.Column<bool>(type: "boolean", nullable: false),
                    IsPublishable = table.Column<bool>(type: "boolean", nullable: false),
                    EvidenceQuality = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    FallbackScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BaselineDefinition = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    UnavailableReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ComputedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdjustedActionEstimates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdjustedActionEstimates_BuildLabGenerations_GenerationId",
                        column: x => x.GenerationId,
                        principalTable: "BuildLabGenerations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AdjustedPathEstimates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GenerationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OpponentChampionId = table.Column<int>(type: "integer", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RegionScope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PathHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ItemPathJson = table.Column<string>(type: "jsonb", nullable: false),
                    EstimatedWinProbability = table.Column<double>(type: "double precision", nullable: true),
                    AdjustedLift = table.Column<double>(type: "double precision", nullable: true),
                    ConfidenceLow = table.Column<double>(type: "double precision", nullable: true),
                    ConfidenceHigh = table.Column<double>(type: "double precision", nullable: true),
                    ObservedCount = table.Column<long>(type: "bigint", nullable: false),
                    EffectiveSampleSize = table.Column<double>(type: "double precision", nullable: false),
                    IsPublishable = table.Column<bool>(type: "boolean", nullable: false),
                    UnavailableReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdjustedPathEstimates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdjustedPathEstimates_BuildLabGenerations_GenerationId",
                        column: x => x.GenerationId,
                        principalTable: "BuildLabGenerations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdjustedActionEstimates_GenerationId_ChampionId_Role_Decisi~",
                table: "AdjustedActionEstimates",
                columns: new[] { "GenerationId", "ChampionId", "Role", "DecisionFamily", "Stage", "PathPrefixHash" });

            migrationBuilder.CreateIndex(
                name: "IX_AdjustedActionEstimates_GenerationId_ChampionId_Role_Oppone~",
                table: "AdjustedActionEstimates",
                columns: new[] { "GenerationId", "ChampionId", "Role", "OpponentChampionId", "RegionScope", "DecisionFamily", "Stage", "PathPrefixHash", "ActionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdjustedPathEstimates_GenerationId_ChampionId_Role_Opponent~",
                table: "AdjustedPathEstimates",
                columns: new[] { "GenerationId", "ChampionId", "Role", "OpponentChampionId", "RegionScope", "PathHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildLabGenerations_IsActive",
                table: "BuildLabGenerations",
                column: "IsActive",
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_BuildLabGenerations_Patch_Status_CompletedAtUtc",
                table: "BuildLabGenerations",
                columns: new[] { "Patch", "Status", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BuildLabGenerations_Status_LeaseExpiresAtUtc",
                table: "BuildLabGenerations",
                columns: new[] { "Status", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipantItemEvents_MatchId_ParticipantId_TimestampMs",
                table: "MatchParticipantItemEvents",
                columns: new[] { "MatchId", "ParticipantId", "TimestampMs" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipantRankContexts_Tier_ObservedAtUtc",
                table: "MatchParticipantRankContexts",
                columns: new[] { "Tier", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchTimelineEventPayloads_MatchId_EventType_TimestampMs",
                table: "MatchTimelineEventPayloads",
                columns: new[] { "MatchId", "EventType", "TimestampMs" });

            migrationBuilder.CreateIndex(
                name: "IX_UserSavedBuilds_ShareId",
                table: "UserSavedBuilds",
                column: "ShareId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSavedBuilds_UserAccountId_UpdatedAtUtc",
                table: "UserSavedBuilds",
                columns: new[] { "UserAccountId", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdjustedActionEstimates");

            migrationBuilder.DropTable(
                name: "AdjustedPathEstimates");

            migrationBuilder.DropTable(
                name: "MatchParticipantItemEvents");

            migrationBuilder.DropTable(
                name: "MatchParticipantRankContexts");

            migrationBuilder.DropTable(
                name: "MatchTimelineEventPayloads");

            migrationBuilder.DropTable(
                name: "UserSavedBuilds");

            migrationBuilder.DropTable(
                name: "BuildLabGenerations");

            migrationBuilder.DropColumn(
                name: "CurrentGold",
                table: "MatchParticipantTimelineSnapshots");

            migrationBuilder.DropColumn(
                name: "JungleCs",
                table: "MatchParticipantTimelineSnapshots");

            migrationBuilder.DropColumn(
                name: "LaneCs",
                table: "MatchParticipantTimelineSnapshots");
        }
    }
}
