using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddSummonerLegacyRiotIdIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Summoners is written continuously, so both are built CONCURRENTLY; each covers only the
            // ~235K legacy rows with no normalized key, so the builds are short.
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_Summoners_LegacyRiotId\" " +
                "ON \"Summoners\" (\"PlatformRegion\", \"GameName\", \"TagLine\") " +
                "WHERE \"GameNameNormalized\" IS NULL;",
                suppressTransaction: true);
            // The case-insensitive legacy fallback compares upper(GameName)/upper(TagLine); EF cannot
            // model an expression index, so this one lives only here.
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_Summoners_LegacyRiotIdUpper\" " +
                "ON \"Summoners\" (\"PlatformRegion\", upper(\"GameName\"), upper(\"TagLine\")) " +
                "WHERE \"GameNameNormalized\" IS NULL;",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Summoners_LegacyRiotIdUpper\";", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Summoners_LegacyRiotId\";", suppressTransaction: true);
        }
    }
}
