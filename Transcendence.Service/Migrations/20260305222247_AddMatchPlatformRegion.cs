using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchPlatformRegion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlatformRegion",
                table: "Matches",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Matches_PlatformRegion_Status_Patch",
                table: "Matches",
                columns: new[] { "PlatformRegion", "Status", "Patch" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Matches_PlatformRegion_Status_Patch",
                table: "Matches");

            migrationBuilder.DropColumn(
                name: "PlatformRegion",
                table: "Matches");
        }
    }
}
