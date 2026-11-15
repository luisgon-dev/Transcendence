using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedMatchArchiving : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchArchiveChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FrozenMatches = table.Column<int>(type: "integer", nullable: false),
                    PrunedMatches = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LegacySource = table.Column<bool>(type: "boolean", nullable: false),
                    ManifestJson = table.Column<string>(type: "text", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchArchiveChunks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatchArchiveEntries",
                columns: table => new
                {
                    ChunkId = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchArchiveEntries", x => new { x.ChunkId, x.MatchId });
                    table.ForeignKey(
                        name: "FK_MatchArchiveEntries_MatchArchiveChunks_ChunkId",
                        column: x => x.ChunkId,
                        principalTable: "MatchArchiveChunks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchArchiveChunks_CompletedAtUtc_CreatedAtUtc",
                table: "MatchArchiveChunks",
                columns: new[] { "CompletedAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchArchiveEntries_MatchId",
                table: "MatchArchiveEntries",
                column: "MatchId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchArchiveEntries");

            migrationBuilder.DropTable(
                name: "MatchArchiveChunks");
        }
    }
}
