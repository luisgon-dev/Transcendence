using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceHardeningNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Summoners_Puuid",
                table: "Summoners");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MatchParticipantItems",
                table: "MatchParticipantItems");

            migrationBuilder.AddColumn<string>(
                name: "GameNameNormalized",
                table: "Summoners",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagLineNormalized",
                table: "Summoners",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SlotIndex",
                table: "MatchParticipantItems",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Summoners"
                SET "GameNameNormalized" = CASE
                    WHEN "GameName" IS NULL OR btrim("GameName") = '' THEN NULL
                    ELSE upper(btrim("GameName"))
                END,
                    "TagLineNormalized" = CASE
                    WHEN "TagLine" IS NULL OR btrim("TagLine") = '' THEN NULL
                    ELSE upper(btrim("TagLine"))
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TEMP TABLE tmp_summoner_dedupe_map AS
                SELECT "Id" AS old_id, keep_id
                FROM (
                    SELECT
                        "Id",
                        first_value("Id") OVER (
                            PARTITION BY "Puuid"
                            ORDER BY "UpdatedAt" DESC, "Id" DESC
                        ) AS keep_id,
                        row_number() OVER (
                            PARTITION BY "Puuid"
                            ORDER BY "UpdatedAt" DESC, "Id" DESC
                        ) AS rn
                    FROM "Summoners"
                    WHERE "Puuid" IS NOT NULL AND btrim("Puuid") <> ''
                ) ranked
                WHERE ranked.rn > 1;

                DELETE FROM "MatchParticipants" mp
                USING tmp_summoner_dedupe_map map, "MatchParticipants" keep_mp
                WHERE mp."SummonerId" = map.old_id
                  AND keep_mp."SummonerId" = map.keep_id
                  AND keep_mp."MatchId" = mp."MatchId";

                DELETE FROM "Ranks" r
                USING tmp_summoner_dedupe_map map, "Ranks" keep_r
                WHERE r."SummonerId" = map.old_id
                  AND keep_r."SummonerId" = map.keep_id
                  AND keep_r."QueueType" = r."QueueType";

                DELETE FROM "MatchSummoner" ms
                USING tmp_summoner_dedupe_map map, "MatchSummoner" keep_ms
                WHERE ms."SummonersId" = map.old_id
                  AND keep_ms."SummonersId" = map.keep_id
                  AND keep_ms."MatchesId" = ms."MatchesId";

                UPDATE "MatchParticipants" mp
                SET "SummonerId" = map.keep_id
                FROM tmp_summoner_dedupe_map map
                WHERE mp."SummonerId" = map.old_id;

                UPDATE "Ranks" r
                SET "SummonerId" = map.keep_id
                FROM tmp_summoner_dedupe_map map
                WHERE r."SummonerId" = map.old_id;

                UPDATE "HistoricalRanks" hr
                SET "SummonerId" = map.keep_id
                FROM tmp_summoner_dedupe_map map
                WHERE hr."SummonerId" = map.old_id;

                UPDATE "LiveGameSnapshots" lgs
                SET "SummonerId" = map.keep_id
                FROM tmp_summoner_dedupe_map map
                WHERE lgs."SummonerId" = map.old_id;

                UPDATE "MatchSummoner" ms
                SET "SummonersId" = map.keep_id
                FROM tmp_summoner_dedupe_map map
                WHERE ms."SummonersId" = map.old_id;

                DELETE FROM "Summoners" s
                USING tmp_summoner_dedupe_map map
                WHERE s."Id" = map.old_id;

                DROP TABLE tmp_summoner_dedupe_map;
                """);

            migrationBuilder.Sql("""
                CREATE TEMP TABLE tmp_summoner_dedupe_map AS
                SELECT "Id" AS old_id, keep_id
                FROM (
                    SELECT
                        "Id",
                        first_value("Id") OVER (
                            PARTITION BY "PlatformRegion", "GameNameNormalized", "TagLineNormalized"
                            ORDER BY "UpdatedAt" DESC, "Id" DESC
                        ) AS keep_id,
                        row_number() OVER (
                            PARTITION BY "PlatformRegion", "GameNameNormalized", "TagLineNormalized"
                            ORDER BY "UpdatedAt" DESC, "Id" DESC
                        ) AS rn
                    FROM "Summoners"
                    WHERE "PlatformRegion" IS NOT NULL AND btrim("PlatformRegion") <> ''
                      AND "GameNameNormalized" IS NOT NULL AND btrim("GameNameNormalized") <> ''
                      AND "TagLineNormalized" IS NOT NULL AND btrim("TagLineNormalized") <> ''
                ) ranked
                WHERE ranked.rn > 1;

                DELETE FROM "MatchParticipants" mp
                USING tmp_summoner_dedupe_map map, "MatchParticipants" keep_mp
                WHERE mp."SummonerId" = map.old_id
                  AND keep_mp."SummonerId" = map.keep_id
                  AND keep_mp."MatchId" = mp."MatchId";

                DELETE FROM "Ranks" r
                USING tmp_summoner_dedupe_map map, "Ranks" keep_r
                WHERE r."SummonerId" = map.old_id
                  AND keep_r."SummonerId" = map.keep_id
                  AND keep_r."QueueType" = r."QueueType";

                DELETE FROM "MatchSummoner" ms
                USING tmp_summoner_dedupe_map map, "MatchSummoner" keep_ms
                WHERE ms."SummonersId" = map.old_id
                  AND keep_ms."SummonersId" = map.keep_id
                  AND keep_ms."MatchesId" = ms."MatchesId";

                UPDATE "MatchParticipants" mp
                SET "SummonerId" = map.keep_id
                FROM tmp_summoner_dedupe_map map
                WHERE mp."SummonerId" = map.old_id;

                UPDATE "Ranks" r
                SET "SummonerId" = map.keep_id
                FROM tmp_summoner_dedupe_map map
                WHERE r."SummonerId" = map.old_id;

                UPDATE "HistoricalRanks" hr
                SET "SummonerId" = map.keep_id
                FROM tmp_summoner_dedupe_map map
                WHERE hr."SummonerId" = map.old_id;

                UPDATE "LiveGameSnapshots" lgs
                SET "SummonerId" = map.keep_id
                FROM tmp_summoner_dedupe_map map
                WHERE lgs."SummonerId" = map.old_id;

                UPDATE "MatchSummoner" ms
                SET "SummonersId" = map.keep_id
                FROM tmp_summoner_dedupe_map map
                WHERE ms."SummonersId" = map.old_id;

                DELETE FROM "Summoners" s
                USING tmp_summoner_dedupe_map map
                WHERE s."Id" = map.old_id;

                DROP TABLE tmp_summoner_dedupe_map;
                """);

            migrationBuilder.Sql("""
                WITH numbered AS (
                    SELECT
                        ctid,
                        row_number() OVER (
                            PARTITION BY "MatchParticipantId"
                            ORDER BY "ItemId"
                        ) - 1 AS slot_index
                    FROM "MatchParticipantItems"
                )
                UPDATE "MatchParticipantItems" mpi
                SET "SlotIndex" = numbered.slot_index
                FROM numbered
                WHERE mpi.ctid = numbered.ctid;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "SlotIndex",
                table: "MatchParticipantItems",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_MatchParticipantItems",
                table: "MatchParticipantItems",
                columns: new[] { "MatchParticipantId", "SlotIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_Summoners_PlatformRegion_GameNameNormalized_TagLineNormaliz~",
                table: "Summoners",
                columns: new[] { "PlatformRegion", "GameNameNormalized", "TagLineNormalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Summoners_Puuid",
                table: "Summoners",
                column: "Puuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipantItems_MatchParticipantId_ItemId",
                table: "MatchParticipantItems",
                columns: new[] { "MatchParticipantId", "ItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Summoners_PlatformRegion_GameNameNormalized_TagLineNormaliz~",
                table: "Summoners");

            migrationBuilder.DropIndex(
                name: "IX_Summoners_Puuid",
                table: "Summoners");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MatchParticipantItems",
                table: "MatchParticipantItems");

            migrationBuilder.DropIndex(
                name: "IX_MatchParticipantItems_MatchParticipantId_ItemId",
                table: "MatchParticipantItems");

            migrationBuilder.DropColumn(
                name: "GameNameNormalized",
                table: "Summoners");

            migrationBuilder.DropColumn(
                name: "TagLineNormalized",
                table: "Summoners");

            migrationBuilder.DropColumn(
                name: "SlotIndex",
                table: "MatchParticipantItems");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MatchParticipantItems",
                table: "MatchParticipantItems",
                columns: new[] { "MatchParticipantId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_Summoners_Puuid",
                table: "Summoners",
                column: "Puuid");
        }
    }
}
