using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class MakeSummonerSearchPrefixNonUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Summoners_SearchPrefix",
                table: "Summoners");

            migrationBuilder.CreateIndex(
                name: "IX_Summoners_SearchPrefix",
                table: "Summoners",
                columns: new[] { "PlatformRegion", "GameNameNormalized", "TagLineNormalized" },
                filter: "\"GameNameNormalized\" IS NOT NULL AND \"TagLineNormalized\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Summoners_SearchPrefix",
                table: "Summoners");

            migrationBuilder.CreateIndex(
                name: "IX_Summoners_SearchPrefix",
                table: "Summoners",
                columns: new[] { "PlatformRegion", "GameNameNormalized", "TagLineNormalized" },
                unique: true,
                filter: "\"GameNameNormalized\" IS NOT NULL AND \"TagLineNormalized\" IS NOT NULL");
        }
    }
}
