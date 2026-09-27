using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildLabCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BuildLabCoverage",
                columns: table => new
                {
                    Patch = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Region = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Matches = table.Column<long>(type: "bigint", nullable: false),
                    LastCountedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BuildLabCoverage", x => new { x.Patch, x.Region });
                });

            // Seed from the ledger once (under a second on prod); the refresher keeps it from here.
            // LEFT JOIN so the ALL total still counts a ledgered match whose row has since gone,
            // exactly as the read it replaces counted the ledger.
            migrationBuilder.Sql("""
                INSERT INTO "BuildLabCoverage" ("Patch", "Region", "Matches", "LastCountedAtUtc")
                SELECT processed."Patch", scope.region, count(*), max(processed."ProcessedAtUtc")
                FROM "BuildLabProcessedMatches" processed
                LEFT JOIN "Matches" match ON match."Id" = processed."MatchId"
                CROSS JOIN LATERAL (VALUES ('ALL'), (NULLIF(match."PlatformRegion", ''))) AS scope(region)
                WHERE scope.region IS NOT NULL
                GROUP BY processed."Patch", scope.region;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BuildLabCoverage");
        }
    }
}
