using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceBuildLabWithAdditiveStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdjustedActionEstimates");

            migrationBuilder.DropTable(
                name: "AdjustedPathEstimates");

            migrationBuilder.DropTable(
                name: "UserSavedBuilds");

            migrationBuilder.DropTable(
                name: "BuildLabGenerations");

            migrationBuilder.CreateTable(
                name: "BuildLabOptionStats",
                columns: table => new
                {
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OpponentChampionId = table.Column<int>(type: "integer", nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PrefixHash = table.Column<long>(type: "bigint", nullable: false),
                    Family = table.Column<short>(type: "smallint", nullable: false),
                    Stage = table.Column<short>(type: "smallint", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActionKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GoldBucket = table.Column<short>(type: "smallint", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    TimingSecondsSum = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildLabOptionStats", x => new { x.ChampionId, x.Role, x.OpponentChampionId, x.Region, x.PrefixHash, x.Family, x.Stage, x.Patch, x.ActionKey, x.GoldBucket });
                });

            migrationBuilder.CreateTable(
                name: "BuildLabProcessedMatches",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildLabProcessedMatches", x => x.MatchId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BuildLabProcessedMatches_Patch",
                table: "BuildLabProcessedMatches",
                column: "Patch");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildLabOptionStats");

            migrationBuilder.DropTable(
                name: "BuildLabProcessedMatches");

            migrationBuilder.CreateTable(
                name: "BuildLabGenerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtifactManifestJson = table.Column<string>(type: "text", nullable: false),
                    ArtifactSha256 = table.Column<string>(type: "text", nullable: true),
                    ArtifactUri = table.Column<string>(type: "text", nullable: true),
                    CodeRevision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DatasetVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    IncludedPatchesJson = table.Column<string>(type: "jsonb", nullable: false),
                    IncludedRegionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    MatchCount = table.Column<long>(type: "bigint", nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PromotedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PromotionHistoryJson = table.Column<string>(type: "jsonb", nullable: false),
                    RankScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RetiredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceCutoffUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StaticDataVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ValidationMetricsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildLabGenerations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSavedBuilds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ItemPathJson = table.Column<string>(type: "jsonb", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OpponentChampionId = table.Column<int>(type: "integer", nullable: true),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RankingMode = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RuneSelectionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ShareId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceAdjustedLift = table.Column<double>(type: "double precision", nullable: true),
                    SourceGenerationId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceIsPublishable = table.Column<bool>(type: "boolean", nullable: true),
                    Spell1Id = table.Column<int>(type: "integer", nullable: true),
                    Spell2Id = table.Column<int>(type: "integer", nullable: true),
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
                    ActionIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ActionKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AdjustedWpa = table.Column<double>(type: "double precision", nullable: true),
                    AverageTimingMinutes = table.Column<double>(type: "double precision", nullable: true),
                    BaselineDefinition = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false, defaultValue: ""),
                    BucketConfidence = table.Column<double>(type: "double precision", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfidenceHigh = table.Column<double>(type: "double precision", nullable: true),
                    ConfidenceLow = table.Column<double>(type: "double precision", nullable: true),
                    CovariateBalance = table.Column<double>(type: "double precision", nullable: false),
                    DecisionFamily = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    EffectiveSampleSize = table.Column<double>(type: "double precision", nullable: false),
                    EvidenceBucket = table.Column<string>(type: "text", nullable: false),
                    EvidenceQuality = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    EvidenceTier = table.Column<int>(type: "integer", nullable: false),
                    FallbackScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsPublishable = table.Column<bool>(type: "boolean", nullable: false),
                    ObservedCount = table.Column<long>(type: "bigint", nullable: false),
                    OpponentChampionId = table.Column<int>(type: "integer", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PathPrefixHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PathPrefixJson = table.Column<string>(type: "jsonb", nullable: false),
                    PickRate = table.Column<double>(type: "double precision", nullable: false),
                    PropensityOverlap = table.Column<double>(type: "double precision", nullable: false),
                    RawWinRate = table.Column<double>(type: "double precision", nullable: false),
                    RegionScope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StableAcrossFolds = table.Column<bool>(type: "boolean", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    UnavailableReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
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
                    AdjustedLift = table.Column<double>(type: "double precision", nullable: true),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    ConfidenceHigh = table.Column<double>(type: "double precision", nullable: true),
                    ConfidenceLow = table.Column<double>(type: "double precision", nullable: true),
                    EffectiveSampleSize = table.Column<double>(type: "double precision", nullable: false),
                    EstimatedWinProbability = table.Column<double>(type: "double precision", nullable: true),
                    IsPublishable = table.Column<bool>(type: "boolean", nullable: false),
                    ItemPathJson = table.Column<string>(type: "jsonb", nullable: false),
                    ObservedCount = table.Column<long>(type: "bigint", nullable: false),
                    OpponentChampionId = table.Column<int>(type: "integer", nullable: false),
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PathHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RegionScope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                name: "IX_BuildLabGenerations_Status",
                table: "BuildLabGenerations",
                column: "Status");

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
    }
}
