using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExamContentProtection : Migration
    {
        /// <inheritdoc />
        // Existing exams get the protection on, as a new exam does: FR-23 asks for it, and the author can lift it per exam.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Config_ContentProtection",
                schema: "examAuthoring",
                table: "Exams",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Config_ContentProtection",
                schema: "examAuthoring",
                table: "Exams");
        }
    }
}
