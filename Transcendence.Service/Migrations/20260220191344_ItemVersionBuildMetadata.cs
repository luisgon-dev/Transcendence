using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class ItemVersionBuildMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<int>>(
                name: "BuildsFrom",
                table: "ItemVersions",
                type: "integer[]",
                nullable: false,
                defaultValue: new List<int>());

            migrationBuilder.AddColumn<List<int>>(
                name: "BuildsInto",
                table: "ItemVersions",
                type: "integer[]",
                nullable: false,
                defaultValue: new List<int>());

            migrationBuilder.AddColumn<bool>(
                name: "InStore",
                table: "ItemVersions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "PriceTotal",
                table: "ItemVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuildsFrom",
                table: "ItemVersions");

            migrationBuilder.DropColumn(
                name: "BuildsInto",
                table: "ItemVersions");

            migrationBuilder.DropColumn(
                name: "InStore",
                table: "ItemVersions");

            migrationBuilder.DropColumn(
                name: "PriceTotal",
                table: "ItemVersions");
        }
    }
}
