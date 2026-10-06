using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuestionReviewWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every question that exists already was usable in exams before there was a workflow, so it starts approved; new ones are drafts.
            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "questionBank",
                table: "Questions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.CreateTable(
                name: "QuestionReviewEntries",
                schema: "questionBank",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    QuestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ByLabel = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    StatusAfter = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuestionReviewEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuestionReviewEntries_Questions_QuestionId",
                        column: x => x.QuestionId,
                        principalSchema: "questionBank",
                        principalTable: "Questions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Questions_Status",
                schema: "questionBank",
                table: "Questions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_QuestionReviewEntries_QuestionId_CreatedAtUtc",
                schema: "questionBank",
                table: "QuestionReviewEntries",
                columns: new[] { "QuestionId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuestionReviewEntries",
                schema: "questionBank");

            migrationBuilder.DropIndex(
                name: "IX_Questions_Status",
                schema: "questionBank",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "questionBank",
                table: "Questions");
        }
    }
}
