using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class DecoupleMatchFactsFromStaticData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MatchParticipantItems_ItemVersions_ItemId_PatchVersion",
                table: "MatchParticipantItems");

            migrationBuilder.DropForeignKey(
                name: "FK_MatchParticipantRunes_RuneVersions_RuneId_PatchVersion",
                table: "MatchParticipantRunes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_MatchParticipantItems_ItemVersions_ItemId_PatchVersion",
                table: "MatchParticipantItems",
                columns: new[] { "ItemId", "PatchVersion" },
                principalTable: "ItemVersions",
                principalColumns: new[] { "ItemId", "PatchVersion" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_MatchParticipantRunes_RuneVersions_RuneId_PatchVersion",
                table: "MatchParticipantRunes",
                columns: new[] { "RuneId", "PatchVersion" },
                principalTable: "RuneVersions",
                principalColumns: new[] { "RuneId", "PatchVersion" },
                onDelete: ReferentialAction.Cascade);
        }
    }
}
