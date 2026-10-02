using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RichQuestionText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Text",
                schema: "questionBank",
                table: "Questions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000);

            // Question text is HTML from now on. Every existing row is plain text written before that, so it is
            // escaped and wrapped (line breaks kept) to render exactly as it did: a question containing "a < b"
            // must not be read as a tag.
            migrationBuilder.Sql("""
                UPDATE "questionBank"."Questions"
                SET "Text" = '<p>'
                    || replace(replace(replace(replace(replace("Text", E'\r', ''), '&', '&amp;'), '<', '&lt;'), '>', '&gt;'), E'\n', '<br>')
                    || '</p>';
                """);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Only the column type is restored. The HTML already stored is not turned back into plain text, and the
        /// change fails if any text is longer than the old 4000-character limit; rolling back after rich questions
        /// exist is not supported.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Text",
                schema: "questionBank",
                table: "Questions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
