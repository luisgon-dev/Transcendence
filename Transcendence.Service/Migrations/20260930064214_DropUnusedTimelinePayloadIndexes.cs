using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class DropUnusedTimelinePayloadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Both served only the retired Build Lab modeler's cohort scan: 0 scans on prod, 34 GB, and
            // written for each of the ~455 payload rows every ingested match adds. CONCURRENTLY, because
            // ingestion writes this table continuously.
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_MatchTimelineEventPayloads_KillEvents\";",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_MatchTimelineEventPayloads_MatchId_EventType_TimestampMs\";",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_MatchTimelineEventPayloads_MatchId_EventType_TimestampMs\" " +
                "ON \"MatchTimelineEventPayloads\" (\"MatchId\", \"EventType\", \"TimestampMs\");",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_MatchTimelineEventPayloads_KillEvents\" " +
                "ON \"MatchTimelineEventPayloads\" (\"MatchId\", \"EventIndex\") " +
                "INCLUDE (\"TimestampMs\", \"EventType\", \"KillerId\", \"KillerTeamId\", \"TeamId\") " +
                "WHERE \"EventType\" IN ('CHAMPION_KILL', 'BUILDING_KILL', 'ELITE_MONSTER_KILL');",
                suppressTransaction: true);
        }
    }
}
