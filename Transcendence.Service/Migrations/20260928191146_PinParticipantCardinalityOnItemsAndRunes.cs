using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <summary>
    /// Pins how many participants the item and rune tables hold, so reads by
    /// <c>"MatchParticipantId" = ANY(ids)</c> use the primary key.
    ///
    /// A sampled ANALYZE cannot estimate n_distinct on a column whose rows are stored together (a
    /// participant's runes and items land in one place), so the planner believed the 48M-row rune table
    /// held 216K participants (~5M) and the 32M-row item table 551K. It then expected hundreds of rows
    /// per id and scanned the tables in full: the Build Atlas refresh read ~4 GB of runes per batch,
    /// over half of prod's disk reads. A negative n_distinct is a ratio, 1 / rows-per-participant
    /// (8.5 runes, 6.2 items, measured on prod), so it stays right as the tables grow.
    /// Both statements take SHARE UPDATE EXCLUSIVE, which does not block ingestion writes; the
    /// ANALYZEs sample, so they finish in seconds and make the pin take effect now.
    /// </summary>
    public partial class PinParticipantCardinalityOnItemsAndRunes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "MatchParticipantRunes" ALTER COLUMN "MatchParticipantId" SET (n_distinct = -0.117);
                ALTER TABLE "MatchParticipantItems" ALTER COLUMN "MatchParticipantId" SET (n_distinct = -0.163);
                ALTER TABLE "MatchParticipantItems"
                    SET (autovacuum_analyze_scale_factor = 0.01, autovacuum_analyze_threshold = 50000);
                ANALYZE "MatchParticipantRunes";
                ANALYZE "MatchParticipantItems";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "MatchParticipantRunes" ALTER COLUMN "MatchParticipantId" RESET (n_distinct);
                ALTER TABLE "MatchParticipantItems" ALTER COLUMN "MatchParticipantId" RESET (n_distinct);
                ALTER TABLE "MatchParticipantItems" RESET (autovacuum_analyze_scale_factor, autovacuum_analyze_threshold);
                """);
        }
    }
}
