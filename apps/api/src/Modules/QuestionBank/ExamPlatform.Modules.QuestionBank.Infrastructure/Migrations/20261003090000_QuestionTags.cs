using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuestionTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing questions have no labels yet: no difficulty and no topics.
            migrationBuilder.AddColumn<string>(
                name: "Difficulty",
                schema: "questionBank",
                table: "Questions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "Topics",
                schema: "questionBank",
                table: "Questions",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Difficulty",
                schema: "questionBank",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Topics",
                schema: "questionBank",
                table: "Questions");
        }
    }
}
