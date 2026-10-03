using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.QuestionBank.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PinnedOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing options are not pinned, so every question keeps shuffling exactly as it did.
            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                schema: "questionBank",
                table: "QuestionOptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPinned",
                schema: "questionBank",
                table: "QuestionOptions");
        }
    }
}
