using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class MatchParticipantItemSlotKeyEnforcement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_MatchParticipantItems",
                table: "MatchParticipantItems");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MatchParticipantItems",
                table: "MatchParticipantItems",
                columns: new[] { "MatchParticipantId", "SlotIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipantItems_MatchParticipantId_ItemId",
                table: "MatchParticipantItems",
                columns: new[] { "MatchParticipantId", "ItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_MatchParticipantItems",
                table: "MatchParticipantItems");

            migrationBuilder.DropIndex(
                name: "IX_MatchParticipantItems_MatchParticipantId_ItemId",
                table: "MatchParticipantItems");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MatchParticipantItems",
                table: "MatchParticipantItems",
                columns: new[] { "MatchParticipantId", "ItemId", "SlotIndex" });
        }
    }
}
