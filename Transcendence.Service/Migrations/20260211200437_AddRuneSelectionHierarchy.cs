using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddRuneSelectionHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_MatchParticipantRunes",
                table: "MatchParticipantRunes");

            migrationBuilder.AddColumn<int>(
                name: "SelectionTree",
                table: "MatchParticipantRunes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SelectionIndex",
                table: "MatchParticipantRunes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StyleId",
                table: "MatchParticipantRunes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_MatchParticipantRunes",
                table: "MatchParticipantRunes",
                columns: new[] { "MatchParticipantId", "SelectionTree", "SelectionIndex", "RuneId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_MatchParticipantRunes",
                table: "MatchParticipantRunes");

            migrationBuilder.DropColumn(
                name: "SelectionTree",
                table: "MatchParticipantRunes");

            migrationBuilder.DropColumn(
                name: "SelectionIndex",
                table: "MatchParticipantRunes");

            migrationBuilder.DropColumn(
                name: "StyleId",
                table: "MatchParticipantRunes");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MatchParticipantRunes",
                table: "MatchParticipantRunes",
                columns: new[] { "MatchParticipantId", "RuneId" });
        }
    }
}
