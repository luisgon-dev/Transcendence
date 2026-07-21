using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddRiotAccountLinking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "UserAccounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserRiotAccounts",
                columns: table => new
                {
                    UserAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Puuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GameName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TagLine = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlatformRegion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRiotAccounts", x => x.UserAccountId);
                    table.ForeignKey(
                        name: "FK_UserRiotAccounts_UserAccounts_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserRiotAccounts_Puuid",
                table: "UserRiotAccounts",
                column: "Puuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserRiotAccounts");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "UserAccounts");
        }
    }
}
