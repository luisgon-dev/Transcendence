using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnedBackgroundOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OperationId",
                table: "SummonerFullHistoryBackfills",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "BackgroundOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ResourceKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PlatformRegion = table.Column<string>(type: "text", nullable: false),
                    GameName = table.Column<string>(type: "text", nullable: false),
                    TagLine = table.Column<string>(type: "text", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    QueuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RetryAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PhasesJson = table.Column<string>(type: "text", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    DispatchJson = table.Column<string>(type: "text", nullable: false),
                    DispatchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HangfireJobId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackgroundOperations_BackgroundOperations_ParentId",
                        column: x => x.ParentId,
                        principalTable: "BackgroundOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BackgroundOperationRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerKind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundOperationRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackgroundOperationRequests_BackgroundOperations_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "BackgroundOperations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundOperationRequests_ExecutionId_OwnerKind_OwnerId",
                table: "BackgroundOperationRequests",
                columns: new[] { "ExecutionId", "OwnerKind", "OwnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundOperations_CompletedAtUtc",
                table: "BackgroundOperations",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundOperations_ParentId_Kind",
                table: "BackgroundOperations",
                columns: new[] { "ParentId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundOperations_ResourceKey_LeaseToken",
                table: "BackgroundOperations",
                columns: new[] { "ResourceKey", "LeaseToken" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackgroundOperationRequests");

            migrationBuilder.DropTable(
                name: "BackgroundOperations");

            migrationBuilder.DropColumn(
                name: "OperationId",
                table: "SummonerFullHistoryBackfills");
        }
    }
}
