using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class More : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchDetails");

            migrationBuilder.DropTable(
                name: "MatchSummoners");

            migrationBuilder.DropTable(
                name: "Runes");

            migrationBuilder.DropColumn(
                name: "Item0",
                table: "MatchParticipants");

            migrationBuilder.DropColumn(
                name: "Item1",
                table: "MatchParticipants");

            migrationBuilder.DropColumn(
                name: "Item2",
                table: "MatchParticipants");

            migrationBuilder.DropColumn(
                name: "Item3",
                table: "MatchParticipants");

            migrationBuilder.DropColumn(
                name: "Item4",
                table: "MatchParticipants");

            migrationBuilder.DropColumn(
                name: "Item5",
                table: "MatchParticipants");

            migrationBuilder.DropColumn(
                name: "Item6",
                table: "MatchParticipants");

            migrationBuilder.DropColumn(
                name: "TrinketItem",
                table: "MatchParticipants");

            migrationBuilder.CreateTable(
                name: "MatchSummoner",
                columns: table => new
                {
                    MatchesId = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonersId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchSummoner", x => new { x.MatchesId, x.SummonersId });
                    table.ForeignKey(
                        name: "FK_MatchSummoner_Matches_MatchesId",
                        column: x => x.MatchesId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchSummoner_Summoners_SummonersId",
                        column: x => x.SummonersId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Patches",
                columns: table => new
                {
                    Version = table.Column<string>(type: "text", nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Patches", x => x.Version);
                });

            migrationBuilder.CreateTable(
                name: "ItemVersions",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    PatchVersion = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemVersions", x => new { x.ItemId, x.PatchVersion });
                    table.ForeignKey(
                        name: "FK_ItemVersions_Patches_PatchVersion",
                        column: x => x.PatchVersion,
                        principalTable: "Patches",
                        principalColumn: "Version",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RuneVersions",
                columns: table => new
                {
                    RuneId = table.Column<int>(type: "integer", nullable: false),
                    PatchVersion = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    RunePathId = table.Column<int>(type: "integer", nullable: false),
                    RunePathName = table.Column<string>(type: "text", nullable: false),
                    Slot = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuneVersions", x => new { x.RuneId, x.PatchVersion });
                    table.ForeignKey(
                        name: "FK_RuneVersions_Patches_PatchVersion",
                        column: x => x.PatchVersion,
                        principalTable: "Patches",
                        principalColumn: "Version",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchParticipantItems",
                columns: table => new
                {
                    MatchParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<int>(type: "integer", nullable: false),
                    PatchVersion = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchParticipantItems", x => new { x.MatchParticipantId, x.ItemId });
                    table.ForeignKey(
                        name: "FK_MatchParticipantItems_ItemVersions_ItemId_PatchVersion",
                        columns: x => new { x.ItemId, x.PatchVersion },
                        principalTable: "ItemVersions",
                        principalColumns: new[] { "ItemId", "PatchVersion" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchParticipantItems_MatchParticipants_MatchParticipantId",
                        column: x => x.MatchParticipantId,
                        principalTable: "MatchParticipants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MatchParticipantRunes",
                columns: table => new
                {
                    MatchParticipantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuneId = table.Column<int>(type: "integer", nullable: false),
                    PatchVersion = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchParticipantRunes", x => new { x.MatchParticipantId, x.RuneId });
                    table.ForeignKey(
                        name: "FK_MatchParticipantRunes_MatchParticipants_MatchParticipantId",
                        column: x => x.MatchParticipantId,
                        principalTable: "MatchParticipants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchParticipantRunes_RuneVersions_RuneId_PatchVersion",
                        columns: x => new { x.RuneId, x.PatchVersion },
                        principalTable: "RuneVersions",
                        principalColumns: new[] { "RuneId", "PatchVersion" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemVersions_PatchVersion",
                table: "ItemVersions",
                column: "PatchVersion");

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipantItems_ItemId_PatchVersion",
                table: "MatchParticipantItems",
                columns: new[] { "ItemId", "PatchVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchParticipantRunes_RuneId_PatchVersion",
                table: "MatchParticipantRunes",
                columns: new[] { "RuneId", "PatchVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_MatchSummoner_SummonersId",
                table: "MatchSummoner",
                column: "SummonersId");

            migrationBuilder.CreateIndex(
                name: "IX_RuneVersions_PatchVersion",
                table: "RuneVersions",
                column: "PatchVersion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchParticipantItems");

            migrationBuilder.DropTable(
                name: "MatchParticipantRunes");

            migrationBuilder.DropTable(
                name: "MatchSummoner");

            migrationBuilder.DropTable(
                name: "ItemVersions");

            migrationBuilder.DropTable(
                name: "RuneVersions");

            migrationBuilder.DropTable(
                name: "Patches");

            migrationBuilder.AddColumn<int>(
                name: "Item0",
                table: "MatchParticipants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Item1",
                table: "MatchParticipants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Item2",
                table: "MatchParticipants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Item3",
                table: "MatchParticipants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Item4",
                table: "MatchParticipants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Item5",
                table: "MatchParticipants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Item6",
                table: "MatchParticipants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TrinketItem",
                table: "MatchParticipants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "MatchSummoners",
                columns: table => new
                {
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchSummoners", x => new { x.MatchId, x.SummonerId });
                    table.ForeignKey(
                        name: "FK_MatchSummoners_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchSummoners_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Runes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Perk0 = table.Column<int>(type: "integer", nullable: false),
                    Perk1 = table.Column<int>(type: "integer", nullable: false),
                    Perk2 = table.Column<int>(type: "integer", nullable: false),
                    Perk3 = table.Column<int>(type: "integer", nullable: false),
                    Perk4 = table.Column<int>(type: "integer", nullable: false),
                    Perk5 = table.Column<int>(type: "integer", nullable: false),
                    PrimaryStyle = table.Column<int>(type: "integer", nullable: false),
                    RuneVars0 = table.Column<int[]>(type: "integer[]", nullable: false),
                    RuneVars1 = table.Column<int[]>(type: "integer[]", nullable: false),
                    RuneVars2 = table.Column<int[]>(type: "integer[]", nullable: false),
                    RuneVars3 = table.Column<int[]>(type: "integer[]", nullable: false),
                    RuneVars4 = table.Column<int[]>(type: "integer[]", nullable: false),
                    RuneVars5 = table.Column<int[]>(type: "integer[]", nullable: false),
                    StatDefense = table.Column<int>(type: "integer", nullable: false),
                    StatFlex = table.Column<int>(type: "integer", nullable: false),
                    StatOffense = table.Column<int>(type: "integer", nullable: false),
                    SubStyle = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MatchDetails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunesId = table.Column<Guid>(type: "uuid", nullable: false),
                    SummonerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Assists = table.Column<int>(type: "integer", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    ChampionName = table.Column<string>(type: "text", nullable: false),
                    Deaths = table.Column<int>(type: "integer", nullable: false),
                    Items = table.Column<List<int>>(type: "integer[]", nullable: false),
                    Kills = table.Column<int>(type: "integer", nullable: false),
                    Lane = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    SummonerSpell1 = table.Column<int>(type: "integer", nullable: false),
                    SummonerSpell2 = table.Column<int>(type: "integer", nullable: false),
                    Win = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchDetails_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchDetails_Runes_RunesId",
                        column: x => x.RunesId,
                        principalTable: "Runes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MatchDetails_Summoners_SummonerId",
                        column: x => x.SummonerId,
                        principalTable: "Summoners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchDetails_MatchId",
                table: "MatchDetails",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchDetails_RunesId",
                table: "MatchDetails",
                column: "RunesId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchDetails_SummonerId",
                table: "MatchDetails",
                column: "SummonerId");

            migrationBuilder.CreateIndex(
                name: "IX_MatchSummoners_SummonerId",
                table: "MatchSummoners",
                column: "SummonerId");

            migrationBuilder.CreateIndex(
                name: "IX_Runes_Combination",
                table: "Runes",
                columns: new[] { "PrimaryStyle", "SubStyle", "Perk0", "Perk1", "Perk2", "Perk3", "Perk4", "Perk5", "StatDefense", "StatFlex", "StatOffense" });
        }
    }
}
