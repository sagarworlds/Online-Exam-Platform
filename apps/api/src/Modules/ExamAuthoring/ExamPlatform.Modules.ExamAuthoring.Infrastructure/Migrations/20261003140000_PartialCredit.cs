using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamAuthoring.Infrastructure.Migrations
{
    /// <summary>Adds the marking scheme's partial-credit switch; every existing exam keeps its all-or-nothing marking.</summary>
    public partial class PartialCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Config_MarkingScheme_PartialCredit",
                schema: "examAuthoring",
                table: "Exams",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Config_MarkingScheme_PartialCredit",
                schema: "examAuthoring",
                table: "Exams");
        }
    }
}
