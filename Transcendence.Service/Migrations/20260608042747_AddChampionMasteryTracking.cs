using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionMasteryTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChampionMasteries",
                columns: table => new
                {
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    ChampionLevel = table.Column<int>(type: "integer", nullable: false),
                    ChampionPoints = table.Column<long>(type: "bigint", nullable: false),
                    LastPlayTime = table.Column<long>(type: "bigint", nullable: false),
                    ChestGranted = table.Column<bool>(type: "boolean", nullable: false),
                    TokensEarned = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionMasteries", x => new { x.SummonerId, x.ChampionId });
                    table.ForeignKey(
                        name: "FK_ChampionMasteries_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChampionMasteries");
        }
    }
}
