using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddSummonerActivityIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hot table (Summoners, ~4M rows, continuously written by ingestion). Build the partial
            // index CONCURRENTLY so it does not hold a write-blocking SHARE lock for the full build.
            // CONCURRENTLY cannot run inside a transaction, hence suppressTransaction. (Raw SQL because
            // EF's CreateIndex emits a locking plain CREATE INDEX — see docs/DEVELOPMENT.md "Applying
            // index migrations to hot tables".) Snapshot/model carry the index via the named HasIndex.
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_Summoners_Region_UpdatedAt_Active\" " +
                "ON \"Summoners\" (\"PlatformRegion\", \"UpdatedAt\") WHERE \"LastActiveAtUtc\" IS NOT NULL;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_Summoners_Region_UpdatedAt_Active\";",
                suppressTransaction: true);
        }
    }
}
