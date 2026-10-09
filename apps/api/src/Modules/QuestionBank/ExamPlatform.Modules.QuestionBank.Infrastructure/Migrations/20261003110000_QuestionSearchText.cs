using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class QuestionSearchText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                schema: "questionBank",
                table: "Questions",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Questions stored before searching existed get their readable text from the markup by dropping every tag. It is an
            // approximation (entities such as &amp; stay as written); a question is indexed exactly the next time it is edited.
            migrationBuilder.Sql(
                "UPDATE \"questionBank\".\"Questions\" SET \"SearchText\" = btrim(regexp_replace(\"Text\", '<[^>]*>', ' ', 'g'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchText",
                schema: "questionBank",
                table: "Questions");
        }
    }
}
