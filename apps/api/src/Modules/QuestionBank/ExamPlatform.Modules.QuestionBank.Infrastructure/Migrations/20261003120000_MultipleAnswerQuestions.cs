using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MultipleAnswerQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every question stored so far takes exactly one answer, so none of them allows several.
            migrationBuilder.AddColumn<bool>(
                name: "AllowsMultiple",
                schema: "questionBank",
                table: "Questions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowsMultiple",
                schema: "questionBank",
                table: "Questions");
        }
    }
}
