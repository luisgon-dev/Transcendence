using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionVersionsAndEvidenceTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "BucketConfidence",
                table: "AdjustedActionEstimates",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceBucket",
                table: "AdjustedActionEstimates",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "EvidenceTier",
                table: "AdjustedActionEstimates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ChampionVersions",
                columns: table => new
                {
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    PatchVersion = table.Column<string>(type: "text", nullable: false),
                    Alias = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BalanceHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Roles = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChampionVersions", x => new { x.ChampionId, x.PatchVersion });
                    table.ForeignKey(
                        name: "FK_ChampionVersions_Patches_PatchVersion",
                        column: x => x.PatchVersion,
                        principalTable: "Patches",
                        principalColumn: "Version",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChampionVersions_PatchVersion",
                table: "ChampionVersions",
                column: "PatchVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChampionVersions");

            migrationBuilder.DropColumn(
                name: "BucketConfidence",
                table: "AdjustedActionEstimates");

            migrationBuilder.DropColumn(
                name: "EvidenceBucket",
                table: "AdjustedActionEstimates");

            migrationBuilder.DropColumn(
                name: "EvidenceTier",
                table: "AdjustedActionEstimates");
        }
    }
}
