using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalyticsResponseSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyticsResponseSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Feature = table.Column<string>(type: "text", nullable: false),
                    ScopeKey = table.Column<string>(type: "text", nullable: false),
                    Patch = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    ComputedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsResponseSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsResponseSnapshots_Feature_ScopeKey_Patch",
                table: "AnalyticsResponseSnapshots",
                columns: new[] { "Feature", "ScopeKey", "Patch" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsResponseSnapshots");
        }
    }
}
