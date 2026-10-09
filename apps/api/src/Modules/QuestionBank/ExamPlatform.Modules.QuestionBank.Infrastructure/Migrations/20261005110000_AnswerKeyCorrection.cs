using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AnswerKeyCorrection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnswerKeyCorrectedAtUtc",
                schema: "questionBank",
                table: "Questions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnswerKeyCorrectedAtUtc",
                schema: "questionBank",
                table: "Questions");
        }
    }
}
