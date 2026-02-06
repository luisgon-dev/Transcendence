using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddPatchDetectionMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HistoricalRanks_Summoners_SummonerId",
                table: "HistoricalRanks");

            migrationBuilder.AddColumn<DateTime>(
                name: "DetectedAt",
                table: "Patches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Patches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "SummonerId",
                table: "HistoricalRanks",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "FK_HistoricalRanks_Summoners_SummonerId",
                table: "HistoricalRanks",
                column: "SummonerId",
                principalTable: "Summoners",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HistoricalRanks_Summoners_SummonerId",
                table: "HistoricalRanks");

            migrationBuilder.DropColumn(
                name: "DetectedAt",
                table: "Patches");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Patches");

            migrationBuilder.AlterColumn<Guid>(
                name: "SummonerId",
                table: "HistoricalRanks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_HistoricalRanks_Summoners_SummonerId",
                table: "HistoricalRanks",
                column: "SummonerId",
                principalTable: "Summoners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
