using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WidenTypedAnswer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A typed answer may now run to 10,000 characters (TypedAnswer.MaxLength), as long as the longest accepted answer.
            migrationBuilder.AlterColumn<string>(
                name: "AnswerText",
                schema: "examRuntime",
                table: "AttemptAnswers",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Only the column type is restored. The change fails if any typed answer is longer than the old 1000-character limit,
        /// so rolling back after longer answers exist is not supported.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "AnswerText",
                schema: "examRuntime",
                table: "AttemptAnswers",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);
        }
    }
}
