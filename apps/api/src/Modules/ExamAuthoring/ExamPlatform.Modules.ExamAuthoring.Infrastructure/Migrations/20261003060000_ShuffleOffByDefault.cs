using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure.Migrations
{
    /// <summary>
    /// The two shuffle flags were stored as on for every exam but nothing read them: the runtime shuffled from the second attempt
    /// on whatever they said. Now that the author can set them and they are honoured, "on" means "shuffle the first attempt too",
    /// so every existing exam is set to off to keep behaving as it did. Data only; the model does not change.
    /// </summary>
    public partial class ShuffleOffByDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"examAuthoring\".\"Exams\" SET \"Config_ShuffleQuestions\" = FALSE, \"Config_ShuffleOptions\" = FALSE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old values were the unused default, so there is nothing worth restoring.
        }
    }
}
