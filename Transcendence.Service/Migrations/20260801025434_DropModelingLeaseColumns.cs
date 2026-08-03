using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class DropModelingLeaseColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BuildLabGenerations_Status_LeaseExpiresAtUtc",
                table: "BuildLabGenerations");

            migrationBuilder.DropColumn(
                name: "HeartbeatAtUtc",
                table: "BuildLabGenerations");

            migrationBuilder.DropColumn(
                name: "LeaseAcquiredAtUtc",
                table: "BuildLabGenerations");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAtUtc",
                table: "BuildLabGenerations");

            migrationBuilder.CreateIndex(
                name: "IX_BuildLabGenerations_Status",
                table: "BuildLabGenerations",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BuildLabGenerations_Status",
                table: "BuildLabGenerations");

            migrationBuilder.AddColumn<DateTime>(
                name: "HeartbeatAtUtc",
                table: "BuildLabGenerations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseAcquiredAtUtc",
                table: "BuildLabGenerations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresAtUtc",
                table: "BuildLabGenerations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BuildLabGenerations_Status_LeaseExpiresAtUtc",
                table: "BuildLabGenerations",
                columns: new[] { "Status", "LeaseExpiresAtUtc" });
        }
    }
}
