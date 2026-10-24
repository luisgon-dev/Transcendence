using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddBuildLabItemPurchaseIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_BuildLabOptionStats_ItemPurchases",
                table: "BuildLabOptionStats",
                columns: new[] { "Patch", "Region", "Family", "ActionKey", "Stage", "GoldBucket", "Games", "Wins", "TimingSecondsSum" },
                filter: "\"OpponentChampionId\" = 0 AND \"Family\" IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BuildLabOptionStats_ItemPurchases",
                table: "BuildLabOptionStats");
        }
    }
}
