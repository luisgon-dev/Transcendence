using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchParticipantMatchupCoveringIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hot table (MatchParticipants, ~3.6M rows, continuously written by ingestion). All builds run
            // CONCURRENTLY so they never hold a write-blocking SHARE lock; CONCURRENTLY cannot run inside a
            // transaction, hence suppressTransaction. Raw SQL (not CreateIndex) because the INCLUDE payload is
            // Npgsql-specific and Transcendence.Data references only EF.Relational, so the model carries the
            // bare (cols) shape and the INCLUDE lives here. **Apply out-of-band on prod** in a top-of-hour gap
            // (NOT via `database update`) — see docs/DEVELOPMENT.md "Applying index migrations to hot tables":
            // build each *_Covering first, verify indisvalid='t', then drop the plain index it replaces.
            //
            // These turn the matchup query's (ComputeMatchupsAsync) two remaining heap-fetch bottlenecks into
            // index-only scans — the prod EXPLAIN had the lane-pairs self-join at ~+72k and the champion-side
            // scan at ~50k, both heap fetches on non-covering indexes (#69 already made the timeline joins
            // index-only).

            // Lane-pairs self-join (opponent lookup): seek by MatchId, read TeamPosition/TeamId/ChampionId/
            // ParticipantId from the leaf. Also serves the MatchId FK + every MatchId lookup (hottest index).
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_MatchParticipants_MatchId_Covering\" " +
                "ON \"MatchParticipants\" (\"MatchId\") " +
                "INCLUDE (\"TeamPosition\", \"TeamId\", \"ChampionId\", \"ParticipantId\");",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_MatchParticipants_MatchId\";",
                suppressTransaction: true);

            // Champion-side scan: seek by (ChampionId, TeamPosition), read MatchId/ParticipantId/Win/TeamId.
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_MatchParticipants_ChampionId_TeamPosition_Covering\" " +
                "ON \"MatchParticipants\" (\"ChampionId\", \"TeamPosition\") " +
                "INCLUDE (\"MatchId\", \"ParticipantId\", \"Win\", \"TeamId\");",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_MatchParticipants_ChampionId_TeamPosition\";",
                suppressTransaction: true);

            // Drop the redundant plain (ChampionId) index — 0 scans on prod; IX_..._ChampionId_Covering
            // (which leads with ChampionId) already serves any ChampionId seek. Offsets the INCLUDE growth.
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_MatchParticipants_ChampionId\";",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_MatchParticipants_ChampionId\" " +
                "ON \"MatchParticipants\" (\"ChampionId\");",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_MatchParticipants_ChampionId_TeamPosition\" " +
                "ON \"MatchParticipants\" (\"ChampionId\", \"TeamPosition\");",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_MatchParticipants_ChampionId_TeamPosition_Covering\";",
                suppressTransaction: true);

            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_MatchParticipants_MatchId\" " +
                "ON \"MatchParticipants\" (\"MatchId\");",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_MatchParticipants_MatchId_Covering\";",
                suppressTransaction: true);
        }
    }
}
