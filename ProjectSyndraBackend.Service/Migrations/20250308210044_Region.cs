using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProjectSyndraBackend.Service.Migrations
{
    /// <inheritdoc />
    public partial class Region : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PlatformRegion",
                table: "Summoners",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "Summoners",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QueueType",
                table: "Matches",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlatformRegion",
                table: "Summoners");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "Summoners");

            migrationBuilder.DropColumn(
                name: "QueueType",
                table: "Matches");
        }
    }
}
