using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveGameSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LiveGameSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Puuid = table.Column<string>(type: "text", nullable: false),
                    PlatformRegion = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    GameId = table.Column<string>(type: "text", nullable: true),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextPollAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiveGameSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LiveGameSnapshots_NextPollAtUtc",
                table: "LiveGameSnapshots",
                column: "NextPollAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LiveGameSnapshots_Puuid_PlatformRegion_ObservedAtUtc",
                table: "LiveGameSnapshots",
                columns: new[] { "Puuid", "PlatformRegion", "ObservedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LiveGameSnapshots");
        }
    }
}
