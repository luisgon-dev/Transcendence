using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineSnapshotsAndIngestionCursor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ParticipantId",
                table: "MatchParticipants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MatchParticipantTimelineSnapshots",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    MinuteMark = table.Column<int>(type: "integer", nullable: false),
                    Gold = table.Column<int>(type: "integer", nullable: false),
                    Xp = table.Column<int>(type: "integer", nullable: false),
                    Cs = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    FrameTimestampMs = table.Column<int>(type: "integer", nullable: false),
                    DerivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    QualityFlags = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchParticipantTimelineSnapshots", x => new { x.MatchId, x.ParticipantId, x.MinuteMark });
                    table.ForeignKey(
                        name: "FK_MatchParticipantTimelineSnapshots_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchTimelineFetchStates",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSuccessAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    SourcePatch = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchTimelineFetchStates", x => x.MatchId);
                    table.ForeignKey(
                        name: "FK_MatchTimelineFetchStates_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SummonerIngestionCursors",
                columns: table => new
                {
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BackfillBeforeEpochSeconds = table.Column<long>(type: "bigint", nullable: true),
                    LastRunAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsecutiveNoopRuns = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SummonerIngestionCursors", x => new { x.SummonerId, x.Scope });
                    table.ForeignKey(
                        name: "FK_SummonerIngestionCursors_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipants_MatchId_ParticipantId",
                table: "MatchParticipants",
                columns: new[] { "MatchId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipantTimelineSnapshots_MinuteMark_MatchId",
                table: "MatchParticipantTimelineSnapshots",
                columns: new[] { "MinuteMark", "MatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchTimelineFetchStates_LastAttemptAtUtc",
                table: "MatchTimelineFetchStates",
                column: "LastAttemptAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MatchTimelineFetchStates_Status",
                table: "MatchTimelineFetchStates",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SummonerIngestionCursors_UpdatedAtUtc",
                table: "SummonerIngestionCursors",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchParticipantTimelineSnapshots");

            migrationBuilder.DropTable(
                name: "MatchTimelineFetchStates");

            migrationBuilder.DropTable(
                name: "SummonerIngestionCursors");

            migrationBuilder.DropIndex(
                name: "IX_MatchParticipants_MatchId_ParticipantId",
                table: "MatchParticipants");

            migrationBuilder.DropColumn(
                name: "ParticipantId",
                table: "MatchParticipants");
        }
    }
}
