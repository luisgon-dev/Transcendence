using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineSnapshotMatchupCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hot table (MatchParticipantTimelineSnapshots, ~22.5M rows, continuously written by the
            // timeline ingestion/backfill jobs). Build CONCURRENTLY so it never holds a write-blocking
            // SHARE lock for the full build; CONCURRENTLY cannot run inside a transaction, hence
            // suppressTransaction. **Apply out-of-band on prod** (NOT via `database update`) and in a
            // top-of-hour gap — a 22.5M-row build on the HDD must not compete with the `:00` warm storm.
            // See docs/DEVELOPMENT.md "Applying index migrations to hot tables".
            //
            // Raw SQL (not EF CreateIndex) for two reasons: (1) EF's CreateIndex emits a locking plain
            // CREATE INDEX; (2) the INCLUDE payload is Npgsql-specific and Transcendence.Data references
            // only EF.Relational, so the model carries the bare (MatchId, ParticipantId, MinuteMark)
            // shape and the INCLUDE columns live here.
            //
            // The key columns mirror the PK, so the matchup join key was already seekable — the win is
            // the INCLUDE payload. ChampionAnalyticsComputeService.ComputeMatchupsAsync LEFT-joins this
            // table twice (championTimeline + opponentTimeline) on (MatchId, ParticipantId) with
            // MinuteMark == 15: opponentTimeline reads Gold/Xp, championTimeline reads Gold/Xp plus
            // DerivedAtUtc (for LatestTimelineAtUtc). INCLUDE (Gold, Xp, DerivedAtUtc) lets BOTH scans
            // run index-only — DerivedAtUtc is required or the champion-side scan still heap-fetches and
            // the INCLUDE buys nothing there. The heap fetch is what pushed the matchup query to ~28s on
            // the HDD-backed prod DB and tripped the 30s command timeout, failing a chunk of
            // WarmDefaultChampionProfilesJob every cycle.
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_MatchParticipantTimelineSnapshots_Matchup\" " +
                "ON \"MatchParticipantTimelineSnapshots\" (\"MatchId\", \"ParticipantId\", \"MinuteMark\") " +
                "INCLUDE (\"Gold\", \"Xp\", \"DerivedAtUtc\");",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_MatchParticipantTimelineSnapshots_Matchup\";",
                suppressTransaction: true);
        }
    }
}
