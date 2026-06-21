using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchParticipantChampionCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hot table (MatchParticipants, ~3M rows, continuously written by ingestion). Build the
            // covering index CONCURRENTLY so it never holds a write-blocking SHARE lock for the full
            // build. CONCURRENTLY cannot run inside a transaction, hence suppressTransaction.
            //
            // Raw SQL (not EF CreateIndex) for two reasons: (1) EF's CreateIndex emits a locking plain
            // CREATE INDEX — see docs/DEVELOPMENT.md "Applying index migrations to hot tables"; (2) the
            // INCLUDE payload is Npgsql-specific and Transcendence.Data references only EF.Relational,
            // so the model carries the bare (ChampionId) shape and the INCLUDE columns live here. The
            // payload (MatchId, TeamPosition, Win, SummonerId) lets the dominant champion-analytics
            // query (ChampionAnalyticsComputeService.ComputeWinRatesAsync) run an index-only scan
            // instead of a ~48k-block heap fetch.
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_MatchParticipants_ChampionId_Covering\" " +
                "ON \"MatchParticipants\" (\"ChampionId\") " +
                "INCLUDE (\"MatchId\", \"TeamPosition\", \"Win\", \"SummonerId\");",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_MatchParticipants_ChampionId_Covering\";",
                suppressTransaction: true);
        }
    }
}
