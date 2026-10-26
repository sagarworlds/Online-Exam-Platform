using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTextMatchRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MatchRule",
                schema: "questionBank",
                table: "Questions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Exact");

            migrationBuilder.AddColumn<string>(
                name: "MatchRule",
                schema: "questionBank",
                table: "QuestionVersions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Exact");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MatchRule",
                schema: "questionBank",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "MatchRule",
                schema: "questionBank",
                table: "QuestionVersions");
        }
    }
}
