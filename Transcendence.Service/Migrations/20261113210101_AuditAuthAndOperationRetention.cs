using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transcendence.Service.Migrations
{
    /// <inheritdoc />
    public partial class AuditAuthAndOperationRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "UserRefreshTokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "ProtectedReplacementToken",
                table: "UserRefreshTokens",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_ExpiresAtUtc",
                table: "UserRefreshTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_RevokedAtUtc",
                table: "UserRefreshTokens",
                column: "RevokedAtUtc",
                filter: "\"ProtectedReplacementToken\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserRefreshTokens_UserAccountId_FamilyId",
                table: "UserRefreshTokens",
                columns: new[] { "UserAccountId", "FamilyId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPasswordResetTokens_ExpiresAtUtc",
                table: "UserPasswordResetTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundOperations_PendingDispatch",
                table: "BackgroundOperations",
                column: "QueuedAtUtc",
                filter: "\"DispatchedAtUtc\" IS NULL AND \"Status\" NOT IN ('succeeded', 'partial', 'failed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserRefreshTokens_ExpiresAtUtc",
                table: "UserRefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_UserRefreshTokens_RevokedAtUtc",
                table: "UserRefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_UserRefreshTokens_UserAccountId_FamilyId",
                table: "UserRefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_UserPasswordResetTokens_ExpiresAtUtc",
                table: "UserPasswordResetTokens");

            migrationBuilder.DropIndex(
                name: "IX_BackgroundOperations_PendingDispatch",
                table: "BackgroundOperations");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "UserRefreshTokens");

            migrationBuilder.DropColumn(
                name: "ProtectedReplacementToken",
                table: "UserRefreshTokens");
        }
    }
}
