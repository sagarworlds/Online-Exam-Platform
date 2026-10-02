using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.Identity.Infrastructure.Migrations
{
    /// <summary>
    /// Adds PasswordResetTokens.RevokedAtUtc and an index on UserId, and maps the table's
    /// Postgres <c>xmin</c> system column as its optimistic concurrency token. The scaffolded
    /// AddColumn/DropColumn for <c>xmin</c> was removed by hand: every Postgres row already has
    /// it, so the snapshot change is all that mapping needs (see <see cref="OtpChallengeConcurrencyToken"/>).
    /// </summary>
    public partial class PasswordResetTokenRevocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAtUtc",
                schema: "identity",
                table: "PasswordResetTokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId",
                schema: "identity",
                table: "PasswordResetTokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PasswordResetTokens_UserId",
                schema: "identity",
                table: "PasswordResetTokens");

            migrationBuilder.DropColumn(
                name: "RevokedAtUtc",
                schema: "identity",
                table: "PasswordResetTokens");
        }
    }
}
