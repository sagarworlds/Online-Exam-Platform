using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuestionDuplicateKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TextKey",
                schema: "questionBank",
                table: "Questions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // Existing questions get the key an edit would give them: a hash of the readable text's letters and digits, lower-cased.
            // A question with nothing readable (only a picture) keeps the empty key, which never matches anything.
            migrationBuilder.Sql(
                "UPDATE \"questionBank\".\"Questions\" SET \"TextKey\" = encode(sha256(convert_to(lower(regexp_replace(\"SearchText\", '[^[:alnum:]]+', '', 'g')), 'UTF8')), 'hex') " +
                "WHERE regexp_replace(\"SearchText\", '[^[:alnum:]]+', '', 'g') <> '';");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_TextKey",
                schema: "questionBank",
                table: "Questions",
                column: "TextKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Questions_TextKey",
                schema: "questionBank",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "TextKey",
                schema: "questionBank",
                table: "Questions");
        }
    }
}
