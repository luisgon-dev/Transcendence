using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceHardeningNormalizationV2 : Migration
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
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<List<int>>(
                name: "BuildsFrom",
                table: "ItemVersions",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{}'::integer[]");

            migrationBuilder.AddColumn<List<int>>(
                name: "BuildsInto",
                table: "ItemVersions",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{}'::integer[]");

            migrationBuilder.AddColumn<bool>(
                name: "InStore",
                table: "ItemVersions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "PriceTotal",
                table: "ItemVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_MatchParticipantItems",
                table: "MatchParticipantItems",
                columns: new[] { "MatchParticipantId", "ItemId", "SlotIndex" });

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

            migrationBuilder.DropColumn(
                name: "GameNameNormalized",
                table: "Summoners");

            migrationBuilder.DropColumn(
                name: "TagLineNormalized",
                table: "Summoners");

            migrationBuilder.DropColumn(
                name: "SlotIndex",
                table: "MatchParticipantItems");

            migrationBuilder.DropColumn(
                name: "BuildsFrom",
                table: "ItemVersions");

            migrationBuilder.DropColumn(
                name: "BuildsInto",
                table: "ItemVersions");

            migrationBuilder.DropColumn(
                name: "InStore",
                table: "ItemVersions");

            migrationBuilder.DropColumn(
                name: "PriceTotal",
                table: "ItemVersions");

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
