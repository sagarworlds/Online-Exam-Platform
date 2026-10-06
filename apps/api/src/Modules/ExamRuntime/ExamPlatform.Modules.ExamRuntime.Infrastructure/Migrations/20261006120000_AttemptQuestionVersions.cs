using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttemptQuestionVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An attempt made before versions were recorded has none, and keeps reading its questions as they are now.
            migrationBuilder.AddColumn<string>(
                name: "QuestionVersions",
                schema: "examRuntime",
                table: "Attempts",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuestionVersions",
                schema: "examRuntime",
                table: "Attempts");
        }
    }
}
