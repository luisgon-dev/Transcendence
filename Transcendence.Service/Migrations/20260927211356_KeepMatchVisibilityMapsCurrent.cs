using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class KeepMatchVisibilityMapsCurrent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Analytics reads the current patch through covering indexes, and an index-only scan still
            // visits the heap for any page the visibility map does not mark all-visible -- which is
            // exactly the newest pages, since insert-driven autovacuum waited for 20% table growth.
            // On prod that turned one synergy query into ~15K scattered heap reads; after a vacuum it
            // made 627. Vacuuming a mostly all-visible table skips the visible pages, so frequent runs
            // are cheap. MatchParticipants' update/analyze values were already set by hand on prod and
            // are recorded here unchanged.
            migrationBuilder.Sql("""
                ALTER TABLE "MatchParticipants" SET (
                    autovacuum_vacuum_insert_threshold = 10000, autovacuum_vacuum_insert_scale_factor = 0.005,
                    autovacuum_vacuum_threshold = 50000, autovacuum_vacuum_scale_factor = 0.02,
                    autovacuum_analyze_threshold = 25000, autovacuum_analyze_scale_factor = 0.01);
                """);
            // Matches rows are updated as their fetch status changes, which clears their pages' bits.
            migrationBuilder.Sql("""
                ALTER TABLE "Matches" SET (
                    autovacuum_vacuum_insert_threshold = 1000, autovacuum_vacuum_insert_scale_factor = 0.01,
                    autovacuum_vacuum_threshold = 1000, autovacuum_vacuum_scale_factor = 0.01,
                    autovacuum_analyze_scale_factor = 0.02);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "MatchParticipants" RESET (autovacuum_vacuum_insert_threshold, autovacuum_vacuum_insert_scale_factor);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE "Matches" RESET (autovacuum_vacuum_insert_threshold, autovacuum_vacuum_insert_scale_factor, autovacuum_vacuum_threshold);
                ALTER TABLE "Matches" SET (autovacuum_vacuum_scale_factor = 0.05);
                """);
        }
    }
}
