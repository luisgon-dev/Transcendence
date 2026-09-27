using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddRankHistoryLatestIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Built CONCURRENTLY so rank refreshes keep writing while it builds (3.3M rows on prod),
            // and before the old index goes, so the SummonerId lookups are served throughout.
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_HistoricalRanks_SummonerId_QueueType_DateRecorded\" " +
                "ON \"HistoricalRanks\" (\"SummonerId\", \"QueueType\", \"DateRecorded\");",
                suppressTransaction: true);
            // Its leading column serves every SummonerId lookup the old single-column index did.
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_HistoricalRanks_SummonerId\";",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_HistoricalRanks_SummonerId\" ON \"HistoricalRanks\" (\"SummonerId\");",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_HistoricalRanks_SummonerId_QueueType_DateRecorded\";",
                suppressTransaction: true);
        }
    }
}
