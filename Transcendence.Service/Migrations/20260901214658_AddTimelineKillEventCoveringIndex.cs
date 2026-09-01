using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineKillEventCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Partial covering index for the modeler's cohort event scan.
            //
            // That scan reads one shape: the three kill/objective types, ordered by
            // (MatchId, EventIndex), selecting timestamp, type and the three lifted scalars. Before
            // this it was a Parallel Seq Scan of the whole 165M-row table (prod EXPLAIN cost
            // 9,493,593) plus a 9.9M-row sort, because the scalars lived in jsonb and Postgres
            // cannot satisfy a jsonb expression from an index -- so it read all 77 GB to keep ~16%.
            // Filtered and covering, the same query is an index-only scan of roughly 2 GB.
            //
            // CONCURRENTLY and out-of-band: this table is 165M rows and ingestion writes it
            // continuously, so a plain CREATE INDEX would hold SHARE for the whole build and block
            // the worker. suppressTransaction because CONCURRENTLY cannot run inside one.
            // See docs/DEVELOPMENT.md, "Applying index migrations to hot tables".
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_MatchTimelineEventPayloads_KillEvents\" " +
                "ON \"MatchTimelineEventPayloads\" (\"MatchId\", \"EventIndex\") " +
                "INCLUDE (\"TimestampMs\", \"EventType\", \"KillerId\", \"KillerTeamId\", \"TeamId\") " +
                "WHERE \"EventType\" IN ('CHAMPION_KILL', 'BUILDING_KILL', 'ELITE_MONSTER_KILL');",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_MatchTimelineEventPayloads_KillEvents\";",
                suppressTransaction: true);
        }
    }
}
