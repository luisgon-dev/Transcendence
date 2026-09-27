using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildLabCoveringReadIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Covering index for Build Lab's read path, so a request is an index-only scan.
            //
            // The refresher inserts a champion's rows a few per batch across the whole heap, so one
            // request's ~300 rows sat on ~400 separate heap pages. Warm that is 6ms; cold, on this
            // box's spinning disk, it was seconds per read, and a page view makes about seven of them
            // (its section plus the recommended-build summary) -- 26-71s on prod while the weekly
            // archive was streaming, which the web page gave up on. Sorted by the read key and carrying
            // every column the read selects, the same request is a few adjacent index pages.
            //
            // CONCURRENTLY because the refresher writes this table every 15 minutes; suppressTransaction
            // because CONCURRENTLY cannot run inside one.
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_BuildLabOptionStats_Read\" " +
                "ON \"BuildLabOptionStats\" (\"ChampionId\", \"Role\", \"OpponentChampionId\", \"Region\", \"PrefixHash\", \"Family\") " +
                "INCLUDE (\"Stage\", \"Patch\", \"ActionKey\", \"GoldBucket\", \"Games\", \"Wins\", \"TimingSecondsSum\");",
                suppressTransaction: true);

            // Index-only scans skip the heap only for pages the visibility map marks all-visible, and
            // every refresh run updates the active patch's rows. Vacuuming after ~1% of the table
            // changes -- roughly each run -- keeps those pages visible between runs.
            migrationBuilder.Sql(
                "ALTER TABLE \"BuildLabOptionStats\" SET (autovacuum_vacuum_scale_factor = 0.01, " +
                "autovacuum_vacuum_threshold = 10000, autovacuum_analyze_scale_factor = 0.02);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"BuildLabOptionStats\" RESET (autovacuum_vacuum_scale_factor, " +
                "autovacuum_vacuum_threshold, autovacuum_analyze_scale_factor);");
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_BuildLabOptionStats_Read\";",
                suppressTransaction: true);
        }
    }
}
