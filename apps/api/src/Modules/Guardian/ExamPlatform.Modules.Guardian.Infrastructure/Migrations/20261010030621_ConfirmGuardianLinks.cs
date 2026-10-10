using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.Guardian.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmGuardianLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The plain codes are dropped: no code was ever e-mailed, so no guardian holds one. Existing links get an empty hash
            // and an expiry at the earliest date, so they can never be confirmed and must be linked again.
            migrationBuilder.DropColumn(
                name: "VerificationToken",
                schema: "guardian",
                table: "GuardianLinks");

            migrationBuilder.AddColumn<DateTime>(
                name: "VerificationExpiresAt",
                schema: "guardian",
                table: "GuardianLinks",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<string>(
                name: "VerificationTokenHash",
                schema: "guardian",
                table: "GuardianLinks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VerificationExpiresAt",
                schema: "guardian",
                table: "GuardianLinks");

            migrationBuilder.DropColumn(
                name: "VerificationTokenHash",
                schema: "guardian",
                table: "GuardianLinks");

            migrationBuilder.AddColumn<string>(
                name: "VerificationToken",
                schema: "guardian",
                table: "GuardianLinks",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");
        }
    }
}
