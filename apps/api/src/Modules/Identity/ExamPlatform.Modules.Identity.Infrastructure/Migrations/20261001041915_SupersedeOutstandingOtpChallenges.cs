using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SupersedeOutstandingOtpChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAtUtc",
                schema: "identity",
                table: "OtpChallenges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OtpChallenges_Destination_Purpose",
                schema: "identity",
                table: "OtpChallenges",
                columns: new[] { "Destination", "Purpose" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OtpChallenges_Destination_Purpose",
                schema: "identity",
                table: "OtpChallenges");

            migrationBuilder.DropColumn(
                name: "SupersededAtUtc",
                schema: "identity",
                table: "OtpChallenges");
        }
    }
}
