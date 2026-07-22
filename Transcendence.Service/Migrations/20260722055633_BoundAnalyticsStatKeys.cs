using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class BoundAnalyticsStatKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RankScope",
                table: "ScopeMatchCountStats",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "QueueFamily",
                table: "ScopeMatchCountStats",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "RANKED_SOLO_DUO",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "RANKED_SOLO_DUO");

            migrationBuilder.AlterColumn<string>(
                name: "PlatformRegion",
                table: "ScopeMatchCountStats",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "ScopeMatchCountStats",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "ChampionScopeGradeStats",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "RankScope",
                table: "ChampionScopeGradeStats",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "QueueFamily",
                table: "ChampionScopeGradeStats",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "RANKED_SOLO_DUO",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "RANKED_SOLO_DUO");

            migrationBuilder.AlterColumn<string>(
                name: "PrimaryRole",
                table: "ChampionScopeGradeStats",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "PlatformRegion",
                table: "ChampionScopeGradeStats",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "ChampionScopeGradeStats",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "ChampionRoleTierStats",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "RankTier",
                table: "ChampionRoleTierStats",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "QueueFamily",
                table: "ChampionRoleTierStats",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "RANKED_SOLO_DUO",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "RANKED_SOLO_DUO");

            migrationBuilder.AlterColumn<string>(
                name: "PlatformRegion",
                table: "ChampionRoleTierStats",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "ChampionRoleTierStats",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "ChampionMatchupStats",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "RankTier",
                table: "ChampionMatchupStats",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "ChampionMatchupStats",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "ChampionBuildSnapshots",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "RankScope",
                table: "ChampionBuildSnapshots",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "ChampionBuildSnapshots",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "RankScope",
                table: "ChampionBanScopeStats",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "QueueFamily",
                table: "ChampionBanScopeStats",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "RANKED_SOLO_DUO",
                oldClrType: typeof(string),
                oldType: "text",
                oldDefaultValue: "RANKED_SOLO_DUO");

            migrationBuilder.AlterColumn<string>(
                name: "PlatformRegion",
                table: "ChampionBanScopeStats",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "ChampionBanScopeStats",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ScopeKey",
                table: "AnalyticsResponseSnapshots",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "AnalyticsResponseSnapshots",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Feature",
                table: "AnalyticsResponseSnapshots",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RankScope",
                table: "ScopeMatchCountStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "QueueFamily",
                table: "ScopeMatchCountStats",
                type: "text",
                nullable: false,
                defaultValue: "RANKED_SOLO_DUO",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "RANKED_SOLO_DUO");

            migrationBuilder.AlterColumn<string>(
                name: "PlatformRegion",
                table: "ScopeMatchCountStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "ScopeMatchCountStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "ChampionScopeGradeStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "RankScope",
                table: "ChampionScopeGradeStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "QueueFamily",
                table: "ChampionScopeGradeStats",
                type: "text",
                nullable: false,
                defaultValue: "RANKED_SOLO_DUO",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "RANKED_SOLO_DUO");

            migrationBuilder.AlterColumn<string>(
                name: "PrimaryRole",
                table: "ChampionScopeGradeStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "PlatformRegion",
                table: "ChampionScopeGradeStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "ChampionScopeGradeStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "ChampionRoleTierStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "RankTier",
                table: "ChampionRoleTierStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "QueueFamily",
                table: "ChampionRoleTierStats",
                type: "text",
                nullable: false,
                defaultValue: "RANKED_SOLO_DUO",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "RANKED_SOLO_DUO");

            migrationBuilder.AlterColumn<string>(
                name: "PlatformRegion",
                table: "ChampionRoleTierStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "ChampionRoleTierStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "ChampionMatchupStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "RankTier",
                table: "ChampionMatchupStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "ChampionMatchupStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                table: "ChampionBuildSnapshots",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "RankScope",
                table: "ChampionBuildSnapshots",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "ChampionBuildSnapshots",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "RankScope",
                table: "ChampionBanScopeStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "QueueFamily",
                table: "ChampionBanScopeStats",
                type: "text",
                nullable: false,
                defaultValue: "RANKED_SOLO_DUO",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldDefaultValue: "RANKED_SOLO_DUO");

            migrationBuilder.AlterColumn<string>(
                name: "PlatformRegion",
                table: "ChampionBanScopeStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(16)",
                oldMaxLength: 16);

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "ChampionBanScopeStats",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "ScopeKey",
                table: "AnalyticsResponseSnapshots",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Patch",
                table: "AnalyticsResponseSnapshots",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Feature",
                table: "AnalyticsResponseSnapshots",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);
        }
    }
}
