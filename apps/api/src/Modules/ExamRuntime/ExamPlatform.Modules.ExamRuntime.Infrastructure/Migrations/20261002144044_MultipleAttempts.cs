using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MultipleAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Attempts_ExamId_CandidateId",
                schema: "examRuntime",
                table: "Attempts");

            // Until now a candidate had at most one attempt per exam, so every existing attempt is attempt 1. (EF writes 0 for
            // a new required column; 0 is not a valid attempt number.)
            migrationBuilder.AddColumn<int>(
                name: "Number",
                schema: "examRuntime",
                table: "Attempts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "ExtraAttemptGrants",
                schema: "examRuntime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtraAttemptGrants", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_ExamId_CandidateId_Number",
                schema: "examRuntime",
                table: "Attempts",
                columns: new[] { "ExamId", "CandidateId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExtraAttemptGrants_ExamId_CandidateId_Number",
                schema: "examRuntime",
                table: "ExtraAttemptGrants",
                columns: new[] { "ExamId", "CandidateId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Undoing this restores "one attempt per candidate per exam" as a unique index, so it fails, rather than lose
        /// attempts, if any candidate has since been given and used an extra attempt.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtraAttemptGrants",
                schema: "examRuntime");

            migrationBuilder.DropIndex(
                name: "IX_Attempts_ExamId_CandidateId_Number",
                schema: "examRuntime",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "Number",
                schema: "examRuntime",
                table: "Attempts");

            migrationBuilder.CreateIndex(
                name: "IX_Attempts_ExamId_CandidateId",
                schema: "examRuntime",
                table: "Attempts",
                columns: new[] { "ExamId", "CandidateId" },
                unique: true);
        }
    }
}
