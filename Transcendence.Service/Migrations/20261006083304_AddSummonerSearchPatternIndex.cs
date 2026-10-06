using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddSummonerSearchPatternIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Serves the search autosuggest's LIKE 'PREFIX%'. text_pattern_ops is the point: under the
            // database's en_US.utf8 collation a default btree cannot serve LIKE, so search read all of
            // Summoners on every call. Summoners is written continuously, so it is built CONCURRENTLY
            // (DatabaseMigrator raises the command timeout to 30 minutes for migrations); the build reads
            // the ~1.2GB heap twice and writes a ~200MB index.
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_Summoners_SearchNamePattern\" " +
                "ON \"Summoners\" (\"PlatformRegion\", \"GameNameNormalized\" text_pattern_ops) " +
                "WHERE \"GameNameNormalized\" IS NOT NULL AND \"TagLineNormalized\" IS NOT NULL;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_Summoners_SearchNamePattern\";",
                suppressTransaction: true);
        }
    }
}
