using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Accommodations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccommodationExtraSeconds",
                schema: "examRuntime",
                table: "Attempts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string[]>(
                name: "AccommodationFormats",
                schema: "examRuntime",
                table: "Attempts",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<bool>(
                name: "AccommodationReaderScribe",
                schema: "examRuntime",
                table: "Attempts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Accommodations",
                schema: "examRuntime",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExtraTimeSeconds = table.Column<int>(type: "integer", nullable: false),
                    ReaderScribe = table.Column<bool>(type: "boolean", nullable: false),
                    AlternateFormats = table.Column<string[]>(type: "text[]", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accommodations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accommodations_ExamId_CandidateId",
                schema: "examRuntime",
                table: "Accommodations",
                columns: new[] { "ExamId", "CandidateId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accommodations",
                schema: "examRuntime");

            migrationBuilder.DropColumn(
                name: "AccommodationReaderScribe",
                schema: "examRuntime",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "AccommodationFormats",
                schema: "examRuntime",
                table: "Attempts");

            migrationBuilder.DropColumn(
                name: "AccommodationExtraSeconds",
                schema: "examRuntime",
                table: "Attempts");
        }
    }
}
