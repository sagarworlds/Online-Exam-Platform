using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttemptAdminActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InvalidatedAtUtc",
                schema: "examRuntime",
                table: "Attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvalidatedByUserId",
                schema: "examRuntime",
                table: "Attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvalidationReason",
                schema: "examRuntime",
                table: "Attempts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PausedAtUtc",
                schema: "examRuntime",
                table: "Attempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TerminatedByUserId",
                schema: "examRuntime",
                table: "Attempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TerminationReason",
                schema: "examRuntime",
                table: "Attempts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttemptWarnings",
                schema: "examRuntime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttemptWarnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AttemptWarnings_Attempts_AttemptId",
                        column: x => x.AttemptId,
                        principalSchema: "examRuntime",
                        principalTable: "Attempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttemptWarnings_AttemptId_IssuedAtUtc",
                schema: "examRuntime",
                table: "AttemptWarnings",
                columns: new[] { "AttemptId", "IssuedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttemptWarnings",
                schema: "examRuntime");

            migrationBuilder.DropColumn(name: "InvalidatedAtUtc", schema: "examRuntime", table: "Attempts");
            migrationBuilder.DropColumn(name: "InvalidatedByUserId", schema: "examRuntime", table: "Attempts");
            migrationBuilder.DropColumn(name: "InvalidationReason", schema: "examRuntime", table: "Attempts");
            migrationBuilder.DropColumn(name: "PausedAtUtc", schema: "examRuntime", table: "Attempts");
            migrationBuilder.DropColumn(name: "TerminatedByUserId", schema: "examRuntime", table: "Attempts");
            migrationBuilder.DropColumn(name: "TerminationReason", schema: "examRuntime", table: "Attempts");
        }
    }
}
