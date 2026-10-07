using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuestionLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every question written before languages existed is English.
            migrationBuilder.AddColumn<string>(
                name: "Language",
                schema: "questionBank",
                table: "Questions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "en");

            migrationBuilder.AddColumn<Guid>(
                name: "TranslationGroupId",
                schema: "questionBank",
                table: "Questions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // A question with no translations is alone in a group whose id is its own, so the unique index below holds from the start.
            migrationBuilder.Sql("UPDATE \"questionBank\".\"Questions\" SET \"TranslationGroupId\" = \"Id\";");

            migrationBuilder.CreateIndex(
                name: "IX_Questions_TranslationGroupId_Language",
                schema: "questionBank",
                table: "Questions",
                columns: new[] { "TranslationGroupId", "Language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Questions_TranslationGroupId_Language",
                schema: "questionBank",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "TranslationGroupId",
                schema: "questionBank",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "Language",
                schema: "questionBank",
                table: "Questions");
        }
    }
}
