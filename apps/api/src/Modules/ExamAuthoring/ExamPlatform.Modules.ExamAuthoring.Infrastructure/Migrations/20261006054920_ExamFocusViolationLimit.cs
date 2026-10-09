using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExamFocusViolationLimit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Config_FocusViolationLimit",
                schema: "examAuthoring",
                table: "Exams",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Config_FocusViolationLimit",
                schema: "examAuthoring",
                table: "Exams");
        }
    }
}
