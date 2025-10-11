using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddRuneOptimization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Runes_Combination",
                table: "Runes",
                columns: new[] { "PrimaryStyle", "SubStyle", "Perk0", "Perk1", "Perk2", "Perk3", "Perk4", "Perk5", "StatDefense", "StatFlex", "StatOffense" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Runes_Combination",
                table: "Runes");
        }
    }
}
