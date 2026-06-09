using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderedBuildPurchasesAndSkillOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                table: "MatchTimelineFetchStates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MatchParticipantItemPurchases",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    PurchaseIndex = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    TimestampMs = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchParticipantItemPurchases", x => new { x.MatchId, x.ParticipantId, x.PurchaseIndex });
                    table.ForeignKey(
                        name: "FK_MatchParticipantItemPurchases_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchParticipantSkillOrders",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipantId = table.Column<int>(type: "integer", nullable: false),
                    Sequence = table.Column<string>(type: "text", nullable: false),
                    FirstThree = table.Column<string>(type: "text", nullable: false),
                    MaxOrder = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchParticipantSkillOrders", x => new { x.MatchId, x.ParticipantId });
                    table.ForeignKey(
                        name: "FK_MatchParticipantSkillOrders_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchParticipantItemPurchases");

            migrationBuilder.DropTable(
                name: "MatchParticipantSkillOrders");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "MatchTimelineFetchStates");
        }
    }
}
