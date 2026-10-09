using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTextAnswerQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "AcceptedAnswers",
                schema: "questionBank",
                table: "Questions",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<bool>(
                name: "IsTextAnswer",
                schema: "questionBank",
                table: "Questions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string[]>(
                name: "AcceptedAnswers",
                schema: "questionBank",
                table: "QuestionVersions",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<bool>(
                name: "IsTextAnswer",
                schema: "questionBank",
                table: "QuestionVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptedAnswers",
                schema: "questionBank",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "IsTextAnswer",
                schema: "questionBank",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "AcceptedAnswers",
                schema: "questionBank",
                table: "QuestionVersions");

            migrationBuilder.DropColumn(
                name: "IsTextAnswer",
                schema: "questionBank",
                table: "QuestionVersions");
        }
    }
}
