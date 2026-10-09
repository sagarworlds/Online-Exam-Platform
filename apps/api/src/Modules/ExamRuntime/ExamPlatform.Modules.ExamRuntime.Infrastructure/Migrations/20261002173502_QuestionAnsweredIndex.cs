using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuestionAnsweredIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AttemptAnswers_QuestionId",
                schema: "examRuntime",
                table: "AttemptAnswers",
                column: "QuestionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AttemptAnswers_QuestionId",
                schema: "examRuntime",
                table: "AttemptAnswers");
        }
    }
}
