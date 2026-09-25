using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <summary>
    /// Pins the planner's view of how many matches the Build Lab source tables hold, and keeps their
    /// statistics fresh.
    ///
    /// The Build Lab refresh reads item events and one-minute frames by <c>"MatchId" = ANY(batch)</c>.
    /// On prod the first 500-match batch took 7.5 minutes: the planner believed MatchParticipantItemEvents
    /// held 23k distinct matches (it holds ~480k), so it expected ~4.5M rows for 500 matches and chose a
    /// parallel sequential scan of 21 GB instead of the index. Two things caused that and both are fixed
    /// here:
    ///
    /// - A sampled ANALYZE cannot estimate n_distinct for a column whose rows are stored clustered
    ///   (every event of a match lands together), so the estimate stays far too low even when fresh. A
    ///   negative n_distinct is a ratio -- roughly 1 / rows-per-match -- so it stays right as the tables
    ///   grow: ~430 item events and ~270 frames per match.
    /// - The item-event and rune tables had no table-local analyze thresholds, so the global 10% scale
    ///   factor meant a 200M-row table was not re-analyzed for 20M changes (it never had been).
    ///
    /// Both statements take SHARE UPDATE EXCLUSIVE, which does not block ingestion writes. The override
    /// takes effect at the next ANALYZE, which the thresholds below now schedule.
    /// </summary>
    public partial class PinBuildLabSourceTableStatistics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "MatchParticipantItemEvents" ALTER COLUMN "MatchId" SET (n_distinct = -0.0023);
                ALTER TABLE "MatchParticipantTimelineSnapshots" ALTER COLUMN "MatchId" SET (n_distinct = -0.0037);
                ALTER TABLE "MatchParticipantItemEvents"
                    SET (autovacuum_analyze_scale_factor = 0.005, autovacuum_analyze_threshold = 50000);
                ALTER TABLE "MatchParticipantRunes"
                    SET (autovacuum_analyze_scale_factor = 0.01, autovacuum_analyze_threshold = 50000);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "MatchParticipantItemEvents" ALTER COLUMN "MatchId" RESET (n_distinct);
                ALTER TABLE "MatchParticipantTimelineSnapshots" ALTER COLUMN "MatchId" RESET (n_distinct);
                ALTER TABLE "MatchParticipantItemEvents"
                    RESET (autovacuum_analyze_scale_factor, autovacuum_analyze_threshold);
                ALTER TABLE "MatchParticipantRunes"
                    RESET (autovacuum_analyze_scale_factor, autovacuum_analyze_threshold);
                """);
        }
    }
}
