using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttemptActiveSection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveSectionOrder",
                schema: "examRuntime",
                table: "Attempts",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveSectionOrder",
                schema: "examRuntime",
                table: "Attempts");
        }
    }
}
