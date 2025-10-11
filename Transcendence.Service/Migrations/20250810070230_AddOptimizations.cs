using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddOptimizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Ranks_SummonerId",
                table: "Ranks");

            migrationBuilder.CreateIndex(
                name: "IX_Ranks_SummonerId_QueueType",
                table: "Ranks",
                columns: new[] { "SummonerId", "QueueType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matches_MatchId",
                table: "Matches",
                column: "MatchId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Ranks_SummonerId_QueueType",
                table: "Ranks");

            migrationBuilder.DropIndex(
                name: "IX_Matches_MatchId",
                table: "Matches");

            migrationBuilder.CreateIndex(
                name: "IX_Ranks_SummonerId",
                table: "Ranks",
                column: "SummonerId");
        }
    }
}
